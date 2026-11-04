using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;

internal sealed class SyncService : ServiceBase
{
    private const string Name = "WindowsChaosSyncProbe";

    private SyncService()
    {
        ServiceName = Name;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--console")
        {
            RunTrials(Console.Out);
            return 0;
        }

        ServiceBase.Run(new SyncService());
        return 0;
    }

    protected override void OnStart(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathRooted(args[0]))
            throw new ArgumentException("Pass an absolute output log path as the service start argument.");

        using (var output = new StreamWriter(args[0], append: false))
            RunTrials(output);
    }

    private static void RunTrials(TextWriter output)
    {
        var ordinary = new OrdinaryFlag();
        var withVolatile = new VolatileFlag();
        RunTrial("ordinary", ordinary.Read, ordinary.Publish, output);
        RunTrial("volatile", withVolatile.Read, withVolatile.Publish, output);
    }

    private static void RunTrial(string name, Func<bool> read, Action publish, TextWriter output)
    {
        using (var firstReadDone = new ManualResetEventSlim())
        {
            var writer = new Thread(() =>
            {
                firstReadDone.Wait();
                publish();
            }) { IsBackground = true };

            writer.Start();
            bool before = read();
            firstReadDone.Set();
            writer.Join();

            bool after = read();
            output.WriteLine($"{name}: first read={before}; recheck after publication={after}");
            if (before || !after)
                throw new InvalidOperationException("Unexpected flag observation.");
        }
    }

    private sealed class OrdinaryFlag
    {
        private bool value;
        internal bool Read() => value;
        internal void Publish() => value = true;
    }

    private sealed class VolatileFlag
    {
        private volatile bool value;
        internal bool Read() => value;
        internal void Publish() => value = true;
    }
}
