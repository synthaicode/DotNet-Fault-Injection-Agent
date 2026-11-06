using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetFaultInjectionAgent;

public sealed record Target(
    [property: JsonRequired] string Path,
    [property: JsonRequired] string Sha256,
    [property: JsonRequired] string Mvid);
public sealed record Site(
    [property: JsonRequired] int MethodToken,
    [property: JsonRequired] int IlOffset,
    [property: JsonRequired] int MemberToken,
    [property: JsonRequired] string Opcode,
    [property: JsonRequired] string MemberName);
public sealed record Conditions(
    [property: JsonRequired] Dictionary<string, string> Environment,
    [property: JsonRequired] int StartAtInvocation);
public sealed record Fault(
    [property: JsonRequired] string Kind,
    [property: JsonRequired] string ExceptionType,
    [property: JsonRequired] string Message,
    [property: JsonRequired] int DelayMs);
public sealed record InjectionPlan(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string RuleId,
    [property: JsonRequired] Target Target,
    [property: JsonRequired] Site Site,
    [property: JsonRequired] Conditions Conditions,
    [property: JsonRequired] Fault Fault,
    [property: JsonRequired] int MaxFirings)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 16
    };

    public static InjectionPlan Parse(byte[] bytes)
    {
        if (bytes.Length is 0 or > 65536) throw new InvalidDataException("Plan must be 1..65536 bytes.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicates(document.RootElement);
        var plan = JsonSerializer.Deserialize<InjectionPlan>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Plan is null.");
        plan.Validate();
        return plan;
    }

    public static byte[] ReadFile(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length is 0 or > 65536) throw new InvalidDataException("Plan must be 1..65536 bytes.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("Plan changed while reading.");
        return bytes;
    }

    public void Validate()
    {
        if (Version != 3 || RuleId is null || !Regex.IsMatch(RuleId, "^[A-Za-z0-9_.-]{1,128}$"))
            throw new InvalidDataException("Expected version 3 and an ASCII ruleId of 1..128 characters.");
        if (Target is null || Site is null || Conditions is null || Fault is null)
            throw new InvalidDataException("Missing plan section.");
        // Runtime paths can name the deployment on a different OS from the planner.
        if (string.IsNullOrWhiteSpace(Target.Path) || Encoding.UTF8.GetByteCount(Target.Path) > 4096 || Target.Path.Contains('\0') ||
            !(Target.Path.StartsWith('/') || Regex.IsMatch(Target.Path, @"^[A-Za-z]:[\\/]") || Target.Path.StartsWith(@"\\")) ||
            Target.Sha256 is null || !Regex.IsMatch(Target.Sha256, "^[0-9A-Fa-f]{64}$") ||
            !Guid.TryParseExact(Target.Mvid, "D", out _))
            throw new InvalidDataException("Invalid target identity.");
        if ((Site.MethodToken & unchecked((int)0xff000000)) != 0x06000000 ||
            (Site.MethodToken & 0x00ffffff) == 0 || Site.IlOffset < 0 ||
            (Site.MemberToken & unchecked((int)0xff000000)) != 0x0a000000 ||
            (Site.MemberToken & 0x00ffffff) == 0 || Site.Opcode is not ("call" or "callvirt") ||
            string.IsNullOrWhiteSpace(Site.MemberName) || Encoding.UTF8.GetByteCount(Site.MemberName) > 1024)
            throw new InvalidDataException("Only a concrete MethodDef call to a MemberRef is supported.");
        if (MaxFirings is < 0 or > 100 || Conditions.StartAtInvocation is < 1 or > 1000000 ||
            Conditions.Environment is null || Conditions.Environment.Count > 16)
            throw new InvalidDataException("Invalid firing conditions.");
        foreach (var pair in Conditions.Environment)
            if (!Regex.IsMatch(pair.Key, "^[A-Za-z_][A-Za-z0-9_]{0,127}$") ||
                pair.Value is null || Encoding.UTF8.GetByteCount(pair.Value) > 256 || pair.Value.Contains('\0'))
                throw new InvalidDataException("Invalid environment condition.");
        if (Fault.Kind == "throw")
        {
            if (Fault.ExceptionType is not ("System.InvalidOperationException" or "System.TimeoutException" or "System.IO.IOException") ||
                Fault.Message is null || Encoding.UTF8.GetByteCount(Fault.Message) > 1024 || Fault.DelayMs != 0)
                throw new InvalidDataException("Unsupported throw fault.");
        }
        else if (Fault.Kind == "delay")
        {
            if (Fault.DelayMs is < 1 or > 10000 || Fault.ExceptionType != "" || Fault.Message != "")
                throw new InvalidDataException("Invalid delay fault.");
        }
        else throw new InvalidDataException("Unsupported fault kind.");
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON key: " + property.Name);
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }
}
