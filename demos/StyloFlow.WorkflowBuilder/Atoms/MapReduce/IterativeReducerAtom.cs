using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// Iterative Reducer - Reduces window contents until target count is reached.
/// Batches items, applies reduction, repeats until target_count items remain.
/// Example: 50 items → batch_size=10 → 5 batches → reduce each → 5 results
/// </summary>
public sealed class IterativeReducerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "iterative-reducer",
        reads: ["*"],
        writes: ["reduce.results", "reduce.iterations", "reduce.final_count", "reduce.reduction_ratio"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "accumulator" : "accumulator";
        var outputWindow = ctx.Config.TryGetValue("output_window", out var ow) ? ow?.ToString() ?? "reduced" : "reduced";
        var targetCount = GetIntConfig(ctx.Config, "target_count", 5);
        var batchSize = GetIntConfig(ctx.Config, "batch_size", 10);
        var operation = ctx.Config.TryGetValue("operation", out var op) ? op?.ToString()?.ToLower() ?? "avg" : "avg";
        var scoreField = ctx.Config.TryGetValue("score_field", out var sf) ? sf?.ToString() ?? "score" : "score";

        var allEntries = ctx.Signals.WindowQuery(windowName);
        var items = allEntries.ToList();
        var initialCount = items.Count;
        var iterations = 0;

        ctx.Log($"Iterative Reducer: starting with {initialCount} items, target={targetCount}");

        // Keep reducing until we reach target count
        while (items.Count > targetCount && iterations < 10) // Safety limit
        {
            iterations++;
            var batches = CreateBatches(items, batchSize);
            var reduced = new List<WindowEntry>();

            foreach (var batch in batches)
            {
                var result = ReduceBatch(batch, operation, scoreField);
                reduced.Add(result);
            }

            ctx.Log($"  Iteration {iterations}: {items.Count} → {reduced.Count} items ({batches.Count} batches)");
            items = reduced;
        }

        // If still over target, take top K by score
        if (items.Count > targetCount)
        {
            items = items
                .OrderByDescending(e => ExtractScore(e.Entity, scoreField))
                .Take(targetCount)
                .ToList();
            ctx.Log($"  Final trim: {items.Count} items");
        }

        // Store results in output window
        var outputWin = ctx.Signals.GetWindow(outputWindow, targetCount * 2, TimeSpan.FromMinutes(30));
        foreach (var item in items)
        {
            ctx.Signals.WindowAdd(outputWindow, item.Key, item.Entity);
        }

        var reductionRatio = initialCount > 0 ? (double)items.Count / initialCount : 1;

        ctx.Log($"Iterative Reducer: {initialCount} → {items.Count} in {iterations} iterations");

        ctx.Emit("reduce.results", items.Select(e => e.Key).ToList());
        ctx.Emit("reduce.iterations", iterations);
        ctx.Emit("reduce.final_count", items.Count);
        ctx.Emit("reduce.reduction_ratio", reductionRatio);

        return Task.CompletedTask;
    }

    private static List<List<WindowEntry>> CreateBatches(List<WindowEntry> items, int batchSize)
    {
        var batches = new List<List<WindowEntry>>();
        for (int i = 0; i < items.Count; i += batchSize)
        {
            batches.Add(items.Skip(i).Take(batchSize).ToList());
        }
        return batches;
    }

    private static WindowEntry ReduceBatch(List<WindowEntry> batch, string operation, string scoreField)
    {
        if (batch.Count == 1) return batch[0];

        var scores = batch.Select(e => ExtractScore(e.Entity, scoreField)).ToList();
        var resultScore = operation switch
        {
            "sum" => scores.Sum(),
            "avg" or "average" => scores.Average(),
            "max" => scores.Max(),
            "min" => scores.Min(),
            "median" => CalculateMedian(scores),
            _ => scores.Average()
        };

        // Keep the entry with highest score as representative
        var bestEntry = batch.OrderByDescending(e => ExtractScore(e.Entity, scoreField)).First();

        // Create merged result
        var merged = new
        {
            ReducedScore = resultScore,
            SourceCount = batch.Count,
            Representative = bestEntry.Entity,
            SourceKeys = batch.Select(e => e.Key).ToList()
        };

        return new WindowEntry(
            $"reduced-{Guid.NewGuid():N}"[..16],
            merged,
            DateTimeOffset.UtcNow);
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

    private static double ExtractScore(object? entity, string field)
    {
        if (entity is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue(field, out var v)) return ToDouble(v);
            if (dict.TryGetValue("ReducedScore", out var rs)) return ToDouble(rs);
        }

        if (entity is JsonElement je)
        {
            if (je.TryGetProperty(field, out var prop) && prop.ValueKind == JsonValueKind.Number)
                return prop.GetDouble();
            if (je.TryGetProperty("ReducedScore", out var rsProp) && rsProp.ValueKind == JsonValueKind.Number)
                return rsProp.GetDouble();
        }

        return ToDouble(entity);
    }

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        string s when double.TryParse(s, out var p) => p,
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
        _ => 0
    };

    private static int GetIntConfig(Dictionary<string, object> config, string key, int defaultValue)
    {
        if (!config.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            _ => defaultValue
        };
    }
}
