using System;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static void Main()
    {
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
                Run("selected", () => Selected(command));
                Run("other", () => Other(command));
                command.CommandText = "#OtherOrder";
                Run("selected with different procedure", () => Selected(command));
            }

            using (var count = new SqlCommand("SELECT COUNT(*) FROM #Received", connection))
            {
                Console.WriteLine("DB rows: " + count.ExecuteScalar());
            }
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
        if (command != null &&
            command.Connection != null &&
            command.CommandType == CommandType.StoredProcedure &&
            string.Equals(command.Connection.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(command.Connection.Database, "tempdb", StringComparison.Ordinal) &&
            string.Equals(command.CommandText, "#ConfirmOrder", StringComparison.Ordinal))
        {
            throw new TimeoutException("Injected before ExecuteNonQuery.");
        }

        return command.ExecuteNonQuery();
    }
}
