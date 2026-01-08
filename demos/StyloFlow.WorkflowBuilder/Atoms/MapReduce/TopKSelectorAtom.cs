using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// TopK Selector - Selects top K items from window by score.
/// Essential for iterative reduction: 50 → TopK(10) → 10 → TopK(5) → 5
/// </summary>
public sealed class TopKSelectorAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "topk-selector",
        reads: ["*"],
        writes: ["topk.selected", "topk.count", "topk.dropped"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "candidates" : "candidates";
        var outputWindow = ctx.Config.TryGetValue("output_window", out var ow) ? ow?.ToString() ?? "topk_results" : "topk_results";
        var k = GetIntConfig(ctx.Config, "k", 10);
        var scoreField = ctx.Config.TryGetValue("score_field", out var sf) ? sf?.ToString() ?? "score" : "score";
        var descending = ctx.Config.TryGetValue("descending", out var desc) && desc?.ToString()?.ToLower() != "false";

        var allEntries = ctx.Signals.WindowQuery(windowName);

        // Extract scores and sort
        var scored = allEntries
            .Select(e => (Entry: e, Score: ExtractScore(e.Entity, scoreField)))
            .ToList();

        var sorted = descending
            ? scored.OrderByDescending(x => x.Score)
            : scored.OrderBy(x => x.Score);

        var selected = sorted.Take(k).ToList();
        var dropped = scored.Count - selected.Count;

        // Add selected to output window
        var outputWin = ctx.Signals.GetWindow(outputWindow, k * 2, TimeSpan.FromMinutes(30));
        foreach (var (entry, score) in selected)
        {
            ctx.Signals.WindowAdd(outputWindow, entry.Key, entry.Entity);
        }

        ctx.Log($"TopK: selected {selected.Count} from {scored.Count} (k={k}, dropped={dropped})");

        ctx.Emit("topk.selected", selected.Select(x => x.Entry.Key).ToList());
        ctx.Emit("topk.count", selected.Count);
        ctx.Emit("topk.dropped", dropped);

        return Task.CompletedTask;
    }

    private static double ExtractScore(object? entity, string field)
    {
        if (entity is AccumulatorEntry ae)
            return ToDouble(ae.Value);

        if (entity is IDictionary<string, object> dict && dict.TryGetValue(field, out var v))
            return ToDouble(v);

        if (entity is JsonElement je && je.TryGetProperty(field, out var prop))
            return prop.ValueKind == JsonValueKind.Number ? prop.GetDouble() : 0;

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
