using System.Security.Cryptography;
using System.Text.Json;

internal static class RuntimeBundle
{
    internal static int Run(string[] args)
    {
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "runtime", "win-x64");
            if (args.SequenceEqual(new[] { "list" }))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { bundled = Directory.Exists(source) ? new[] { "win-x64" } : Array.Empty<string>() }));
                return 0;
            }
            if (args.Length != 3 || args[0] != "export" || args[1] != "--output")
                throw new ArgumentException("Usage: dotnet-fault-injection runtime list | runtime export --output <new-directory>");
            var destination = Path.GetFullPath(args[2]);
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
            Console.WriteLine(JsonSerializer.Serialize(new { runtime = "win-x64", directory = destination,
                launcher = Path.Combine(destination, "Invoke-FaultInjection.ps1") }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
