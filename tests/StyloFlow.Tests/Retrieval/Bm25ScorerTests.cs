using StyloFlow.Retrieval;
using Xunit;

namespace StyloFlow.Tests.Retrieval;

public class Bm25ScorerTests
{
    [Fact]
    public void DefaultConstructor_SetsDefaultParameters()
    {
        // Arrange & Act
        var scorer = new Bm25Scorer();

        // Assert
        Assert.Equal("BM25", scorer.Name);
    }

    [Fact]
    public void Constructor_WithCustomParameters_AcceptsValidValues()
    {
        // Arrange & Act
        var scorer = new Bm25Scorer(k1: 2.0, b: 0.5);

        // Assert
        Assert.Equal("BM25", scorer.Name);
    }

    [Fact]
    public void Constructor_NegativeK1_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25Scorer(k1: -1, b: 0.75));
    }

    [Fact]
    public void Constructor_NegativeB_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25Scorer(k1: 1.5, b: -0.1));
    }

    [Fact]
    public void Constructor_BGreaterThanOne_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bm25Scorer(k1: 1.5, b: 1.5));
    }

    [Fact]
    public void Score_EmptyQuery_ReturnsZero()
    {
        // Arrange
        var scorer = new Bm25Scorer();
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
        var scorer = new Bm25Scorer();
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
        var scorer = new Bm25Scorer();
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
        var scorer = new Bm25Scorer();
        var query = new List<string> { "foo" };
        var document = new List<string> { "hello", "world" };

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void Score_CaseInsensitive_MatchesRegardlessOfCase()
    {
        // Arrange
        var scorer = new Bm25Scorer();
        var query = new List<string> { "HELLO" };
        var document = new List<string> { "hello", "world" };

        // Act
        var score = scorer.Score(query, document);

        // Assert
        Assert.True(score > 0);
    }

    [Fact]
    public void Score_WithStrings_TokenizesAndScores()
    {
        // Arrange
        var scorer = new Bm25Scorer();

        // Act
        var score = scorer.Score("hello world", "hello beautiful world");

        // Assert
        Assert.True(score > 0);
    }

    [Fact]
    public void Score_MoreMatchingTerms_HigherScore()
    {
        // Arrange
        var scorer = new Bm25Scorer();
        var query = new List<string> { "hello", "world" };
        var doc1 = new List<string> { "hello" };
        var doc2 = new List<string> { "hello", "world" };

        // Act
        var score1 = scorer.Score(query, doc1);
        var score2 = scorer.Score(query, doc2);

        // Assert
        Assert.True(score2 > score1);
    }

    [Fact]
    public void Initialize_BuildsCorpusStatistics()
    {
        // Arrange
        var scorer = new Bm25Scorer();
        var documents = new[]
        {
            "the quick brown fox",
            "jumped over the lazy dog",
            "the brown dog ran"
        };

        // Act
        scorer.Initialize(documents);

        // Assert - Scoring with corpus should differ from without
        var query = new List<string> { "brown" };
        var doc = new List<string> { "brown", "fox" };
        var score = scorer.Score(query, doc);
        Assert.True(score > 0);
    }

    [Fact]
    public void ScoreAll_ReturnsRankedResults()
    {
        // Arrange
        var scorer = new Bm25Scorer();
        var items = new[] { "cat sat on mat", "dog ran in park", "cat and dog are pets" };

        // Act
        var results = scorer.ScoreAll(items, x => x, "cat");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Item.Contains("cat"));
    }

    [Fact]
    public void ScoreAll_OrderedByScoreDescending()
    {
        // Arrange
        var scorer = new Bm25Scorer();
        var items = new[] { "cat cat cat", "cat", "no match" };

        // Act
        var results = scorer.ScoreAll(items, x => x, "cat");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Score >= results[1].Score);
    }

    [Fact]
    public void Tokenize_SplitsOnWordBoundaries()
    {
        // Act
        var tokens = Bm25Scorer.Tokenize("Hello, World! This is a test.");

        // Assert
        Assert.Contains("hello", tokens);
        Assert.Contains("world", tokens);
        Assert.Contains("this", tokens);
        Assert.Contains("is", tokens);
        Assert.Contains("test", tokens);
    }

    [Fact]
    public void Tokenize_RemovesSingleCharacters()
    {
        // Act
        var tokens = Bm25Scorer.Tokenize("I am a test");

        // Assert
        Assert.DoesNotContain("i", tokens);
        Assert.DoesNotContain("a", tokens);
        Assert.Contains("am", tokens);
        Assert.Contains("test", tokens);
    }

    [Fact]
    public void Tokenize_LowercasesTokens()
    {
        // Act
        var tokens = Bm25Scorer.Tokenize("HELLO World");

        // Assert
        Assert.Contains("hello", tokens);
        Assert.Contains("world", tokens);
        Assert.DoesNotContain("HELLO", tokens);
    }

    [Fact]
    public void Constructor_WithCorpus_UsesProvidedStatistics()
    {
        // Arrange
        var documents = new[]
        {
            new List<string> { "quick", "brown", "fox" },
            new List<string> { "lazy", "brown", "dog" }
        };
        var corpus = Bm25Corpus.Build(documents);

        // Act
        var scorer = new Bm25Scorer(corpus);

        // Assert
        var score = scorer.Score(new List<string> { "brown" }, new List<string> { "brown", "cat" });
        Assert.True(score > 0);
    }
}

