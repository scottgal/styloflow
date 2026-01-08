using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.Proposers;

/// <summary>
/// Sentiment detector proposer - analyzes sentiment using LLM.
/// Taxonomy: proposer, probabilistic, persistable via escalation
/// </summary>
public sealed class SentimentDetectorProposer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Proposer,
        AtomDeterminism.Probabilistic,
        AtomPersistence.PersistableViaEscalation,
        name: "sentiment-detector",
        reads: ["text.content", "text.analyzed"],
        writes: ["sentiment.score", "sentiment.label", "sentiment.confidence"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var text = ctx.Signals.Get<string>("text.content") ?? "";

        if (string.IsNullOrWhiteSpace(text))
        {
            ctx.Log("No text for sentiment analysis");
            return;
        }

        // Check cache first
        var textHash = ComputeHash(text);
        var cached = await ctx.Storage.GetCachedSentimentAsync(textHash);

        SentimentResult result;
        if (cached != null)
        {
            result = cached;
            ctx.Log($"Sentiment (CACHED): {result.Label} ({result.Score:F2})");
        }
        else
        {
            ctx.Log("Analyzing sentiment with TinyLlama...");
            result = await ctx.Ollama.AnalyzeSentimentAsync(text);

            // Cache the result
            await ctx.Storage.CacheSentimentAsync(textHash, result);
            ctx.Log($"Sentiment: {result.Label} ({result.Score:F2}, confidence: {result.Confidence:F2})");
        }

        ctx.Emit("sentiment.score", result.Score, result.Confidence);
        ctx.Emit("sentiment.label", result.Label, result.Confidence);
        ctx.Emit("sentiment.confidence", result.Confidence);

        // Conditional signals
        if (result.Score > 0.3)
            ctx.Emit("sentiment.is_positive", true, result.Confidence);
        if (result.Score < -0.3)
            ctx.Emit("sentiment.is_negative", true, result.Confidence);
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}
