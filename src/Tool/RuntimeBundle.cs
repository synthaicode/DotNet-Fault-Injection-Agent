using System.Security.Cryptography;
using System.Text.Json;

internal static class RuntimeBundle
{
    internal static int Run(string[] args)
    {
        try
        {
            var rid = OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : "";
            if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64) rid = "";
            var root = Path.Combine(AppContext.BaseDirectory, "runtime");
            if (args.SequenceEqual(new[] { "list" }))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { bundled = new[] { "win-x64", "linux-x64" }.Where(value => Directory.Exists(Path.Combine(root, value))).ToArray() }));
                return 0;
            }
            string output = null;
            var options = new HashSet<string>();
            if (args.Length == 0 || args[0] != "export" || args.Length % 2 != 1)
                throw new ArgumentException("Usage: runtime export [--rid win-x64|linux-x64] --output <new-directory>");
            for (var i = 1; i < args.Length; i += 2)
            {
                if (!options.Add(args[i])) throw new ArgumentException("Duplicate option.");
                if (args[i] == "--output") output = args[i + 1];
                else if (args[i] == "--rid") rid = args[i + 1];
                else throw new ArgumentException($"Unknown option: {args[i]}");
            }
            if (rid is not ("win-x64" or "linux-x64") || string.IsNullOrWhiteSpace(output))
                throw new ArgumentException("Select a supported RID and an output directory.");
            var source = Path.Combine(root, rid);
            var destination = Path.GetFullPath(output);
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException("Export requires a new directory; existing files are never replaced.");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")));
            var files = manifest.RootElement.GetProperty("files").EnumerateObject().ToArray();
            foreach (var file in files)
            {
                if (Path.GetFileName(file.Name) != file.Name || file.Name is "." or "..")
                    throw new InvalidDataException("Invalid bundled file name.");
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(source, file.Name))));
                if (!hash.Equals(file.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Bundled file checksum mismatch: {file.Name}");
            }
            Directory.CreateDirectory(destination);
            foreach (var file in files) File.Copy(Path.Combine(source, file.Name), Path.Combine(destination, file.Name));
            File.Copy(Path.Combine(source, "manifest.json"), Path.Combine(destination, "manifest.json"));
            Console.WriteLine(JsonSerializer.Serialize(new { runtime = rid, directory = destination,
                launcher = Path.Combine(destination, rid == "win-x64" ? "Invoke-FaultInjection.ps1" : "Invoke-FaultInjection.sh") }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
