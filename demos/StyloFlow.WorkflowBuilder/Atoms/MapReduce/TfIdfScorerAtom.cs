using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// TF-IDF Scorer - Computes Term Frequency-Inverse Document Frequency scores.
/// Identifies distinctive terms in documents. Pure deterministic computation.
/// </summary>
public sealed class TfIdfScorerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "tfidf-scorer",
        reads: ["*"],
        writes: ["tfidf.scores", "tfidf.top_terms", "tfidf.document_count"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "documents" : "documents";
        var textField = ctx.Config.TryGetValue("text_field", out var txtFld) ? txtFld?.ToString() ?? "text" : "text";
        var topN = GetIntConfig(ctx.Config, "top_n", 10);
        var queryDocument = ctx.Config.TryGetValue("query_document", out var qd) ? qd?.ToString() : null;

        var allEntries = ctx.Signals.WindowQuery(windowName);
        var documents = allEntries
            .Select(e => (Key: e.Key, Text: ExtractText(e.Entity, textField)))
            .Where(d => !string.IsNullOrWhiteSpace(d.Text))
            .ToList();

        if (documents.Count == 0)
        {
            ctx.Log("TF-IDF: no documents found");
            ctx.Emit("tfidf.scores", new Dictionary<string, double>());
            ctx.Emit("tfidf.top_terms", new List<object>());
            ctx.Emit("tfidf.document_count", 0);
            return Task.CompletedTask;
        }

        // Build document frequency index
        var documentFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalDocuments = documents.Count;

        foreach (var (_, text) in documents)
        {
            var terms = ScoringUtilities.TokenizeUnique(text);
            foreach (var term in terms)
            {
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        // Compute TF-IDF for query document (or first document)
        var targetDoc = queryDocument ?? documents.First().Text;
        var targetTokens = ScoringUtilities.Tokenize(targetDoc);
        var termFrequency = targetTokens
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var tfidfScores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (term, tf) in termFrequency)
        {
            var df = documentFrequency.GetValueOrDefault(term, 1);
            var idf = Math.Log((double)totalDocuments / df);
            var tfidf = ((double)tf / targetTokens.Count) * idf;
            tfidfScores[term] = tfidf;
        }

        var topTerms = tfidfScores
            .OrderByDescending(kvp => kvp.Value)
            .Take(topN)
            .Select(kvp => new { Term = kvp.Key, Score = kvp.Value })
            .ToList();

        ctx.Log($"TF-IDF: {totalDocuments} documents, {tfidfScores.Count} unique terms");
        foreach (var term in topTerms.Take(5))
        {
            ctx.Log($"  {term.Term}: {term.Score:F4}");
        }

        ctx.Emit("tfidf.scores", tfidfScores);
        ctx.Emit("tfidf.top_terms", topTerms);
        ctx.Emit("tfidf.document_count", totalDocuments);

        return Task.CompletedTask;
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
