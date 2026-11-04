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
            remaining = Fields.Length == 4 && int.TryParse(Fields[3], out int limit) ? limit : 0;
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
            if (args.Length != 1 || !Path.IsPathRooted(args[0]))
                throw new ArgumentException("Pass an absolute log path.");
            using (var output = new StreamWriter(args[0]))
            using (var connection = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Initial Catalog=tempdb;Integrated Security=true"))
            using (var command = new SqlCommand("#ConfirmOrder", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                Console.SetError(output);
                try
                {
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        try
                        {
                            Selected(command);
                        }
                        catch (TimeoutException error)
                        {
                            output.WriteLine($"attempt={attempt} TimeoutException: {error.Message}");
                        }
                        catch (InvalidOperationException error)
                        {
                            output.WriteLine($"attempt={attempt} InvalidOperationException: {error.Message}");
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
        if (InjectionRule.Enabled && fields.Length == 4 &&
            command != null &&
            command.Connection != null &&
            command.CommandType == CommandType.StoredProcedure &&
            string.Equals(command.Connection.DataSource, fields[0], StringComparison.OrdinalIgnoreCase) &&
            string.Equals(command.Connection.Database, fields[1], StringComparison.Ordinal) &&
            string.Equals(command.CommandText, fields[2], StringComparison.Ordinal) &&
            rule.TryConsume())
        {
            Console.Error.WriteLine("fault fired: methodToken=" + token + " timeout-before-send");
            throw new TimeoutException("Injected before ExecuteNonQuery.");
        }

        return command.ExecuteNonQuery();
    }
}
