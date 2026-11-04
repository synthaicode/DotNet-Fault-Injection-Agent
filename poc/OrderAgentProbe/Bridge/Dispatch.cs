using System;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Threading;

namespace ChaosOrderBridge
{
    public static class Dispatch
    {
        private static readonly ConcurrentDictionary<int, Rule> Rules = new ConcurrentDictionary<int, Rule>();

        public static int ExecuteNonQuery(DbCommand command, int methodToken)
        {
            var rule = Rules.GetOrAdd(methodToken, token => new Rule(token));
            var fields = rule.Fields;
            if (fields.Length == 4 && command != null && command.Connection != null &&
                command.CommandType == CommandType.StoredProcedure &&
                string.Equals(command.Connection.DataSource, fields[0], StringComparison.OrdinalIgnoreCase) &&
                string.Equals(command.Connection.Database, fields[1], StringComparison.Ordinal) &&
                string.Equals(command.CommandText, fields[2], StringComparison.Ordinal) && rule.TryConsume())
            {
                Console.Error.WriteLine("fault fired: methodToken=" + methodToken + " timeout-before-send");
                throw new TimeoutException("Windows Chaos Agent: timeout before SQL send.");
            }
            return command.ExecuteNonQuery();
        }

        private sealed class Rule
        {
            internal readonly string[] Fields;
            private int remaining;

            internal Rule(int token)
            {
                Fields = (Environment.GetEnvironmentVariable("WINDOWS_CHAOS_VALIDATED_RULE_" + token) ?? "").Split('\n');
                remaining = Fields.Length == 4 && int.TryParse(Fields[3], out int limit) ? limit : 0;
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
