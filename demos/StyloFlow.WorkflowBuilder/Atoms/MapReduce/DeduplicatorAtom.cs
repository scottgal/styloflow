using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// Deduplicator - Removes near-duplicates using string similarity.
/// Uses configurable similarity threshold and algorithm (jaro-winkler, levenshtein, combined).
/// </summary>
public sealed class DeduplicatorAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "deduplicator",
        reads: ["*"],
        writes: ["dedup.unique", "dedup.duplicates_removed", "dedup.clusters"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "items" : "items";
        var outputWindow = ctx.Config.TryGetValue("output_window", out var ow) ? ow?.ToString() ?? "deduplicated" : "deduplicated";
        var textField = ctx.Config.TryGetValue("text_field", out var tf) ? tf?.ToString() ?? "text" : "text";
        var threshold = GetDoubleConfig(ctx.Config, "threshold", 0.85);
        var algorithm = ctx.Config.TryGetValue("algorithm", out var alg) ? alg?.ToString()?.ToLower() ?? "combined" : "combined";

        var allEntries = ctx.Signals.WindowQuery(windowName);
        var items = allEntries.Select(e => (Entry: e, Text: ExtractText(e.Entity, textField))).ToList();

        var unique = new List<(WindowEntry Entry, string Text, List<string> Aliases)>();
        var duplicatesRemoved = 0;

        foreach (var (entry, text) in items)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;

            // Find best match in existing unique items
            var bestMatch = unique
                .Select(u => (Item: u, Similarity: ComputeSimilarity(text, u.Text, algorithm)))
                .Where(x => x.Similarity >= threshold)
                .OrderByDescending(x => x.Similarity)
                .FirstOrDefault();

            if (bestMatch.Item.Entry != null)
            {
                // Duplicate found - add as alias
                bestMatch.Item.Aliases.Add(entry.Key);
                duplicatesRemoved++;
            }
            else
            {
                // New unique item
                unique.Add((entry, text, new List<string>()));
            }
        }

        // Store unique items in output window
        var outputWin = ctx.Signals.GetWindow(outputWindow, unique.Count * 2, TimeSpan.FromMinutes(30));
        foreach (var (entry, _, aliases) in unique)
        {
            var deduped = new
            {
                Original = entry.Entity,
                AliasCount = aliases.Count,
                Aliases = aliases
            };
            ctx.Signals.WindowAdd(outputWindow, entry.Key, deduped);
        }

        ctx.Log($"Deduplicator: {items.Count} → {unique.Count} unique (removed {duplicatesRemoved}, threshold={threshold:F2})");

        ctx.Emit("dedup.unique", unique.Select(u => u.Entry.Key).ToList());
        ctx.Emit("dedup.duplicates_removed", duplicatesRemoved);
        ctx.Emit("dedup.clusters", unique.Count);

        return Task.CompletedTask;
    }

    private static double ComputeSimilarity(string a, string b, string algorithm)
    {
        return algorithm switch
        {
            "jaro" or "jarowinkler" => ScoringUtilities.JaroWinklerSimilarity(a, b),
            "levenshtein" => ScoringUtilities.NormalizedLevenshteinSimilarity(a, b),
            "ngram" => ScoringUtilities.NGramCosineSimilarity(a, b),
            _ => ScoringUtilities.CombinedStringSimilarity(a, b)
        };
    }

    private static string ExtractText(object? entity, string field)
    {
        if (entity is string s) return s;

        if (entity is IDictionary<string, object> dict && dict.TryGetValue(field, out var v))
            return v?.ToString() ?? "";

        if (entity is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String) return je.GetString() ?? "";
            if (je.TryGetProperty(field, out var prop)) return prop.ToString();
        }

        return entity?.ToString() ?? "";
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
}
