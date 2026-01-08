using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Shapers;

/// <summary>
/// Signal Comparator - Compares values and outputs boolean result.
/// </summary>
public sealed class SignalComparatorShaper
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "signal-comparator",
        reads: ["*"],
        writes: ["compare.result", "compare.difference"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var op = ctx.Config.TryGetValue("operator", out var opVal) ? opVal?.ToString() : "gt";
        var threshold = ctx.Config.TryGetValue("threshold", out var thVal) ? ShaperHelper.ToDouble(thVal) : 0.5;

        var value = ShaperHelper.GetNumericSignal(ctx.Signals);
        if (value == 0)
        {
            ctx.Log("No numeric signal to compare");
            return Task.CompletedTask;
        }

        var diff = value - threshold;
        var result = op switch
        {
            "gt" => value > threshold,
            "gte" => value >= threshold,
            "lt" => value < threshold,
            "lte" => value <= threshold,
            "eq" => Math.Abs(diff) < 0.0001,
            "ne" => Math.Abs(diff) >= 0.0001,
            _ => value > threshold
        };

        ctx.Log($"Compare: {value:F3} {op} {threshold:F3} = {result}, diff={diff:F3}");

        ctx.Emit("compare.result", result);
        ctx.Emit("compare.difference", diff);

        return Task.CompletedTask;
    }
}
