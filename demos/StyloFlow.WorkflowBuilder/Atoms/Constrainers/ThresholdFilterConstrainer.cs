using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Constrainers;

/// <summary>
/// Threshold filter constrainer - gates signals based on threshold.
/// Taxonomy: constrainer, deterministic, ephemeral
/// </summary>
public sealed class ThresholdFilterConstrainer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "threshold-filter",
        reads: ["sentiment.score"],
        writes: ["filter.passed", "filter.exceeded", "filter.value"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var threshold = ctx.Config.TryGetValue("threshold", out var t) && t is double d ? d : 0.5;
        var signalKey = ctx.Config.TryGetValue("signal_key", out var sk) ? sk?.ToString() : "sentiment.score";

        var value = ctx.Signals.Get<double>(signalKey ?? "sentiment.score");

        var passed = value >= threshold;
        var exceeded = value > threshold;

        ctx.Log($"Filter check: {signalKey}={value:F2} vs threshold={threshold:F2} -> {(passed ? "PASSED" : "BLOCKED")}");

        ctx.Emit("filter.passed", passed);
        ctx.Emit("filter.exceeded", exceeded);
        ctx.Emit("filter.value", value);

        if (exceeded)
        {
            ctx.Emit("filter.action_required", true);
        }

        return Task.CompletedTask;
    }
}
