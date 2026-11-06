using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;


namespace DotNetFaultInjectionAgent;

public static class PlanDrivenPlanner
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "capabilities")
            {
                Console.WriteLine("""
                {"planVersion":3,"runtime":"net8.0","site":"external MemberRef call/callvirt at a unique Portable PDB source line","conditions":["environment equality","startAtInvocation"],"faults":["throw System.InvalidOperationException","throw System.TimeoutException","throw System.IO.IOException","delay 1..10000ms before call"],"maxFirings":"0..100, atomic per process","planChanges":"new process required","argumentMatching":false,"taskFaultInjection":false}
                """);
                return 0;
            }
            var expectedLength = args.Length > 0 && args[0] == "list" ? 4 : 5;
            if (args.Length != expectedLength || args[0] is not ("list" or "plan" or "verify"))
                throw new ArgumentException("Planner list|plan|verify <assembly> <source> <line[:member-name]> [plan.json]");
            var assembly = Path.GetFullPath(args[1]);
            var source = File.ReadAllBytes(args[2]);
            var selection = args[3].Split(':', 2);
            var line = int.Parse(selection[0]);
            var assemblyBytes = File.ReadAllBytes(assembly);
            using var pe = new PEReader(new MemoryStream(assemblyBytes));
            if (!pe.TryOpenAssociatedPortablePdb(assembly, p => File.Exists(p) ? File.OpenRead(p) : null,
                    out var provider, out _)) throw new InvalidDataException("Matching Portable PDB required.");
            using var pdb = provider!;
            var debug = pdb.GetMetadataReader();
            var metadata = pe.GetMetadataReader();
            var opcodes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode))
                .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => (ushort)o.Value);
            var sites = new List<Site>();
            foreach (var handle in metadata.MethodDefinitions)
            {
                var method = metadata.GetMethodDefinition(handle);
                if (method.RelativeVirtualAddress == 0) continue;
                var information = debug.GetMethodDebugInformation(handle.ToDebugInformationHandle());
                var points = information.GetSequencePoints().ToArray();
                foreach (var point in points.Where(p => !p.IsHidden && p.StartLine == line && p.EndLine == line))
                {
                    var document = debug.GetDocument(point.Document.IsNil ? information.Document : point.Document);
                    if (Path.GetFileName(debug.GetString(document.Name).Replace('\\', '/')) != Path.GetFileName(args[2])) continue;
                    if (debug.GetGuid(document.HashAlgorithm) != new Guid("8829d00f-11b8-4213-878b-770e8597ac16"))
                        throw new InvalidDataException("SHA-256 PDB source checksum required.");
                    if (!debug.GetBlobBytes(document.Hash).SequenceEqual(System.Security.Cryptography.SHA256.HashData(source)))
                        throw new InvalidDataException("Source checksum does not match Portable PDB.");
                    var end = points.Where(p => p.Offset > point.Offset).Select(p => p.Offset).DefaultIfEmpty(int.MaxValue).Min();
                    var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
                    for (var i = 0; i < il.Length && i < end;)
                    {
                        var offset = i;
                        ushort code = il[i++];
                        if (code == 0xfe) code = (ushort)(0xfe00 | il[i++]);
                        var opcode = opcodes[code];
                        if (offset >= point.Offset && (opcode == OpCodes.Call || opcode == OpCodes.Callvirt))
                        {
                            var token = BitConverter.ToInt32(il, i);
                            var callee = MetadataTokens.EntityHandle(token);
                            if (callee.Kind == HandleKind.MemberReference)
                                sites.Add(new Site(MetadataTokens.GetToken(handle), offset, token, opcode.Name!,
                                    metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle)callee).Name)));
                        }
                        i += opcode.OperandType switch
                        {
                            OperandType.InlineNone => 0,
                            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                            OperandType.InlineVar => 2,
                            OperandType.InlineI8 or OperandType.InlineR => 8,
                            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                            _ => 4
                        };
                    }
                }
            }
            sites = sites.Distinct().ToList();
            if (selection.Length == 2) sites = sites.Where(site => site.MemberName == selection[1]).ToList();
            if (args[0] == "list")
            {
                Console.WriteLine(JsonSerializer.Serialize(sites, InjectionPlan.JsonOptions));
                return 0;
            }
            if (sites.Count != 1) throw new InvalidDataException($"Expected one external call at the source line, found {sites.Count}.");
            var identity = new Target(assembly, InjectionPlan.Hash(assemblyBytes),
                metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D"));
            if (args[0] == "plan")
            {
                var plan = new InjectionPlan(3, "rule-1", identity, sites[0],
                    new Conditions(new Dictionary<string, string>(), 1),
                    new Fault("throw", "System.TimeoutException", "Injected timeout before call", 0), 1);
                plan.Validate();
                using var output = new FileStream(args[4], FileMode.CreateNew);
                JsonSerializer.Serialize(output, plan, InjectionPlan.JsonOptions);
            }
            else
            {
                var plan = InjectionPlan.Parse(InjectionPlan.ReadFile(args[4]));
                if (!string.Equals(plan.Target.Sha256, identity.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    Guid.Parse(plan.Target.Mvid) != Guid.Parse(identity.Mvid) || plan.Site != sites[0])
                    throw new InvalidDataException("Plan identity or call site differs from source/PDB/assembly.");
                Console.WriteLine($"Verified {plan.RuleId}: {plan.Site.MemberName} at IL_{plan.Site.IlOffset:X4}");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
