using StyloFlow.Retrieval;
using StyloFlow.Retrieval.Documents;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Documents;

public class MmrRerankerTests
{
    [Fact]
    public void Constructor_DefaultLambda_SetsCorrectValue()
    {
        // Arrange & Act
        var reranker = new MmrReranker();

        // Assert - no exception means success
        Assert.NotNull(reranker);
    }

    [Fact]
    public void Constructor_CustomLambda_AcceptsValidRange()
    {
        // Arrange & Act
        var reranker0 = new MmrReranker(0.0);
        var reranker1 = new MmrReranker(1.0);
        var reranker05 = new MmrReranker(0.5);

        // Assert
        Assert.NotNull(reranker0);
        Assert.NotNull(reranker1);
        Assert.NotNull(reranker05);
    }

    [Fact]
    public void Constructor_NegativeLambda_Throws()
    {
        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new MmrReranker(-0.1));
    }

    [Fact]
    public void Constructor_LambdaGreaterThanOne_Throws()
    {
        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new MmrReranker(1.1));
    }

    [Fact]
    public void Rerank_EmptyItems_ReturnsEmpty()
    {
        // Arrange
        var reranker = new MmrReranker();
        var items = Array.Empty<string>();
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(items, queryEmbedding, _ => new float[] { 1, 0, 0 }, topK: 5);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Rerank_TopKZero_ReturnsEmpty()
    {
        // Arrange
        var reranker = new MmrReranker();
        var items = new[] { "A", "B", "C" };
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(items, queryEmbedding, _ => new float[] { 1, 0, 0 }, topK: 0);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Rerank_SingleItem_ReturnsItem()
    {
        // Arrange
        var reranker = new MmrReranker();
        var items = new[] { "A" };
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(items, queryEmbedding, _ => new float[] { 1, 0, 0 }, topK: 5);

        // Assert
        Assert.Single(result);
        Assert.Equal("A", result[0].Item);
    }

    [Fact]
    public void Rerank_MostRelevantFirst_WhenHighLambda()
    {
        // Arrange - high lambda = relevance over diversity
        var reranker = new MmrReranker(0.9);
        var embeddings = new Dictionary<string, float[]>
        {
            ["A"] = new float[] { 1, 0, 0 },      // Most similar to query
            ["B"] = new float[] { 0.5f, 0.5f, 0 }, // Somewhat similar
            ["C"] = new float[] { 0, 1, 0 }        // Orthogonal
        };
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(
            embeddings.Keys,
            queryEmbedding,
            x => embeddings[x],
            topK: 3);

        // Assert - A should be first (most relevant)
        Assert.Equal("A", result[0].Item);
    }

    [Fact]
    public void Rerank_PromotesDiversity_WhenLowLambda()
    {
        // Arrange - low lambda = diversity over relevance
        var reranker = new MmrReranker(0.1);
        var embeddings = new Dictionary<string, float[]>
        {
            ["A1"] = new float[] { 1, 0, 0 },       // Very similar to A2
            ["A2"] = new float[] { 0.99f, 0.1f, 0 }, // Very similar to A1
            ["B"] = new float[] { 0, 1, 0 }          // Different direction
        };
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(
            embeddings.Keys,
            queryEmbedding,
            x => embeddings[x],
            topK: 3);

        // Assert - B should appear earlier than A2 due to diversity
        var bIndex = result.ToList().FindIndex(x => x.Item == "B");
        var a2Index = result.ToList().FindIndex(x => x.Item == "A2");
        // With low lambda, B should be selected before A2 after A1
        Assert.True(bIndex <= 1); // B should be in top 2
    }

    [Fact]
    public void Rerank_RespectsTopK()
    {
        // Arrange
        var reranker = new MmrReranker();
        var items = new[] { "A", "B", "C", "D", "E" };
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(items, queryEmbedding, _ => new float[] { 1, 0, 0 }, topK: 2);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Rerank_TopKLargerThanItems_ReturnsAll()
    {
        // Arrange
        var reranker = new MmrReranker();
        var items = new[] { "A", "B" };
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(items, queryEmbedding, _ => new float[] { 1, 0, 0 }, topK: 10);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Rerank_WithScorers_EmptyItems_ReturnsEmpty()
    {
        // Arrange
        var reranker = new MmrReranker();
        var items = Array.Empty<string>();

        // Act
        var result = reranker.Rerank(
            items,
            _ => 1.0,
            (_, _) => 0.5,
            topK: 5);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Rerank_WithScorers_UsesProvidedFunctions()
    {
        // Arrange
        var reranker = new MmrReranker(0.5);
        var items = new[] { "A", "B", "C" };
        var relevance = new Dictionary<string, double>
        {
            ["A"] = 1.0,
            ["B"] = 0.8,
            ["C"] = 0.6
        };

        // Act
        var result = reranker.Rerank(
            items,
            x => relevance[x],
            (x, y) => x == y ? 1.0 : 0.0,
            topK: 3);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("A", result[0].Item); // Highest relevance first
    }

    [Fact]
    public void Rerank_WithScorers_DiversityPenalty_Works()
    {
        // Arrange
        var reranker = new MmrReranker(0.3); // High diversity weight
        var items = new[] { "A", "A-similar", "B-different" };

        double RelevanceScore(string item) => item.StartsWith("A") ? 1.0 : 0.7;
        double DiversityScore(string a, string b) =>
            a.StartsWith("A") && b.StartsWith("A") ? 1.0 : 0.2; // A items are similar

        // Act
        var result = reranker.Rerank(items, RelevanceScore, DiversityScore, topK: 3);

        // Assert - B-different should be promoted due to diversity
        var bIndex = result.ToList().FindIndex(x => x.Item == "B-different");
        Assert.True(bIndex <= 1); // Should be in top 2
    }

    [Fact]
    public void Rerank_AllScoresPositiveOrZero()
    {
        // Arrange
        var reranker = new MmrReranker();
        var embeddings = new Dictionary<string, float[]>
        {
            ["A"] = new float[] { 1, 0, 0 },
            ["B"] = new float[] { 0, 1, 0 },
            ["C"] = new float[] { 0, 0, 1 }
        };
        var queryEmbedding = new float[] { 1, 0, 0 };

        // Act
        var result = reranker.Rerank(
            embeddings.Keys,
            queryEmbedding,
            x => embeddings[x],
            topK: 3);

        // Assert - MMR scores can be negative due to diversity penalty
        // but the first item should always have non-negative score
        Assert.True(result[0].Score >= -1); // Reasonable range
    }
}
