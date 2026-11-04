using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

try
{
    if (args.Length is < 4 or > 5)
        throw new ArgumentException("Usage: SourceMap <Program.cs> <SqlTarget.exe> <net48 reference directory> <list|source line> [plan.json]");

    var sourcePath = Path.GetFullPath(args[0]);
    var exePath = Path.GetFullPath(args[1]);
    using var sourceStream = File.OpenRead(sourcePath);
    var source = SourceText.From(sourceStream, checksumAlgorithm: SourceHashAlgorithm.Sha256);
    var tree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
    var references = new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Data.dll" }
        .Select(name => MetadataReference.CreateFromFile(Path.Combine(args[2], name)));
    var compilation = CSharpCompilation.Create("SqlTarget", [tree], references,
        new CSharpCompilationOptions(OutputKind.ConsoleApplication));
    var errors = compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0)
        throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(error => error.ToString())));

    var model = compilation.GetSemanticModel(tree);
    // Limit the candidates to the single-return wrapper shape supported by this PoC.
    var candidates = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
        .Where(call => call.Parent is ReturnStatementSyntax { Parent: BlockSyntax block } &&
            block.Statements.Count == 1 && block.Parent is MethodDeclarationSyntax)
        .Select(call => (Call: call, Method: model.GetSymbolInfo(call).Symbol as IMethodSymbol))
        .Where(candidate => candidate.Method is { Name: "ExecuteNonQuery", Parameters.Length: 0 } &&
            candidate.Method.ContainingType.ToDisplayString() == "System.Data.SqlClient.SqlCommand")
        .Select(candidate => (candidate.Call,
            Caller: model.GetEnclosingSymbol(candidate.Call.SpanStart) as IMethodSymbol,
            Line: candidate.Call.GetLocation().GetLineSpan().StartLinePosition.Line + 1))
        .ToArray();

    if (args[3] == "list")
    {
        foreach (var candidate in candidates)
            Console.WriteLine($"{candidate.Line}: {candidate.Caller?.ToDisplayString()} -> SqlCommand.ExecuteNonQuery()");
        return 0;
    }

    if (!int.TryParse(args[3], out var line))
        throw new ArgumentException("Select a source line from the candidate list.");
    var selected = candidates.Single(candidate => candidate.Line == line);
    if (selected.Caller is not { IsStatic: true, Parameters.Length: 1 } caller ||
        caller.ContainingType.ToDisplayString() != "Program" ||
        caller.Parameters[0].Type.ToDisplayString() != "System.Data.SqlClient.SqlCommand" ||
        caller.ReturnType.SpecialType != SpecialType.System_Int32)
        throw new InvalidDataException("The selected method is outside the fixed SqlTarget fixture shape.");

    using var image = File.OpenRead(exePath);
    using var pe = new PEReader(image, PEStreamOptions.LeaveOpen);
    if (!pe.TryOpenAssociatedPortablePdb(exePath, path => File.Exists(path) ? File.OpenRead(path) : null,
        out var provider, out _) || provider is null)
        throw new InvalidDataException("A matching Portable PDB was not found.");
    using (provider)
    {
        var pdb = provider.GetMetadataReader();
        var metadata = pe.GetMetadataReader();
        var owner = metadata.TypeDefinitions.Single(handle =>
            metadata.GetString(metadata.GetTypeDefinition(handle).Name) == "Program");
        var methodHandle = metadata.GetTypeDefinition(owner).GetMethods().Single(handle =>
            metadata.GetString(metadata.GetMethodDefinition(handle).Name) == caller.Name);
        var method = metadata.GetMethodDefinition(methodHandle);
        var points = pdb.GetMethodDebugInformation(methodHandle.ToDebugInformationHandle());
        var point = points.GetSequencePoints().Single(item =>
            !item.IsHidden && item.StartLine == line && item.EndLine == line);
        var document = pdb.GetDocument(point.Document.IsNil ? points.Document : point.Document);
        var checksum = pdb.GetBlobBytes(document.Hash);
        var algorithm = pdb.GetGuid(document.HashAlgorithm);
        var expected = algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16")
            ? source.GetChecksum().ToArray() : Array.Empty<byte>();
        if (expected.Length == 0 || !checksum.SequenceEqual(expected))
            throw new InvalidDataException("Source checksum does not match the PDB document.");

        // Release SqlTarget fixture: ldarg.0, callvirt <memberref>, ret.
        var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
        if (point.Offset != 0 || il is not [0x02, 0x6f, _, _, _, _, 0x2a])
            throw new InvalidDataException("Unexpected IL shape for the selected call.");
        var token = MetadataTokens.EntityHandle(BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(2, 4)));
        if (token.Kind != HandleKind.MemberReference)
            throw new InvalidDataException("The selected call does not reference an external member.");
        var member = metadata.GetMemberReference((MemberReferenceHandle)token);
        if (member.Parent.Kind != HandleKind.TypeReference ||
            metadata.GetString(member.Name) != "ExecuteNonQuery" ||
            !metadata.GetBlobBytes(member.Signature).SequenceEqual(new byte[] { 0x20, 0, 0x08 }))
            throw new InvalidDataException("Unexpected call signature.");
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (metadata.GetString(type.Namespace) != "System.Data.Common" ||
            metadata.GetString(type.Name) != "DbCommand")
            throw new InvalidDataException("Unexpected call declaring type.");

        image.Position = 0;
        var plan = new
        {
            version = 1,
            sha256 = Convert.ToHexString(SHA256.HashData(image)),
            mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid),
            methodToken = MetadataTokens.GetToken(methodHandle),
            ilOffset = 1,
            call = "System.Data.Common.DbCommand.ExecuteNonQuery()",
            fault = "timeout-before-send",
            server = @"(localdb)\MSSQLLocalDB",
            database = "tempdb",
            procedure = "#ConfirmOrder",
            maxFirings = 1
        };
        if (args.Length == 5)
        {
            using var output = new FileStream(args[4], FileMode.CreateNew);
            System.Text.Json.JsonSerializer.Serialize(output, plan,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return 0;
        }
        Console.WriteLine($"Source: {caller.ToDisplayString()} line {line}");
        Console.WriteLine($"SHA-256: {plan.sha256}");
        Console.WriteLine($"MVID: {plan.mvid}");
        Console.WriteLine($"Method: 0x{MetadataTokens.GetToken(methodHandle):X8}, IL_0001");
        Console.WriteLine("Call: System.Data.Common.DbCommand.ExecuteNonQuery()");
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
