using System.Text;
using System.Text.Json;
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
        throw new ArgumentException("Usage: SourceMap <Program.cs> <SqlTarget.exe> <net48 reference directory> <list|verify|source line[,source line]> [plan.json]");

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

    var verify = args[3] == "verify";
    if (verify && args.Length != 5)
        throw new ArgumentException("Specify the plan file to verify.");
    var selections = verify
        ? candidates.Select(candidate => candidate.Line.ToString()).ToArray()
        : args[3].Split(',');
    if (selections.Length > 1 && args.Length != 5)
        throw new ArgumentException("Specify an output path for a multi-rule plan.");
    var rules = new List<object>();
    var seenLines = new HashSet<int>();
    foreach (var selection in selections)
    {
        if (!int.TryParse(selection, out var line))
            throw new ArgumentException("Select a source line from the candidate list.");
        if (!seenLines.Add(line)) throw new ArgumentException("Duplicate source line.");
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
            rules.Add(plan);
            if (args.Length == 5) continue;
            Console.WriteLine($"Source: {caller.ToDisplayString()} line {line}");
            Console.WriteLine($"SHA-256: {plan.sha256}");
            Console.WriteLine($"MVID: {plan.mvid}");
            Console.WriteLine($"Method: 0x{MetadataTokens.GetToken(methodHandle):X8}, IL_0001");
            Console.WriteLine("Call: System.Data.Common.DbCommand.ExecuteNonQuery()");
        }
    }
    if (verify)
    {
        VerifyPlan(args[4], rules.Select(rule => JsonSerializer.SerializeToElement(rule)));
    }
    else if (args.Length == 5)
    {
        using var output = new FileStream(args[4], FileMode.CreateNew);
        var plan = rules.Count == 1 ? rules[0] : new { version = 2, rules };
        System.Text.Json.JsonSerializer.Serialize(output, plan,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}


static void VerifyPlan(string path, IEnumerable<JsonElement> verifiedSites)
{
    using var input = File.OpenRead(path);
    if (input.Length is <= 0 or > 65536)
        throw new InvalidDataException("Invalid plan file size.");

    using var document = JsonDocument.Parse(input);
    var root = document.RootElement;
    var keys = ReadKeys(root);
    JsonElement[] rules;
    if (root.GetProperty("version").GetInt32() == 2)
    {
        if (keys.Count != 2)
            throw new InvalidDataException("Unexpected multi-rule plan fields.");
        rules = root.GetProperty("rules").EnumerateArray().ToArray();
    }
    else
    {
        rules = [root];
    }

    if (rules.Length == 0)
        throw new InvalidDataException("The plan contains no rules.");

    var sites = verifiedSites.ToDictionary(site => site.GetProperty("methodToken").GetInt32());
    var seen = new HashSet<int>();
    foreach (var rule in rules)
    {
        if (ReadKeys(rule).Count != 11)
            throw new InvalidDataException("Unexpected rule fields.");

        if (rule.GetProperty("version").GetInt32() != 1 || rule.GetProperty("ilOffset").GetInt32() != 1)
            throw new InvalidDataException("Unsupported rule version or IL offset.");

        var token = rule.GetProperty("methodToken").GetInt32();
        if (!seen.Add(token) || !sites.TryGetValue(token, out var site))
            throw new InvalidDataException($"Unknown or repeated method token: {token}.");

        foreach (var key in new[] { "version", "sha256", "mvid", "methodToken", "ilOffset", "call", "fault" })
        {
            if (!JsonElement.DeepEquals(rule.GetProperty(key), site.GetProperty(key)))
                throw new InvalidDataException($"Rule {token}: {key} does not match the verified source and executable.");
        }

        foreach (var key in new[] { "server", "database", "procedure" })
        {
            var value = rule.GetProperty(key).GetString();
            if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 1024 ||
                value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new InvalidDataException($"Rule {token}: invalid {key}.");
        }

        if (rule.GetProperty("maxFirings").GetInt32() < 0)
            throw new InvalidDataException($"Rule {token}: invalid maxFirings.");
    }

    Console.WriteLine($"Verified {rules.Length} rule(s) against the executable, Portable PDB and source.");
}

static HashSet<string> ReadKeys(JsonElement value)
{
    var keys = new HashSet<string>(StringComparer.Ordinal);
    foreach (var property in value.EnumerateObject())
    {
        if (!keys.Add(property.Name))
            throw new InvalidDataException($"Duplicate JSON key: {property.Name}.");
    }
    return keys;
}
