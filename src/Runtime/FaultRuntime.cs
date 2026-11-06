using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DotNetFaultInjectionAgent.Runtime;

public static class FaultRuntime
{
    private sealed class State(InjectionPlan plan, string hash)
    {
        public readonly InjectionPlan Plan = plan;
        public readonly string Hash = hash;
        public long MatchingInvocations;
        public int Firings;
    }

    private static readonly Lazy<State> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static State Load()
    {
        var path = Environment.GetEnvironmentVariable("DOTNET_FAULT_INJECTION_PLAN")
            ?? throw new InvalidOperationException("DOTNET_FAULT_INJECTION_PLAN is missing.");
        var bytes = InjectionPlan.ReadFile(path);
        return new State(InjectionPlan.Parse(bytes), InjectionPlan.Hash(bytes));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void BeforeCall(string expectedPlanHash)
    {
        var state = Loaded.Value;
        if (!string.Equals(state.Hash, expectedPlanHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Chaos configuration error: plan changed after instrumentation.");
        foreach (var pair in state.Plan.Conditions.Environment)
            if (!string.Equals(Environment.GetEnvironmentVariable(pair.Key), pair.Value, StringComparison.Ordinal))
                return;
        var invocation = Interlocked.Increment(ref state.MatchingInvocations);
        if (invocation < state.Plan.Conditions.StartAtInvocation) return;
        int used;
        do
        {
            used = Volatile.Read(ref state.Firings);
            if (used >= state.Plan.MaxFirings) return;
        }
        while (Interlocked.CompareExchange(ref state.Firings, used + 1, used) != used);

        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            @event = "chaos.fired", ruleId = state.Plan.RuleId,
            invocation, firing = used + 1, fault = state.Plan.Fault.Kind
        }));
        var fault = state.Plan.Fault;
        if (fault.Kind == "delay")
        {
            Thread.Sleep(fault.DelayMs);
            return;
        }
        throw fault.ExceptionType switch
        {
            "System.InvalidOperationException" => new InvalidOperationException(fault.Message),
            "System.TimeoutException" => new TimeoutException(fault.Message),
            "System.IO.IOException" => new IOException(fault.Message),
            _ => new InvalidOperationException("Unsupported fault after plan validation.")
        };
    }
}
