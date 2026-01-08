using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// RRF (Reciprocal Rank Fusion) - Combines multiple ranked lists into a single ranking.
/// Score = Σ 1/(k + rank_i) where k is a constant (typically 60).
/// Pure deterministic computation - no LLM.
/// </summary>
public sealed class RRFScorerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "rrf-scorer",
        reads: ["*"],
        writes: ["rrf.scores", "rrf.ranked", "rrf.top_item", "rrf.top_score"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "rankings" : "rankings";
        var k = GetDoubleConfig(ctx.Config, "k", 60.0); // RRF constant, typically 60
        var topN = GetIntConfig(ctx.Config, "top_n", 10);
        var idField = ctx.Config.TryGetValue("id_field", out var idf) ? idf?.ToString() ?? "id" : "id";
        var rankField = ctx.Config.TryGetValue("rank_field", out var rf) ? rf?.ToString() ?? "rank" : "rank";
        var sourceField = ctx.Config.TryGetValue("source_field", out var sf) ? sf?.ToString() ?? "source" : "source";

        var allEntries = ctx.Signals.WindowQuery(windowName);

        // Parse ranking entries: { id, rank, source }
        var rankings = new List<(string Id, int Rank, string Source)>();
        foreach (var entry in allEntries)
        {
            var id = ExtractString(entry.Entity, idField);
            var rank = ExtractInt(entry.Entity, rankField);
            var source = ExtractString(entry.Entity, sourceField) ?? "default";

            if (id != null && rank > 0)
            {
                rankings.Add((id, rank, source));
            }
        }

        // Group by source to get multiple ranking lists
        var sourceGroups = rankings.GroupBy(r => r.Source).ToList();

        // Calculate RRF scores: sum of 1/(k + rank) across all sources
        var rrfScores = new Dictionary<string, double>();
        foreach (var (id, rank, source) in rankings)
        {
            if (!rrfScores.ContainsKey(id))
                rrfScores[id] = 0;

            rrfScores[id] += 1.0 / (k + rank);
        }

        // Sort by RRF score descending
        var ranked = rrfScores
            .OrderByDescending(kvp => kvp.Value)
            .Take(topN)
            .Select((kvp, index) => new { Id = kvp.Key, Score = kvp.Value, Rank = index + 1 })
            .ToList();

        var topItem = ranked.FirstOrDefault();

        ctx.Log($"RRF Scorer: {rankings.Count} rankings from {sourceGroups.Count} sources, k={k}");
        foreach (var item in ranked.Take(5))
        {
            ctx.Log($"  #{item.Rank}: {item.Id} (score={item.Score:F4})");
        }

        ctx.Emit("rrf.scores", rrfScores);
        ctx.Emit("rrf.ranked", ranked);
        ctx.Emit("rrf.top_item", topItem?.Id);
        ctx.Emit("rrf.top_score", topItem?.Score ?? 0);

        return Task.CompletedTask;
    }

    private static string? ExtractString(object? value, string field)
    {
        if (value is IDictionary<string, object> dict && dict.TryGetValue(field, out var v))
            return v?.ToString();
        if (value is JsonElement je && je.TryGetProperty(field, out var prop))
            return prop.ToString();
        return null;
    }

    private static int ExtractInt(object? value, string field)
    {
        var str = ExtractString(value, field);
        return int.TryParse(str, out var i) ? i : 0;
    }

    private static double GetDoubleConfig(Dictionary<string, object> config, string key, double defaultValue)
    {
        if (!config.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            double d => d,
            int i => i,
            string s when double.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetDouble(),
            _ => defaultValue
        };
    }

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
