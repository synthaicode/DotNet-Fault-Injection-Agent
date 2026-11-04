using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using ServiceBase = System.ServiceProcess.ServiceBase;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private sealed class InjectionRule
    {
        internal readonly string[] Fields;
        private int remaining;

        internal InjectionRule(int token)
        {
            Fields = (Environment.GetEnvironmentVariable("WINDOWS_CHAOS_VALIDATED_RULE_" + token) ?? "").Split('\n');
            remaining = (Fields.Length == 4 || Fields.Length == 5) && int.TryParse(Fields[3], out int limit) ? limit : 0;
        }

        internal static bool Enabled = true;

        internal bool TryConsume()
        {
            int count;
            do
            {
                count = Volatile.Read(ref remaining);
                if (count <= 0)
                    return false;
            }
            while (Interlocked.CompareExchange(ref remaining, count - 1, count) != count);

            return true;
        }
    }

    private static readonly ConcurrentDictionary<int, InjectionRule> Rules =
        new ConcurrentDictionary<int, InjectionRule>();

    private static ManualResetEventSlim delayStarted;

    private static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--service")
        {
            ServiceBase.Run(new ServiceProbe());
            return;
        }

        if (args.Length == 1 && args[0] == "--concurrent")
        {
            RunConcurrent();
            return;
        }

        bool controlled = args.Length == 1 && args[0] == "--controlled";
        using (var connection = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Initial Catalog=tempdb;Integrated Security=true"))
        {
            connection.Open();
            using (var setup = connection.CreateCommand())
            {
                setup.CommandText = "CREATE TABLE #Received (Value int NOT NULL)";
                setup.ExecuteNonQuery();
                setup.CommandText = "CREATE PROCEDURE #ConfirmOrder AS INSERT INTO #Received VALUES (1)";
                setup.ExecuteNonQuery();
                setup.CommandText = "CREATE PROCEDURE #OtherOrder AS INSERT INTO #Received VALUES (2)";
                setup.ExecuteNonQuery();
            }

            using (var command = new SqlCommand("#ConfirmOrder", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                if (controlled)
                {
                    RunControlled(command);
                    return;
                }

                Run("other", () => Other(command));
                command.CommandText = "#OtherOrder";
                Run("selected with different procedure", () => Selected(command));
                if (args.Length == 1 && args[0] == "--three-sites")
                {
                    Run("third", () => Third(command));
                    Run("third again", () => Third(command));
                }
                command.CommandText = "#ConfirmOrder";
                Run("selected", () => Selected(command));
                Run("selected again", () => Selected(command));
            }

            using (var count = new SqlCommand("SELECT COUNT(*) FROM #Received", connection))
            {
                Console.WriteLine("DB rows: " + count.ExecuteScalar());
            }
        }
    }

    private sealed class ServiceProbe : ServiceBase
    {
        internal ServiceProbe()
        {
            ServiceName = "WindowsChaosAgentProbe";
        }

        protected override void OnStart(string[] args)
        {
            if (args.Length < 1 || args.Length > 3 || !Path.IsPathRooted(args[0]) ||
                (args.Length == 3 && args[2] != "overlap" && args[2] != "retry"))
                throw new ArgumentException("Pass an absolute log path and optionally a SQL Server instance and overlap or retry mode.");
            var settings = new SqlConnectionStringBuilder
            {
                DataSource = args.Length >= 2 ? args[1] : @"(localdb)\MSSQLLocalDB",
                InitialCatalog = "tempdb",
                IntegratedSecurity = true,
                ConnectTimeout = 10
            };
            bool retry = args.Length == 3 && args[2] == "retry";
            if (args.Length == 3 && !retry)
            {
                using (var output = new StreamWriter(args[0]))
                    RunOverlap(settings.ConnectionString, TextWriter.Synchronized(output));
                return;
            }

            using (var output = new StreamWriter(args[0]))
            using (var connection = new SqlConnection(settings.ConnectionString))
            using (var command = new SqlCommand("#ConfirmOrder", connection))
            {
                if (args.Length >= 2)
                {
                    connection.Open();
                    using (var setup = connection.CreateCommand())
                    {
                        setup.CommandText = "CREATE TABLE #Received (Id int IDENTITY)";
                        setup.ExecuteNonQuery();
                        setup.CommandText = "CREATE PROCEDURE #ConfirmOrder AS INSERT INTO #Received DEFAULT VALUES";
                        setup.ExecuteNonQuery();
                    }
                }
                command.CommandType = CommandType.StoredProcedure;
                Console.SetError(output);
                try
                {
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        bool shouldRetry = false;
                        try
                        {
                            output.WriteLine($"attempt={attempt} success: rows affected={Selected(command)}");
                        }
                        catch (TimeoutException error) when (!retry || attempt == 1)
                        {
                            shouldRetry = retry;
                            output.WriteLine($"attempt={attempt} TimeoutException: {error.Message}");
                        }
                        catch (InvalidOperationException error) when (!retry)
                        {
                            output.WriteLine($"attempt={attempt} InvalidOperationException: {error.Message}");
                        }

                        if (connection.State == ConnectionState.Open)
                        {
                            using (var count = new SqlCommand("SELECT COUNT(*) FROM #Received", connection))
                                output.WriteLine($"attempt={attempt} DB rows={count.ExecuteScalar()}");
                        }

                        if (retry)
                        {
                            if (!shouldRetry)
                                break;
                            output.WriteLine("retry: scheduled after TimeoutException");
                        }
                    }
                }
                finally
                {
                    Console.SetError(TextWriter.Null);
                }
            }
        }
    }

    private static void RunOverlap(string connectionString, TextWriter output)
    {
        var table = "##ChaosOverlap_" + Guid.NewGuid().ToString("N");
        using (var first = new SqlConnection(connectionString))
        using (var second = new SqlConnection(connectionString))
        using (var observer = new SqlConnection(connectionString))
        using (var started = new ManualResetEventSlim())
        {
            first.Open();
            second.Open();
            observer.Open();
            using (var create = new SqlCommand("CREATE TABLE " + table + " (Worker char(1) NOT NULL PRIMARY KEY)", first))
                create.ExecuteNonQuery();
            foreach (var connection in new[] { first, second })
            {
                using (var setup = new SqlCommand("CREATE PROCEDURE #ConfirmOrder @Worker char(1) AS INSERT INTO " +
                    table + " VALUES (@Worker)", connection))
                    setup.ExecuteNonQuery();
            }

            var gate = new object();
            var order = new System.Collections.Generic.List<string>();
            var completed = 0;
            Action<SqlConnection, string> execute = (connection, worker) =>
            {
                using (var command = new SqlCommand("#ConfirmOrder", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.Add("@Worker", SqlDbType.Char, 1).Value = worker;
                    if (Selected(command) != 1)
                        throw new InvalidOperationException("Expected one affected row.");
                }

                lock (gate)
                {
                    order.Add(worker);
                    using (var count = new SqlCommand("SELECT COUNT(*) FROM " + table, observer))
                    {
                        var rows = (int)count.ExecuteScalar();
                        completed++;
                        output.WriteLine($"completed={worker} application={completed} db={rows}");
                        if (rows != completed)
                            throw new InvalidOperationException("Application and database counts diverged.");
                    }
                }
            };

            var previousError = Console.Error;
            Console.SetError(output);
            delayStarted = started;
            try
            {
                output.WriteLine("initial application=0 db=0");
                var workerA = Task.Run(() => execute(first, "A"));
                if (!started.Wait(TimeSpan.FromSeconds(8)))
                {
                    workerA.GetAwaiter().GetResult();
                    throw new InvalidOperationException("Agent did not signal the first worker delay.");
                }
                var workerB = Task.Run(() => execute(second, "B"));
                Task.WaitAll(workerA, workerB);
                if (order.Count != 2 || order[0] != "B" || order[1] != "A")
                    throw new InvalidOperationException("Expected completion order B then A.");
            }
            finally
            {
                delayStarted = null;
                Console.SetError(previousError);
            }
        }
    }

    private static void RunControlled(SqlCommand command)
    {
        InjectionRule.Enabled = false;
        Console.WriteLine("ready: disabled");
        string request;
        while ((request = Console.ReadLine()) != null)
        {
            switch (request)
            {
                case "enable":
                    InjectionRule.Enabled = true;
                    Console.WriteLine("enabled");
                    break;
                case "disable":
                    InjectionRule.Enabled = false;
                    Console.WriteLine("disabled");
                    break;
                case "call":
                    Run("selected", () => Selected(command));
                    break;
                case "quit":
                    return;
                default:
                    Console.WriteLine("unknown command");
                    break;
            }
        }
    }

    private static void RunConcurrent()
    {
        const string connectionString = @"Server=(localdb)\MSSQLLocalDB;Initial Catalog=tempdb;Integrated Security=true";
        using (var first = new SqlConnection(connectionString))
        using (var second = new SqlConnection(connectionString))
        using (var gate = new Barrier(2))
        {
            first.Open();
            second.Open();
            PrepareConcurrentSession(first);
            PrepareConcurrentSession(second);
            Task.WaitAll(
                Task.Run(() => RunConcurrentCall(first, gate, "first")),
                Task.Run(() => RunConcurrentCall(second, gate, "second")));
        }
    }

    private static void PrepareConcurrentSession(SqlConnection connection)
    {
        using (var command = new SqlCommand("CREATE PROCEDURE #ConfirmOrder AS SET NOCOUNT ON", connection))
        {
            command.ExecuteNonQuery();
        }
    }

    private static void RunConcurrentCall(SqlConnection connection, Barrier gate, string label)
    {
        using (var command = new SqlCommand("#ConfirmOrder", connection))
        {
            command.CommandType = CommandType.StoredProcedure;
            gate.SignalAndWait();
            Run(label, () => Selected(command));
        }
    }

    private static void Run(string label, Func<int> action)
    {
        try
        {
            Console.WriteLine(label + ": " + action());
        }
        catch (TimeoutException)
        {
            Console.WriteLine(label + ": TimeoutException");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Selected(SqlCommand command)
    {
        return command.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Other(SqlCommand command)
    {
        return command.ExecuteNonQuery();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Third(SqlCommand command)
    {
        return command.ExecuteNonQuery();
    }

    // The profiler supplies the token of the method containing the rewritten call.
    private static int FailBeforeSend(SqlCommand command, int token)
    {
        var rule = Rules.GetOrAdd(token, key => new InjectionRule(key));
        var fields = rule.Fields;
        if (InjectionRule.Enabled && (fields.Length == 4 || fields.Length == 5) &&
            command != null &&
            command.Connection != null &&
            command.CommandType == CommandType.StoredProcedure &&
            string.Equals(command.Connection.DataSource, fields[0], StringComparison.OrdinalIgnoreCase) &&
            string.Equals(command.Connection.Database, fields[1], StringComparison.Ordinal) &&
            string.Equals(command.CommandText, fields[2], StringComparison.Ordinal) &&
            rule.TryConsume())
        {
            if (fields.Length == 4)
            {
                Console.Error.WriteLine("fault fired: methodToken=" + token + " timeout-before-send");
                throw new TimeoutException("Injected before ExecuteNonQuery.");
            }

            Console.Error.WriteLine("fault fired: methodToken=" + token + " delay-before-send");
            delayStarted?.Set();
            Thread.Sleep(int.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture));
        }

        return command.ExecuteNonQuery();
    }
}
