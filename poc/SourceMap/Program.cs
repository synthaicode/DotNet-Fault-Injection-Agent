using System.Text;
using System.Text.Json;
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
        throw new ArgumentException("Usage: SourceMap <Program.cs|project.csproj> <SqlTarget.exe> <net48 reference directory> <list|recommend|verify|selection[,selection]> [plan.json]");

    var sourcePath = Path.GetFullPath(args[0]);
    var exePath = Path.GetFullPath(args[1]);
    var projectMode = Path.GetExtension(sourcePath).Equals(".csproj", StringComparison.OrdinalIgnoreCase);
    var compilation = projectMode ? ProjectInput.Load(sourcePath, args[2]) : LoadFixture(sourcePath, args[2]);
    var errors = compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length != 0)
        throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(error => error.ToString())));

    var candidates = compilation.SyntaxTrees.SelectMany(tree =>
    {
        var model = compilation.GetSemanticModel(tree);
        return tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => projectMode || call.Parent is StatementSyntax { Parent: BlockSyntax block } &&
                block.Statements.Count == 1 && block.Parent is MethodDeclarationSyntax)
            .Select(call => (Call: call, Method: model.GetSymbolInfo(call).Symbol as IMethodSymbol))
            .Where(candidate => candidate.Method is { Name: "ExecuteNonQuery", Parameters.Length: 0 } &&
                candidate.Method.ContainingType.ToDisplayString() == "System.Data.SqlClient.SqlCommand" ||
                candidate.Method is { Name: "WriteAllText", Parameters.Length: 2 } fileMethod &&
                fileMethod.ContainingType.ToDisplayString() == "System.IO.File" &&
                fileMethod.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_String) ||
                candidate.Method is { Name: "Connect", Parameters.Length: 2 } networkMethod &&
                networkMethod.ContainingType.ToDisplayString() == "System.Net.Sockets.TcpClient" &&
                networkMethod.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                networkMethod.Parameters[1].Type.SpecialType == SpecialType.System_Int32)
            .Select(candidate => (candidate.Call,
                File: candidate.Method.Name == "WriteAllText",
                Network: candidate.Method.Name == "Connect",
                Caller: model.GetEnclosingSymbol(candidate.Call.SpanStart) as IMethodSymbol,
                Line: candidate.Call.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
    }).Select((candidate, index) => (candidate.Call, candidate.Caller, candidate.Line, candidate.File, candidate.Network,
        Id: projectMode ? index + 1 : candidate.Line)).ToArray();

    if (args[3] is "list" or "recommend")
    {
        foreach (var candidate in candidates)
        {
            Console.WriteLine($"{candidate.Id}: {candidate.Caller?.ToDisplayString()} -> {(candidate.File ? "File.WriteAllText(string, string)" : candidate.Network ? "TcpClient.Connect(string, int)" : "SqlCommand.ExecuteNonQuery()")}");
            if (projectMode)
                Console.WriteLine($"   {Path.GetRelativePath(Path.GetDirectoryName(sourcePath), candidate.Call.SyntaxTree.FilePath)}:{candidate.Line}");
            if (args[3] == "recommend" && candidate.File)
            {
                Console.WriteLine("   Trial: throw IOException before opening the file. Check file state, error handling and retry.");
            }
            else if (args[3] == "recommend" && candidate.Network)
            {
                Console.WriteLine("   Trial: throw SocketException(ConnectionRefused) before connecting. Check fallback or retry.");
            }
            else if (args[3] == "recommend")
            {
                var enclosingCatches = candidate.Call.Ancestors().OfType<TryStatementSyntax>()
                    .Where(statement => statement.Block.Span.Contains(candidate.Call.Span))
                    .SelectMany(statement => statement.Catches).ToArray();
                var catches = enclosingCatches.Select(clause => clause.Declaration?.Type.ToString() ?? "any exception")
                    .Distinct().ToArray();
                Console.WriteLine(catches.Length == 0
                    ? "   Trial: check how a pre-send failure propagates; no enclosing catch was found in this method."
                    : $"   Trial: check what happens after a pre-send failure; enclosing catches: {string.Join(", ", catches)}.");
                foreach (var clause in enclosingCatches)
                {
                    var line = clause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var assignments = clause.Block.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                        .Select(assignment => assignment.Left.ToString()).Distinct().ToArray();
                    if (assignments.Length > 0)
                        Console.WriteLine($"   Evidence at line {line}: catch writes {string.Join(", ", assignments)}.");
                }
                Console.WriteLine("   Intervention: fail the confirmed stored-procedure call once before send (timeout-before-send).");
                Console.WriteLine("   Observe: worker state, recorded exception, visible notification and DB writes.");
                Console.WriteLine("   Pass if the confirmed recovery or stop behavior occurs, the expected notification is visible, and the failed call creates no DB write.");
                Console.WriteLine("   Shared-state trial: if a worker publishes a flag read by another thread, vary the write/read order and observe whether the reader reacts. Direct memory-visibility fault injection is unsupported.");
                Console.WriteLine("   Sync trial: delay a successful SQL response. While waiting, check UI or other work can progress and completion state is unchanged. After success, check one completion per call.");
                Console.WriteLine("   Completion-order trial (when two workers share completion state): start call A, delay only A, then let call B complete before A.");
                Console.WriteLine("   Check: correlate SQL completion with each call. Require completion order B then A and a count delta of exactly +1 after B, then +1 after A. A missing or repeated update fails even if the final count matches.");
                Console.WriteLine("   Observation only: a synchronous API does not establish which thread calls it or prove a synchronization defect.");
            }
        }
        return 0;
    }

    var verify = args[3] == "verify";
    if (verify && args.Length != 5)
        throw new ArgumentException("Specify the plan file to verify.");
    var selections = verify
        ? candidates.Select(candidate => candidate.Id.ToString()).ToArray()
        : args[3].Split(',');
    if (selections.Length > 1 && args.Length != 5)
        throw new ArgumentException("Specify an output path for a multi-rule plan.");
    var rules = new List<object>();
    var seenLines = new HashSet<int>();
    foreach (var selection in selections)
    {
        if (!int.TryParse(selection, out var id))
            throw new ArgumentException("Select a source line from the candidate list.");
        if (!seenLines.Add(id)) throw new ArgumentException("Duplicate selection.");
        var selected = candidates.Single(candidate => candidate.Id == id);
        var line = selected.Line;
        var source = selected.Call.SyntaxTree.GetText();
        var caller = selected.Caller ?? throw new InvalidDataException("Missing enclosing method.");
        if (!projectMode && !selected.File && !selected.Network && (caller is not { IsStatic: true, Parameters.Length: 1 } ||
            caller.ContainingType.ToDisplayString() != "Program" ||
            caller.Parameters[0].Type.ToDisplayString() != "System.Data.SqlClient.SqlCommand" ||
            caller.ReturnType.SpecialType != SpecialType.System_Int32))
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
                metadata.GetString(metadata.GetTypeDefinition(handle).Name) == caller.ContainingType.MetadataName &&
                metadata.GetString(metadata.GetTypeDefinition(handle).Namespace) == (caller.ContainingNamespace.IsGlobalNamespace ? "" : caller.ContainingNamespace.ToDisplayString()));
            var methodHandle = metadata.GetTypeDefinition(owner).GetMethods().Single(handle =>
                metadata.GetString(metadata.GetMethodDefinition(handle).Name) == caller.Name);
            var method = metadata.GetMethodDefinition(methodHandle);
            var points = pdb.GetMethodDebugInformation(methodHandle.ToDebugInformationHandle());
            var sequencePoints = points.GetSequencePoints().ToArray();
            var point = sequencePoints.Single(item =>
                !item.IsHidden && item.StartLine == line && item.EndLine == line);
            var document = pdb.GetDocument(point.Document.IsNil ? points.Document : point.Document);
            var checksum = pdb.GetBlobBytes(document.Hash);
            var algorithm = pdb.GetGuid(document.HashAlgorithm);
            var expected = algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16")
                ? source.GetChecksum().ToArray() : Array.Empty<byte>();
            if (expected.Length == 0 || !checksum.SequenceEqual(expected))
                throw new InvalidDataException("Source checksum does not match the PDB document.");

            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            if (!projectMode && !selected.File && !selected.Network && (point.Offset != 0 || il is not [0x02, 0x6f, _, _, _, _, 0x2a]))
                throw new InvalidDataException("Unexpected IL shape for the selected call.");
            var end = sequencePoints.Where(item => item.Offset > point.Offset).Select(item => item.Offset)
                .DefaultIfEmpty(il.Length).Min();
            var ilOffset = ProjectInput.FindCall(il, point.Offset, end, metadata, selected.File, selected.Network);

            image.Position = 0;
            var sha256 = Convert.ToHexString(SHA256.HashData(image));
            var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            var methodToken = MetadataTokens.GetToken(methodHandle);
            var plan = new
            {
                version = 1,
                sha256,
                mvid,
                methodToken,
                ilOffset,
                call = "System.Data.Common.DbCommand.ExecuteNonQuery()",
                fault = "timeout-before-send",
                server = @"(localdb)\MSSQLLocalDB",
                database = "tempdb",
                procedure = "#ConfirmOrder",
                maxFirings = 1
            };
            rules.Add(selected.File ? (object)new
            {
                version = 1, sha256, mvid, methodToken, ilOffset,
                call = "System.IO.File.WriteAllText(string,string)",
                fault = "io-error-before-write",
                path = Path.GetFullPath("file-probe.txt"),
                maxFirings = 1
            } : selected.Network ? new
            {
                version = 1, sha256, mvid, methodToken, ilOffset,
                call = "System.Net.Sockets.TcpClient.Connect(string,int)",
                fault = "connection-refused-before-connect",
                host = "127.0.0.1", port = 48128, maxFirings = 1
            } : (object)plan);
            if (args.Length == 5) continue;
            Console.WriteLine($"Source: {caller.ToDisplayString()} line {line}");
            Console.WriteLine($"SHA-256: {plan.sha256}");
            Console.WriteLine($"MVID: {plan.mvid}");
            Console.WriteLine($"Method: 0x{MetadataTokens.GetToken(methodHandle):X8}, IL_{ilOffset:X4}");
            Console.WriteLine(selected.File ? "Call: System.IO.File.WriteAllText(string,string)" : selected.Network ? "Call: System.Net.Sockets.TcpClient.Connect(string,int)" : "Call: System.Data.Common.DbCommand.ExecuteNonQuery()");
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

    var sites = verifiedSites.ToDictionary(site =>
        (site.GetProperty("methodToken").GetInt32(), site.GetProperty("ilOffset").GetInt32()));
    var seen = new HashSet<(int, int)>();
    foreach (var rule in rules)
    {
        var fault = rule.GetProperty("fault").GetString();
        var delay = fault == "delay-before-send";
        var file = fault is "io-error-before-write" or "oom-before-write" or "thread-abort-before-write";
        var network = fault == "connection-refused-before-connect";
        if ((!network && !file && !delay && fault != "timeout-before-send") || ReadKeys(rule).Count != (network ? 10 : file ? 9 : delay ? 12 : 11))
            throw new InvalidDataException("Unexpected rule fields.");

        if (rule.GetProperty("version").GetInt32() != 1 || rule.GetProperty("ilOffset").GetInt32() < 0)
            throw new InvalidDataException("Unsupported rule version or IL offset.");

        var token = rule.GetProperty("methodToken").GetInt32();
        var offset = rule.GetProperty("ilOffset").GetInt32();
        if (!seen.Add((token, offset)) || !sites.TryGetValue((token, offset), out var site))
            throw new InvalidDataException($"Unknown or repeated call site: method {token}, IL offset {offset}.");

        foreach (var key in new[] { "version", "sha256", "mvid", "methodToken", "ilOffset", "call" })
        {
            if (!JsonElement.DeepEquals(rule.GetProperty(key), site.GetProperty(key)))
                throw new InvalidDataException($"Rule {token}: {key} does not match the verified source and executable.");
        }

        var siteFault = site.GetProperty("fault").GetString();
        if (file != (siteFault == "io-error-before-write") || network != (siteFault == "connection-refused-before-connect"))
            throw new InvalidDataException($"Rule {token}: incompatible fault type.");
        if (file && !Path.IsPathFullyQualified(rule.GetProperty("path").GetString()))
            throw new InvalidDataException($"Rule {token}: expected an absolute file path.");
        if (network && (rule.GetProperty("host").GetString() != "127.0.0.1" ||
            rule.GetProperty("port").GetInt32() is < 1 or > 65535))
            throw new InvalidDataException($"Rule {token}: expected a loopback host and valid port.");
        foreach (var key in network ? Array.Empty<string>() : file ? new[] { "path" } : new[] { "server", "database", "procedure" })
        {
            var value = rule.GetProperty(key).GetString();
            if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 1024 ||
                value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new InvalidDataException($"Rule {token}: invalid {key}.");
        }

        if (delay && rule.GetProperty("delayMs").GetInt32() is < 1 or > 10000)
            throw new InvalidDataException($"Rule {token}: delayMs must be between 1 and 10000.");
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


static CSharpCompilation LoadFixture(string sourcePath, string referenceDirectory)
{
    using var input = File.OpenRead(sourcePath);
    var source = SourceText.From(input, checksumAlgorithm: SourceHashAlgorithm.Sha256);
    var tree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
    var coreReferences = Path.Combine(referenceDirectory, "System.Runtime.dll");
    var referencePaths = File.Exists(coreReferences)
        ? Directory.GetFiles(referenceDirectory, "*.dll")
        : new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Data.dll", "System.ServiceProcess.dll" }
            .Select(name => Path.Combine(referenceDirectory, name)).ToArray();
    var references = referencePaths.Select(path => MetadataReference.CreateFromFile(path));
    return CSharpCompilation.Create("SqlTarget", [tree], references,
        new CSharpCompilationOptions(OutputKind.ConsoleApplication));
}
