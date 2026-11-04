using System;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

internal static class Program
{
    private sealed class InjectionRule
    {
        internal readonly string[] Fields;
        private int remaining;

        internal InjectionRule(int slot)
        {
            Fields = (Environment.GetEnvironmentVariable("WINDOWS_CHAOS_VALIDATED_RULE_" + slot) ?? "").Split('\n');
            remaining = Fields.Length == 5 && int.TryParse(Fields[3], out int limit) ? limit : 0;
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

    private static class Rules
    {
        internal static readonly InjectionRule First = new InjectionRule(0);
        internal static readonly InjectionRule Second = new InjectionRule(1);
    }

    private static void Main(string[] args)
    {
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

    // The profiler redirects the selected call to this test-only helper.
    private static int FailBeforeSend(SqlCommand command)
    {
        return ApplyRule(command, Rules.First);
    }

    private static int FailBeforeSendSecond(SqlCommand command)
    {
        return ApplyRule(command, Rules.Second);
    }

    private static int ApplyRule(SqlCommand command, InjectionRule rule)
    {
        var fields = rule.Fields;
        if (InjectionRule.Enabled && fields.Length == 5 &&
            command != null &&
            command.Connection != null &&
            command.CommandType == CommandType.StoredProcedure &&
            string.Equals(command.Connection.DataSource, fields[0], StringComparison.OrdinalIgnoreCase) &&
            string.Equals(command.Connection.Database, fields[1], StringComparison.Ordinal) &&
            string.Equals(command.CommandText, fields[2], StringComparison.Ordinal) &&
            rule.TryConsume())
        {
            Console.Error.WriteLine("fault fired: methodToken=" + fields[4] + " timeout-before-send");
            throw new TimeoutException("Injected before ExecuteNonQuery.");
        }

        return command.ExecuteNonQuery();
    }
}
