using System.Text.Json;
using System.Text.RegularExpressions;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// BM25 (Best Matching 25) - Text relevance scoring algorithm.
/// Classic information retrieval scoring function.
/// Pure deterministic computation - no LLM.
/// </summary>
public sealed class BM25ScorerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "bm25-scorer",
        reads: ["*"],
        writes: ["bm25.scores", "bm25.ranked", "bm25.top_item", "bm25.top_score"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "documents" : "documents";
        var query = ctx.Config.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
        var textField = ctx.Config.TryGetValue("text_field", out var txtFld) ? txtFld?.ToString() ?? "text" : "text";
        var idField = ctx.Config.TryGetValue("id_field", out var idFld) ? idFld?.ToString() ?? "id" : "id";
        var k1 = GetDoubleConfig(ctx.Config, "k1", 1.2);   // Term frequency saturation
        var b = GetDoubleConfig(ctx.Config, "b", 0.75);    // Length normalization
        var topN = GetIntConfig(ctx.Config, "top_n", 10);

        // Also check for query in signals
        if (string.IsNullOrEmpty(query))
        {
            query = ctx.Signals.Get<string>("query") ?? ctx.Signals.Get<string>("search.query") ?? "";
        }

        var allEntries = ctx.Signals.WindowQuery(windowName);

        // Parse documents
        var documents = new List<BM25Document>();
        foreach (var entry in allEntries)
        {
            var id = ExtractString(entry.Entity, idField) ?? entry.Key;
            var text = ExtractString(entry.Entity, textField) ?? "";
            documents.Add(new BM25Document(id, text, Tokenize(text)));
        }

        if (documents.Count == 0 || string.IsNullOrEmpty(query))
        {
            ctx.Log($"BM25 Scorer: no documents or empty query");
            ctx.Emit("bm25.scores", new Dictionary<string, double>());
            ctx.Emit("bm25.ranked", new List<object>());
            ctx.Emit("bm25.top_item", null);
            ctx.Emit("bm25.top_score", 0.0);
            return Task.CompletedTask;
        }

        var queryTerms = Tokenize(query);
        var avgDocLength = documents.Average(d => d.Tokens.Length);

        // Calculate IDF for query terms
        var idf = new Dictionary<string, double>();
        foreach (var term in queryTerms.Distinct())
        {
            var docsWithTerm = documents.Count(d => d.Tokens.Contains(term));
            idf[term] = Math.Log((documents.Count - docsWithTerm + 0.5) / (docsWithTerm + 0.5) + 1);
        }

        // Calculate BM25 score for each document
        var scores = new Dictionary<string, double>();
        foreach (var doc in documents)
        {
            var score = 0.0;
            var docLength = doc.Tokens.Length;

            foreach (var term in queryTerms.Distinct())
            {
                var tf = doc.Tokens.Count(t => t == term);
                if (tf > 0 && idf.TryGetValue(term, out var termIdf))
                {
                    var numerator = tf * (k1 + 1);
                    var denominator = tf + k1 * (1 - b + b * docLength / avgDocLength);
                    score += termIdf * numerator / denominator;
                }
            }

            scores[doc.Id] = score;
        }

        var ranked = scores
            .OrderByDescending(kvp => kvp.Value)
            .Take(topN)
            .Select((kvp, i) => new { Id = kvp.Key, Score = kvp.Value, Rank = i + 1 })
            .ToList();

        var topItem = ranked.FirstOrDefault();

        ctx.Log($"BM25 Scorer: query='{query}', {documents.Count} docs, k1={k1}, b={b}");
        foreach (var item in ranked.Take(5))
        {
            ctx.Log($"  #{item.Rank}: {item.Id} (score={item.Score:F4})");
        }

        ctx.Emit("bm25.scores", scores);
        ctx.Emit("bm25.ranked", ranked);
        ctx.Emit("bm25.top_item", topItem?.Id);
        ctx.Emit("bm25.top_score", topItem?.Score ?? 0);

        return Task.CompletedTask;
    }

    private static string[] Tokenize(string text)
    {
        return Regex.Split(text.ToLowerInvariant(), @"\W+")
            .Where(t => t.Length > 1)
            .ToArray();
    }

    private static string? ExtractString(object? value, string field)
    {
        if (value is IDictionary<string, object> dict && dict.TryGetValue(field, out var v))
            return v?.ToString();
        if (value is JsonElement je && je.TryGetProperty(field, out var prop))
            return prop.ToString();
        return null;
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

    private sealed record BM25Document(string Id, string Text, string[] Tokens);
}
