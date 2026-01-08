using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// Reducer - Applies aggregation functions to accumulated groups.
/// Supports sum, avg, max, min, count, first, last, concat operations.
/// Pure deterministic computation - no LLM.
/// </summary>
public sealed class ReducerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "reducer",
        reads: ["*"],
        writes: ["reduce.result", "reduce.groups", "reduce.operation", "reduce.group_results"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "accumulator" : "accumulator";
        var operation = ctx.Config.TryGetValue("operation", out var op) ? op?.ToString()?.ToLowerInvariant() ?? "sum" : "sum";
        var groupKey = ctx.Config.TryGetValue("group_key", out var gk) ? gk?.ToString() : null; // null = all groups

        var allEntries = ctx.Signals.WindowQuery(windowName);
        var accumulatorEntries = allEntries
            .Where(e => e.Entity is AccumulatorEntry)
            .Select(e => (AccumulatorEntry)e.Entity)
            .ToList();

        // Group by key
        var groups = accumulatorEntries
            .GroupBy(e => e.GroupKey)
            .Where(g => groupKey == null || g.Key == groupKey)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Value).ToList());

        var groupResults = new Dictionary<string, object?>();

        foreach (var (key, values) in groups)
        {
            var result = ApplyOperation(operation, values);
            groupResults[key] = result;
        }

        // Overall result (first group if single, or summary)
        object? overallResult = groupResults.Count == 1
            ? groupResults.Values.First()
            : groupResults;

        ctx.Log($"Reducer '{windowName}': operation={operation}, groups={groups.Count}");
        foreach (var (key, result) in groupResults)
        {
            ctx.Log($"  {key}: {result}");
        }

        ctx.Emit("reduce.result", overallResult);
        ctx.Emit("reduce.groups", groups.Count);
        ctx.Emit("reduce.operation", operation);
        ctx.Emit("reduce.group_results", groupResults);

        return Task.CompletedTask;
    }

    private static object? ApplyOperation(string operation, List<object?> values)
    {
        var numericValues = values
            .Select(ToDouble)
            .Where(v => !double.IsNaN(v))
            .ToList();

        return operation switch
        {
            "sum" => numericValues.Sum(),
            "avg" or "average" => numericValues.Count > 0 ? numericValues.Average() : 0,
            "max" => numericValues.Count > 0 ? numericValues.Max() : 0,
            "min" => numericValues.Count > 0 ? numericValues.Min() : 0,
            "count" => values.Count,
            "first" => values.FirstOrDefault(),
            "last" => values.LastOrDefault(),
            "concat" => string.Join(", ", values.Select(v => v?.ToString() ?? "")),
            "distinct" => values.Distinct().Count(),
            "stddev" => CalculateStdDev(numericValues),
            "variance" => CalculateVariance(numericValues),
            "median" => CalculateMedian(numericValues),
            _ => values.Count
        };
    }

    private static double ToDouble(object? value)
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

    private static double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        return Math.Sqrt(values.Average(v => Math.Pow(v - avg, 2)));
    }

    private static double CalculateVariance(List<double> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        return values.Average(v => Math.Pow(v - avg, 2));
    }

    private static double CalculateMedian(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }
}
