using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Diagnostics;

var directory = args[0];
Directory.CreateDirectory(directory);
if (args.Length > 2 && args[2] == "mutate-plan")
{
    var planPath = Environment.GetEnvironmentVariable("WINDOWS_CHAOS_PLAN")!;
    File.AppendAllText(planPath, " ");
}
var tasks = Enumerable.Range(0, args.Length > 1 ? int.Parse(args[1]) : 3).Select(index => Task.Run(() =>
{
    var path = Path.Combine(directory, $"selected-{index}.txt");
    string? error = null;
    var watch = Stopwatch.StartNew();
    try { Fixture.Selected(path); }
    catch (Exception exception) { error = exception.GetType().FullName + ": " + exception.Message; }
    Console.WriteLine(JsonSerializer.Serialize(new { index, error, exists = File.Exists(path), elapsedMs = watch.ElapsedMilliseconds }));
}));
await Task.WhenAll(tasks);
Fixture.Other(Path.Combine(directory, "other.txt"));

static class Fixture
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Selected(string path)
    {
        File.WriteAllText(path, "selected");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Other(string path)
    {
        File.WriteAllText(path, "other");
    }
}
