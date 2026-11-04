using System.Diagnostics;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

internal static class ProjectInput
{
    internal static CSharpCompilation Load(string path, string referenceDirectory)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "msbuild", path, "-nologo", "-p:Configuration=Release",
            "-p:TargetFrameworkVersion=v4.8", "-getItem:Compile,Reference,ProjectReference,PackageReference",
            "-getProperty:DefineConstants,OutputType,AssemblyName" })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start MSBuild.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidDataException(errors.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());

        using var result = JsonDocument.Parse(output.GetAwaiter().GetResult());
        var properties = result.RootElement.GetProperty("Properties");
        var items = result.RootElement.GetProperty("Items");
        if (items.GetProperty("ProjectReference").GetArrayLength() != 0 ||
            items.GetProperty("PackageReference").GetArrayLength() != 0)
            throw new InvalidDataException("ProjectReference and PackageReference are unsupported in this PoC.");

        var symbols = properties.GetProperty("DefineConstants").GetString()
            .Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var options = CSharpParseOptions.Default.WithPreprocessorSymbols(symbols);
        var trees = items.GetProperty("Compile").EnumerateArray().Select(item =>
        {
            var file = item.GetProperty("FullPath").GetString();
            using var stream = File.OpenRead(file);
            return CSharpSyntaxTree.ParseText(
                SourceText.From(stream, checksumAlgorithm: SourceHashAlgorithm.Sha256), options, file);
        }).ToArray();
        var references = items.GetProperty("Reference").EnumerateArray()
            .Select(item => item.GetProperty("Identity").GetString().Split(',')[0])
            .Append("mscorlib").Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => MetadataReference.CreateFromFile(Path.Combine(referenceDirectory, name + ".dll")));

        var kind = properties.GetProperty("OutputType").GetString() switch
        {
            "WinExe" => OutputKind.WindowsApplication,
            "Exe" => OutputKind.ConsoleApplication,
            _ => OutputKind.DynamicallyLinkedLibrary
        };
        return CSharpCompilation.Create(properties.GetProperty("AssemblyName").GetString(), trees, references,
            new CSharpCompilationOptions(kind));
    }

    private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields()
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null))
        .ToDictionary(opcode => unchecked((ushort)opcode.Value));

    internal static int FindCall(byte[] il, int start, int end, MetadataReader metadata, bool file, bool network)
    {
        var matches = new List<int>();
        for (var offset = 0; offset < end;)
        {
            var instruction = offset;
            ushort code = il[offset++];
            if (code == 0xfe)
                code = (ushort)(0xfe00 | il[offset++]);
            var opcode = Opcodes[code];
            if (instruction >= start && opcode == (file ? OpCodes.Call : OpCodes.Callvirt) &&
                IsExpectedCall(metadata, MetadataTokens.EntityHandle(
                    System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, 4))), file, network))
                matches.Add(instruction);

            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + 4 *
                    System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset, 4))),
                _ => 4
            };
        }
        if (matches.Count != 1)
            throw new InvalidDataException("Expected exactly one matching call in the PDB sequence point.");
        return matches[0];
    }

    private static bool IsExpectedCall(MetadataReader metadata, EntityHandle token, bool file, bool network)
    {
        if (token.Kind != HandleKind.MemberReference)
            return false;
        var member = metadata.GetMemberReference((MemberReferenceHandle)token);
        if (member.Parent.Kind != HandleKind.TypeReference ||
            metadata.GetString(member.Name) != (file ? "WriteAllText" : network ? "Connect" : "ExecuteNonQuery") ||
            !metadata.GetBlobBytes(member.Signature).SequenceEqual(file ? new byte[] { 0, 2, 1, 0x0e, 0x0e } : network ? new byte[] { 0x20, 2, 1, 0x0e, 8 } : new byte[] { 0x20, 0, 0x08 }))
            return false;
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        return metadata.GetString(type.Namespace) == (file ? "System.IO" : network ? "System.Net.Sockets" : "System.Data.Common") &&
            metadata.GetString(type.Name) == (file ? "File" : network ? "TcpClient" : "DbCommand");
    }
}
