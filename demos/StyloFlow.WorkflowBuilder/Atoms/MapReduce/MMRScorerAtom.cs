using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MapReduce;

/// <summary>
/// MMR (Maximal Marginal Relevance) - Diversity-aware ranking that balances relevance and novelty.
/// Score = λ * Sim(doc, query) - (1-λ) * max(Sim(doc, selected_docs))
/// Pure deterministic computation - no LLM.
/// </summary>
public sealed class MMRScorerAtom
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "mmr-scorer",
        reads: ["*"],
        writes: ["mmr.ranked", "mmr.selected", "mmr.diversity_score"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var windowName = ctx.Config.TryGetValue("window_name", out var wn) ? wn?.ToString() ?? "candidates" : "candidates";
        var lambda = GetDoubleConfig(ctx.Config, "lambda", 0.7); // Balance: 1=pure relevance, 0=pure diversity
        var topN = GetIntConfig(ctx.Config, "top_n", 10);
        var idField = ctx.Config.TryGetValue("id_field", out var idf) ? idf?.ToString() ?? "id" : "id";
        var scoreField = ctx.Config.TryGetValue("score_field", out var sf) ? sf?.ToString() ?? "score" : "score";
        var embeddingField = ctx.Config.TryGetValue("embedding_field", out var ef) ? ef?.ToString() ?? "embedding" : "embedding";

        var allEntries = ctx.Signals.WindowQuery(windowName);

        // Parse candidates with scores and optional embeddings
        var candidates = new List<MMRCandidate>();
        foreach (var entry in allEntries)
        {
            var id = ExtractString(entry.Entity, idField);
            var score = ExtractDouble(entry.Entity, scoreField);
            var embedding = ExtractEmbedding(entry.Entity, embeddingField);

            if (id != null)
            {
                candidates.Add(new MMRCandidate(id, score, embedding ?? GenerateSimpleEmbedding(id)));
            }
        }

        if (candidates.Count == 0)
        {
            ctx.Log("MMR Scorer: no candidates found");
            ctx.Emit("mmr.ranked", new List<object>());
            ctx.Emit("mmr.selected", new List<string>());
            ctx.Emit("mmr.diversity_score", 0.0);
            return Task.CompletedTask;
        }

        // Greedy MMR selection
        var selected = new List<MMRCandidate>();
        var remaining = new HashSet<MMRCandidate>(candidates);

        while (selected.Count < topN && remaining.Count > 0)
        {
            MMRCandidate? best = null;
            double bestScore = double.MinValue;

            foreach (var candidate in remaining)
            {
                // Relevance component
                var relevance = candidate.Score;

                // Diversity component (max similarity to already selected)
                var maxSimilarity = selected.Count > 0
                    ? selected.Max(s => CosineSimilarity(candidate.Embedding, s.Embedding))
                    : 0;

                // MMR score
                var mmrScore = lambda * relevance - (1 - lambda) * maxSimilarity;

                if (mmrScore > bestScore)
                {
                    bestScore = mmrScore;
                    best = candidate;
                }
            }

            if (best != null)
            {
                selected.Add(best);
                remaining.Remove(best);
            }
        }

        // Calculate overall diversity score
        var diversityScore = CalculateDiversityScore(selected);

        var ranked = selected.Select((c, i) => new { Id = c.Id, Score = c.Score, Rank = i + 1 }).ToList();

        ctx.Log($"MMR Scorer: {candidates.Count} candidates, λ={lambda}, selected {selected.Count}");
        ctx.Log($"  Diversity score: {diversityScore:F3}");
        foreach (var item in ranked.Take(5))
        {
            ctx.Log($"  #{item.Rank}: {item.Id} (relevance={item.Score:F3})");
        }

        ctx.Emit("mmr.ranked", ranked);
        ctx.Emit("mmr.selected", selected.Select(s => s.Id).ToList());
        ctx.Emit("mmr.diversity_score", diversityScore);

        return Task.CompletedTask;
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        var dotProduct = a.Zip(b, (x, y) => x * y).Sum();
        var magnitudeA = Math.Sqrt(a.Sum(x => x * x));
        var magnitudeB = Math.Sqrt(b.Sum(x => x * x));

        if (magnitudeA == 0 || magnitudeB == 0) return 0;
        return dotProduct / (magnitudeA * magnitudeB);
    }

    private static double CalculateDiversityScore(List<MMRCandidate> selected)
    {
        if (selected.Count < 2) return 1.0;

        // Average pairwise dissimilarity
        var totalDissimilarity = 0.0;
        var pairs = 0;

        for (int i = 0; i < selected.Count; i++)
        {
            for (int j = i + 1; j < selected.Count; j++)
            {
                totalDissimilarity += 1 - CosineSimilarity(selected[i].Embedding, selected[j].Embedding);
                pairs++;
            }
        }

        return pairs > 0 ? totalDissimilarity / pairs : 1.0;
    }

    private static double[] GenerateSimpleEmbedding(string text)
    {
        // Simple hash-based pseudo-embedding for demo
        var hash = text.GetHashCode();
        var embedding = new double[8];
        for (int i = 0; i < 8; i++)
        {
            embedding[i] = ((hash >> (i * 4)) & 0xF) / 15.0;
        }
        return embedding;
    }

    private static string? ExtractString(object? value, string field)
    {
        if (value is IDictionary<string, object> dict && dict.TryGetValue(field, out var v))
            return v?.ToString();
        if (value is JsonElement je && je.TryGetProperty(field, out var prop))
            return prop.ToString();
        return null;
    }

    private static double ExtractDouble(object? value, string field)
    {
        var str = ExtractString(value, field);
        return double.TryParse(str, out var d) ? d : 0;
    }

    private static double[]? ExtractEmbedding(object? value, string field)
    {
        if (value is IDictionary<string, object> dict && dict.TryGetValue(field, out var v))
        {
            if (v is double[] arr) return arr;
            if (v is IEnumerable<double> enumerable) return enumerable.ToArray();
        }
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

    private sealed record MMRCandidate(string Id, double Score, double[] Embedding);
}