public class Bm25CorpusTests
{
    [Fact]
    public void Build_EmptyDocuments_ReturnsValidCorpus()
    {
        // Arrange & Act
        var corpus = Bm25Corpus.Build(Array.Empty<List<string>>());

        // Assert
        Assert.Equal(0, corpus.DocumentCount);
        Assert.Equal(0, corpus.AverageDocumentLength);
    }

    [Fact]
    public void Build_SingleDocument_CalculatesCorrectStatistics()
    {
        // Arrange
        var documents = new[] { new List<string> { "hello", "world" } };

        // Act
        var corpus = Bm25Corpus.Build(documents);

        // Assert
        Assert.Equal(1, corpus.DocumentCount);
        Assert.Equal(2.0, corpus.AverageDocumentLength);
    }

    [Fact]
    public void Build_MultipleDocuments_CalculatesAverageLength()
    {
        // Arrange
        var documents = new[]
        {
            new List<string> { "a", "b" },     // 2 terms
            new List<string> { "c", "d", "e", "f" }  // 4 terms
        };

        // Act
        var corpus = Bm25Corpus.Build(documents);

        // Assert
        Assert.Equal(2, corpus.DocumentCount);
        Assert.Equal(3.0, corpus.AverageDocumentLength); // (2 + 4) / 2 = 3
    }

    [Fact]
    public void GetDocumentFrequency_ExistingTerm_ReturnsCount()
    {
        // Arrange
        var documents = new[]
        {
            new List<string> { "hello", "world" },
            new List<string> { "hello", "there" },
            new List<string> { "goodbye" }
        };
        var corpus = Bm25Corpus.Build(documents);

        // Act & Assert
        Assert.Equal(2, corpus.GetDocumentFrequency("hello"));
        Assert.Equal(1, corpus.GetDocumentFrequency("world"));
        Assert.Equal(1, corpus.GetDocumentFrequency("goodbye"));
    }

    [Fact]
    public void GetDocumentFrequency_NonExistingTerm_ReturnsZero()
    {
        // Arrange
        var documents = new[] { new List<string> { "hello" } };
        var corpus = Bm25Corpus.Build(documents);

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
        var corpus = Bm25Corpus.Build(documents);

        // Act & Assert
        Assert.Equal(2, corpus.GetDocumentFrequency("hello"));
        Assert.Equal(2, corpus.GetDocumentFrequency("HELLO"));
    }

    [Fact]
    public void Build_DuplicateTermsInDocument_CountsOnce()
    {
        // Arrange - "hello" appears twice in doc but should count as 1 doc frequency
        var documents = new[]
        {
            new List<string> { "hello", "hello", "world" }
        };
        var corpus = Bm25Corpus.Build(documents);

        // Act & Assert
        Assert.Equal(1, corpus.GetDocumentFrequency("hello"));
    }
}
