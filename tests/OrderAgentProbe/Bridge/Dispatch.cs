using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Threading;

namespace DotNetFaultInjectionAgent.OrderBridge
{
    public static class Dispatch
    {
        private static readonly ConcurrentDictionary<int, Rule> Rules = new ConcurrentDictionary<int, Rule>();

        public static int ExecuteNonQuery(DbCommand command, int methodToken)
        {
            var rule = Rules.GetOrAdd(methodToken, token => new Rule(token));
            var fields = rule.Fields;
            var delayed = false;
            var matches = (fields.Length == 4 || fields.Length == 5) && command != null && command.Connection != null &&
                command.CommandType == CommandType.StoredProcedure &&
                string.Equals(command.Connection.DataSource, fields[0], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(command.Connection.Database, fields[1], StringComparison.Ordinal) &&
                string.Equals(command.CommandText, fields[2], StringComparison.Ordinal);
            if (matches && rule.TryConsume())
            {
                if (fields.Length == 4)
                {
                    Console.Error.WriteLine("fault fired: methodToken=" + methodToken + " timeout-before-send");
                    throw new TimeoutException("DotNet Fault Injection Agent: timeout before SQL send.");
                }

                delayed = true;
                Console.Error.WriteLine("fault fired: methodToken=" + methodToken + " delay-before-send");
                Thread.Sleep(int.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture));
            }
            var result = command.ExecuteNonQuery();
            if (matches && fields.Length == 5)
                Console.Error.WriteLine("SQL completed: delayed=" + delayed);
            return result;
        }

        private sealed class Rule
        {
            internal readonly string[] Fields;
            private int remaining;

            internal Rule(int token)
            {
                Fields = (Environment.GetEnvironmentVariable("DOTNET_FAULT_INJECTION_VALIDATED_RULE_" + token) ?? "").Split('\n');
                remaining = (Fields.Length == 4 || Fields.Length == 5) && int.TryParse(Fields[3], out int limit) ? limit : 0;
            }

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
    }
}
