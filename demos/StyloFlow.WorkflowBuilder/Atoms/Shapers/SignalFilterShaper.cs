using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Filter - Gates signals by condition.
/// </summary>
public sealed class SignalFilterShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-filter",
        reads: ["*"],
        writes: ["filter.passed", "filter.blocked"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var op = ctx.Config.TryGetValue("operator", out var opVal) ? opVal?.ToString() : "gt";
        var threshold = ctx.Config.TryGetValue("threshold", out var thVal) ? ShaperHelper.ToDouble(thVal) : 0.5;

        var value = ShaperHelper.GetNumericSignal(ctx.Signals);
        if (value == 0)
        {
            ctx.Log("No numeric signal to filter");
            ctx.Emit("filter.blocked", true);
            return Task.CompletedTask;
        }

        var passed = op switch
        {
            "gt" => value > threshold,
            "gte" => value >= threshold,
            "lt" => value < threshold,
            "lte" => value <= threshold,
            "eq" => Math.Abs(value - threshold) < 0.0001,
            "ne" => Math.Abs(value - threshold) >= 0.0001,
            _ => value > threshold
        };

        ctx.Log($"Filter: {value:F3} {op} {threshold:F3} -> {(passed ? "PASSED" : "BLOCKED")}");

        ctx.Emit("filter.passed", passed ? value : null);
        ctx.Emit("filter.blocked", !passed);

        return Task.CompletedTask;
    }
}
