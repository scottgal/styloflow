using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// Sum Reducer - Sums numeric values from accumulated entries.
/// </summary>
public sealed class SumReducerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "reduce-sum",
        reads: ["*"],
        writes: ["reduce.sum", "reduce.count"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "accumulator" : "accumulator";

        var values = GetNumericValues(ctx, windowName);
        var sum = values.Sum();

        ctx.Log($"Sum Reducer: {values.Count} values, sum={sum:F4}");
        ctx.Emit("reduce.sum", sum);
        ctx.Emit("reduce.count", values.Count);

        return Task.CompletedTask;
    }

    internal static List<double> GetNumericValues(WorkflowAtomContext ctx, string windowName)
    {
        var allEntries = ctx.Signals.WindowQuery(windowName);
        return allEntries
            .Select(e => e.Entity is AccumulatorEntry ae ? ToDouble(ae.Value) : ToDouble(e.Entity))
            .Where(v => !double.IsNaN(v))
            .ToList();
    }

    internal static double ToDouble(object? value)
    {
        return value switch
        {
            null => double.NaN,
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal dec => (double)dec,
            string s when double.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            _ => double.NaN
        };
    }
}
