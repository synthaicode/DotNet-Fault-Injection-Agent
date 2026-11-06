using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

internal static class Program
{
    private static readonly ConcurrentDictionary<int, Rule> Rules = new();

    private static void Main(string[] args)
    {
        var path = Path.GetFullPath(args[0]);
        if (File.Exists(path))
            throw new IOException("Expected an unused path.");

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                WriteSelected(path, "probe");
                Console.WriteLine($"attempt={attempt} success");
            }
            catch (IOException error)
            {
                Console.WriteLine($"attempt={attempt} IOException: {error.Message}");
            }
            Console.WriteLine($"attempt={attempt} exists={File.Exists(path)}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteSelected(string path, string content)
    {
        File.WriteAllText(path, content);
    }

    private static void WriteWithFault(string path, string content, int token)
    {
        var rule = Rules.GetOrAdd(token, key => new Rule(key));
        if (string.Equals(path, rule.Path, StringComparison.OrdinalIgnoreCase) && rule.TryConsume())
        {
            Console.Error.WriteLine($"fault fired: methodToken={token} io-error-before-write");
            throw new IOException("Injected before File.WriteAllText.");
        }

        File.WriteAllText(path, content);
    }

    private sealed class Rule
    {
        internal readonly string Path;
        private int remaining;

        internal Rule(int token)
        {
            var fields = (Environment.GetEnvironmentVariable("WINDOWS_CHAOS_VALIDATED_RULE_" + token) ?? "").Split('\n');
            if (fields.Length == 2 && int.TryParse(fields[1], out remaining))
                Path = System.IO.Path.GetFullPath(fields[0]);
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
