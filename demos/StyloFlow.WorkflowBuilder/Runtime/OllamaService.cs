using System.Text.Json;
using OllamaSharp;
using OllamaSharp.Models;

namespace StyloFlow.WorkflowBuilder.Runtime;

// Extension for ToListAsync
file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }
}

/// <summary>
/// Service for interacting with Ollama/TinyLlama for text analysis.
/// Includes caching to avoid repeated LLM calls for identical inputs.
/// </summary>
public class OllamaService
{
    private readonly OllamaApiClient _client;
    private readonly Dictionary<string, CachedResult> _cache = new();
    private readonly string _model;

    public OllamaService(string baseUrl = "http://localhost:11434", string model = "tinyllama")
    {
        _client = new OllamaApiClient(new Uri(baseUrl));
        _model = model;
    }

    /// <summary>
    /// Analyze sentiment of text using TinyLlama.
    /// Returns score from -1.0 (negative) to 1.0 (positive).
    /// </summary>
    public async Task<SentimentResult> AnalyzeSentimentAsync(string text)
    {
        var cacheKey = $"sentiment:{ComputeHash(text)}";

        if (_cache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.Timestamp < TimeSpan.FromHours(1))
        {
            return (SentimentResult)cached.Value;
        }

        var prompt = $$"""
            Analyze the sentiment of this text and respond with ONLY a JSON object.
            No explanation, just the JSON.

            Text: "{{text}}"

            Respond with exactly this format:
            {"score": <number from -1 to 1>, "label": "<positive|negative|neutral>", "confidence": <number from 0 to 1>}
            """;

        try
        {
            var response = await _client.GenerateAsync(new GenerateRequest
            {
                Model = _model,
                Prompt = prompt,
                Stream = false
            }).ToListAsync();

            var responseText = string.Join("", response.Select(r => r.Response));

            // Extract JSON from response
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<SentimentResult>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null)
                {
                    parsed.FromCache = false;
                    _cache[cacheKey] = new CachedResult { Value = parsed, Timestamp = DateTime.UtcNow };
                    return parsed;
                }
            }

            // Fallback: simple keyword analysis
            return AnalyzeSentimentSimple(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ollama error: {ex.Message}. Using simple analysis.");
            return AnalyzeSentimentSimple(text);
        }
    }

    /// <summary>
    /// Analyze text and extract key information.
    /// </summary>
    public async Task<TextAnalysisResult> AnalyzeTextAsync(string text)
    {
        var words = text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var sentences = text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);

        return new TextAnalysisResult
        {
            WordCount = words.Length,
            CharCount = text.Length,
            SentenceCount = sentences.Length,
            Content = text,
            ProcessedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Generate embeddings for text (for vector search).
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        try
        {
            var response = await _client.EmbedAsync(new EmbedRequest
            {
                Model = "nomic-embed-text",
                Input = [text]
            });

            return response.Embeddings?.FirstOrDefault()?.ToArray() ?? GenerateSimpleEmbedding(text);
        }
        catch
        {
            return GenerateSimpleEmbedding(text);
        }
    }

    private SentimentResult AnalyzeSentimentSimple(string text)
    {
        var lower = text.ToLowerInvariant();

        var positiveWords = new[] { "good", "great", "excellent", "amazing", "wonderful", "fantastic", "love", "happy", "best", "awesome" };
        var negativeWords = new[] { "bad", "terrible", "awful", "horrible", "hate", "worst", "angry", "sad", "poor", "disappointing" };

        var positiveCount = positiveWords.Count(w => lower.Contains(w));
        var negativeCount = negativeWords.Count(w => lower.Contains(w));

        var total = positiveCount + negativeCount;
        if (total == 0)
        {
            return new SentimentResult { Score = 0, Label = "neutral", Confidence = 0.5, FromCache = false };
        }

        var score = (double)(positiveCount - negativeCount) / total;
        var label = score > 0.2 ? "positive" : score < -0.2 ? "negative" : "neutral";

        return new SentimentResult
        {
            Score = Math.Clamp(score, -1, 1),
            Label = label,
            Confidence = 0.6 + (Math.Abs(score) * 0.3),
            FromCache = false
        };
    }

    private float[] GenerateSimpleEmbedding(string text)
    {
        // Simple hash-based embedding for demo
        var embedding = new float[384];
        var hash = text.GetHashCode();
        var random = new Random(hash);

        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        // Normalize
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] /= (float)magnitude;
        }

        return embedding;
    }

    private static string ComputeHash(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash)[..16];
    }

    private class CachedResult
    {
        public required object Value { get; init; }
        public DateTime Timestamp { get; init; }
    }
}

public class SentimentResult
{
    public double Score { get; set; }
    public string Label { get; set; } = "neutral";
    public double Confidence { get; set; } = 0.5;
    public bool FromCache { get; set; }
}

public class TextAnalysisResult
{
    public int WordCount { get; set; }
    public int CharCount { get; set; }
    public int SentenceCount { get; set; }
    public string Content { get; set; } = "";
    public DateTime ProcessedAt { get; set; }
}
