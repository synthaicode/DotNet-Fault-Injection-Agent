using System;
using System.Data;
using System.Data.SqlClient;
using System.Runtime.CompilerServices;

internal static class Program
{
    private static void Main()
    {
        using (var connection = new SqlConnection(@"Server=(localdb)\MSSQLLocalDB;Integrated Security=true"))
        {
            connection.Open();
            using (var setup = connection.CreateCommand())
            {
                setup.CommandText = "CREATE TABLE #Received (Value int NOT NULL)";
                setup.ExecuteNonQuery();
                setup.CommandText = "CREATE PROCEDURE #ConfirmOrder AS INSERT INTO #Received VALUES (1)";
                setup.ExecuteNonQuery();
            }

            using (var command = new SqlCommand("#ConfirmOrder", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                Run("selected", () => Selected(command));
                Run("other", () => Other(command));
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
        throw new TimeoutException("Injected before ExecuteNonQuery.");
    }
}
