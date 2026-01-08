using StyloFlow.Retrieval;
using Xunit;

namespace StyloFlow.Tests.Retrieval;

public class TfIdfScorerTests
{
    [Fact]
    public void DefaultConstructor_SetsDefaultVariants()
    {
        // Arrange & Act
        var scorer = new TfIdfScorer();

        // Assert
        Assert.Equal("TF-IDF", scorer.Name);
    }

    [Fact]
    public void Constructor_WithVariants_AcceptsAllCombinations()
    {
        // Arrange & Act
        foreach (var tf in Enum.GetValues<TfVariant>())
        {
            foreach (var idf in Enum.GetValues<IdfVariant>())
            {
                var scorer = new TfIdfScorer(tf, idf);
                Assert.Equal("TF-IDF", scorer.Name);
            }
        }
    }

    [Fact]
    public void Score_EmptyQuery_ReturnsZero()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        var query = new List<string>();
        var document = new List<string> { "hello", "world" };

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Score_EmptyDocument_ReturnsZero()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        var query = new List<string> { "hello" };
        var document = new List<string>();

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Score_MatchingTerms_ReturnsPositiveScore()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        var query = new List<string> { "hello" };
        var document = new List<string> { "hello", "world" };

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.True(score > 0);
    }

    [Fact]
    public void Score_NoMatchingTerms_ReturnsZero()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        var query = new List<string> { "foo" };
        var document = new List<string> { "hello", "world" };

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Score_WithStrings_TokenizesAndScores()
    {
        // Arrange
        var scorer = new TfIdfScorer();

        // Act
        var score = scorer.Score("machine learning", "machine learning algorithms");

        // Assert
        Assert.True(score > 0);
    }

    [Fact]
    public void Initialize_BuildsCorpusStatistics()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        var documents = new[]
        {
            "machine learning algorithms",
            "deep learning neural networks",
            "algorithms and data structures"
        };

        // Act
        scorer.Initialize(documents);

        // Assert - rare terms should have higher IDF
        var query = new List<string> { "machine" };
        var doc = new List<string> { "machine", "learning" };
        var score = scorer.Score(query, doc);
        Assert.True(score > 0);
    }

    [Fact]
    public void ComputeTermTfIdf_ReturnsScoreForTerm()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        scorer.Initialize(new[] { "cat dog animal", "cat mouse creature", "dog bone treat" });

        // Act
        var score = scorer.ComputeTermTfIdf("cat", "cat sat mat creature animal");

        // Assert
        Assert.True(score >= 0);
    }

    [Fact]
    public void GetDistinctiveTerms_ReturnsTopNTerms()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        scorer.Initialize(new[] { "common common common", "unique rare special" });

        // Act
        var terms = scorer.GetDistinctiveTerms("unique rare special terms", topN: 3);

        // Assert
        Assert.True(terms.Count <= 3);
        Assert.All(terms, t => Assert.True(t.Score >= 0));
    }

    [Fact]
    public void GetDistinctiveTerms_OrderedByScoreDescending()
    {
        // Arrange
        var scorer = new TfIdfScorer();
        scorer.Initialize(new[] { "test document", "another test" });

        // Act
        var terms = scorer.GetDistinctiveTerms("test document with some words", topN: 10);

        // Assert
        for (int i = 1; i < terms.Count; i++)
        {
            Assert.True(terms[i - 1].Score >= terms[i].Score);
        }
    }

    [Fact]
    public void Tokenize_RemovesStopWords()
    {
        // Act
        var tokens = TfIdfScorer.Tokenize("The quick brown fox jumps over the lazy dog");

        // Assert - TF-IDF implementation removes common stop words
        Assert.DoesNotContain("the", tokens);
        Assert.Contains("quick", tokens);
        Assert.Contains("brown", tokens);
        Assert.Contains("fox", tokens);
        Assert.Contains("jumps", tokens);
        Assert.Contains("lazy", tokens);
        Assert.Contains("dog", tokens);
    }

    [Fact]
    public void Tokenize_RemovesShortWords()
    {
        // Act
        var tokens = TfIdfScorer.Tokenize("I am a big dog");

        // Assert
        Assert.DoesNotContain("am", tokens);
        Assert.Contains("big", tokens);
        Assert.Contains("dog", tokens);
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmptyList()
    {
        // Act
        var tokens = TfIdfScorer.Tokenize("");

        // Assert
        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_NullString_ReturnsEmptyList()
    {
        // Act
        var tokens = TfIdfScorer.Tokenize(null!);

        // Assert
        Assert.Empty(tokens);
    }

    [Fact]
    public void Constructor_WithCorpus_UsesProvidedStatistics()
    {
        // Arrange
        var documents = new[]
        {
            new List<string> { "machine", "learning" },
            new List<string> { "deep", "learning" }
        };
        var corpus = TfIdfCorpus.Build(documents);

        // Act
        var scorer = new TfIdfScorer(corpus);

        // Assert
        var score = scorer.Score(new List<string> { "machine" }, new List<string> { "machine", "test" });
        Assert.True(score > 0);
    }

    [Theory]
    [InlineData(TfVariant.Raw)]
    [InlineData(TfVariant.Boolean)]
    [InlineData(TfVariant.LogNormalized)]
    [InlineData(TfVariant.DoubleNormalized)]
    [InlineData(TfVariant.AugmentedNormalized)]
    public void Score_DifferentTfVariants_ProducesValidScore(TfVariant variant)
    {
        // Arrange
        var scorer = new TfIdfScorer(variant, IdfVariant.Smooth);
        var query = new List<string> { "test" };
        var document = new List<string> { "test", "document", "test" };

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.True(score >= 0);
    }

    [Theory]
    [InlineData(IdfVariant.Standard)]
    [InlineData(IdfVariant.Smooth)]
    [InlineData(IdfVariant.Probabilistic)]
    public void Score_DifferentIdfVariants_ProducesValidScore(IdfVariant variant)
    {
        // Arrange
        var scorer = new TfIdfScorer(TfVariant.LogNormalized, variant);
        scorer.Initialize(new[] { "test document", "another test", "more documents" });
        var query = new List<string> { "test" };
        var document = new List<string> { "test", "document" };

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.True(score >= 0);
    }
}

public class TfIdfCorpusTests
{
    [Fact]
    public void Build_EmptyDocuments_ReturnsValidCorpus()
    {
        // Arrange & Act
        var corpus = TfIdfCorpus.Build(Array.Empty<List<string>>());

        // Assert
        Assert.Equal(0, corpus.DocumentCount);
    }

    [Fact]
    public void Build_SingleDocument_SetsDocumentCount()
    {
        // Arrange
        var documents = new[] { new List<string> { "hello", "world" } };

        // Act
        var corpus = TfIdfCorpus.Build(documents);

        // Assert
        Assert.Equal(1, corpus.DocumentCount);
    }

    [Fact]
    public void GetDocumentFrequency_ExistingTerm_ReturnsCount()
    {
        // Arrange
        var documents = new[]
        {
            new List<string> { "machine", "learning" },
            new List<string> { "machine", "vision" },
            new List<string> { "deep", "learning" }
        };
        var corpus = TfIdfCorpus.Build(documents);

        // Act & Assert
        Assert.Equal(2, corpus.GetDocumentFrequency("machine"));
        Assert.Equal(2, corpus.GetDocumentFrequency("learning"));
        Assert.Equal(1, corpus.GetDocumentFrequency("vision"));
    }

    [Fact]
    public void GetDocumentFrequency_NonExistingTerm_ReturnsZero()
    {
        // Arrange
        var documents = new[] { new List<string> { "hello" } };
        var corpus = TfIdfCorpus.Build(documents);

        // Act & Assert
        Assert.Equal(0, corpus.GetDocumentFrequency("nonexistent"));
    }

    [Fact]
    public void GetDocumentFrequency_CaseInsensitive()
    {
        // Arrange
        var documents = new[]
        {
            new List<string> { "Hello" },
            new List<string> { "HELLO" }
        };
        var corpus = TfIdfCorpus.Build(documents);

        // Act & Assert
        Assert.Equal(2, corpus.GetDocumentFrequency("hello"));
    }
}
