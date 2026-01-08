using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// Average Reducer - Calculates mean of numeric values.
/// </summary>
public sealed class AvgReducerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "reduce-avg",
        reads: ["*"],
        writes: ["reduce.avg", "reduce.count"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "accumulator" : "accumulator";

        var values = SumReducerAtom.GetNumericValues(ctx, windowName);
        var avg = values.Count > 0 ? values.Average() : 0;

        ctx.Log($"Avg Reducer: {values.Count} values, avg={avg:F4}");
        ctx.Emit("reduce.avg", avg);
        ctx.Emit("reduce.count", values.Count);

        return Task.CompletedTask;
    }
}
