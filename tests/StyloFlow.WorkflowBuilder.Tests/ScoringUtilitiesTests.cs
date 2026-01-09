using StyloFlow.WorkflowBuilder.Atoms.MapReduce;

namespace StyloFlow.WorkflowBuilder.Tests;

public class ScoringUtilitiesTests
{
    #region Cosine Similarity Tests

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        // Arrange
        var a = new float[] { 1.0f, 2.0f, 3.0f };
        var b = new float[] { 1.0f, 2.0f, 3.0f };

        // Act
        var similarity = ScoringUtilities.CosineSimilarity(a, b);

        // Assert
        similarity.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        // Arrange
        var a = new float[] { 1.0f, 0.0f };
        var b = new float[] { 0.0f, 1.0f };

        // Act
        var similarity = ScoringUtilities.CosineSimilarity(a, b);

        // Assert
        similarity.Should().BeApproximately(0.0, 0.0001);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        // Arrange
        var a = new float[] { 1.0f, 0.0f };
        var b = new float[] { -1.0f, 0.0f };

        // Act
        var similarity = ScoringUtilities.CosineSimilarity(a, b);

        // Assert
        similarity.Should().BeApproximately(-1.0, 0.0001);
    }

    [Fact]
    public void CosineSimilarity_DoubleArrays_Works()
    {
        // Arrange
        var a = new double[] { 1.0, 2.0, 3.0 };
        var b = new double[] { 1.0, 2.0, 3.0 };

        // Act
        var similarity = ScoringUtilities.CosineSimilarity(a, b);

        // Assert
        similarity.Should().BeApproximately(1.0, 0.0001);
    }

    #endregion

    #region String Similarity Tests

    [Fact]
    public void JaroWinklerSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Arrange
        var a = "hello world";
        var b = "hello world";

        // Act
        var similarity = ScoringUtilities.JaroWinklerSimilarity(a, b);

        // Assert
        similarity.Should().Be(1.0);
    }

    [Fact]
    public void JaroWinklerSimilarity_SimilarStrings_ReturnsHigh()
    {
        // Arrange
        var a = "hello world";
        var b = "hello word"; // One letter different

        // Act
        var similarity = ScoringUtilities.JaroWinklerSimilarity(a, b);

        // Assert
        similarity.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void JaroWinklerSimilarity_DifferentStrings_ReturnsLow()
    {
        // Arrange
        var a = "hello";
        var b = "world";

        // Act
        var similarity = ScoringUtilities.JaroWinklerSimilarity(a, b);

        // Assert
        similarity.Should().BeLessThan(0.7);
    }

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        // Arrange
        var a = "hello";
        var b = "hello";

        // Act
        var distance = ScoringUtilities.LevenshteinDistance(a, b);

        // Assert
        distance.Should().Be(0);
    }

    [Fact]
    public void LevenshteinDistance_OneEdit_ReturnsOne()
    {
        // Arrange
        var a = "hello";
        var b = "hallo"; // One substitution

        // Act
        var distance = ScoringUtilities.LevenshteinDistance(a, b);

        // Assert
        distance.Should().Be(1);
    }

    [Fact]
    public void LevenshteinDistance_Insertion_CountsCorrectly()
    {
        // Arrange
        var a = "hello";
        var b = "helloo"; // One insertion

        // Act
        var distance = ScoringUtilities.LevenshteinDistance(a, b);

        // Assert
        distance.Should().Be(1);
    }

    [Fact]
    public void NormalizedLevenshteinSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = ScoringUtilities.NormalizedLevenshteinSimilarity("hello", "hello");

        // Assert
        similarity.Should().Be(1.0);
    }

    [Fact]
    public void NGramCosineSimilarity_SimilarStrings_ReturnsHigh()
    {
        // Act
        var similarity = ScoringUtilities.NGramCosineSimilarity("hello world", "hello worlds", 2);

        // Assert
        similarity.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void CombinedStringSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = ScoringUtilities.CombinedStringSimilarity("hello", "hello");

        // Assert
        similarity.Should().Be(1.0);
    }

    #endregion

    #region RRF Tests

    [Fact]
    public void RRFScore_Rank1_ReturnsExpected()
    {
        // Act
        var score = ScoringUtilities.RRFScore(1, 60);

        // Assert
        score.Should().BeApproximately(1.0 / 61, 0.0001);
    }

    [Fact]
    public void RRFScore_HigherRank_ReturnsLowerScore()
    {
        // Act
        var rank1Score = ScoringUtilities.RRFScore(1, 60);
        var rank10Score = ScoringUtilities.RRFScore(10, 60);

        // Assert
        rank1Score.Should().BeGreaterThan(rank10Score);
    }

    [Fact]
    public void FuseRankings_CombinesMultipleLists()
    {
        // Arrange
        var list1 = new[] { "a", "b", "c" };
        var list2 = new[] { "b", "a", "d" };

        // Act
        var scores = ScoringUtilities.FuseRankings(new[] { list1, list2 });

        // Assert
        scores.Should().ContainKey("a");
        scores.Should().ContainKey("b");
        // "b" appears at rank 2 in list1 and rank 1 in list2
        // "a" appears at rank 1 in list1 and rank 2 in list2
        // They should have similar scores
        Math.Abs(scores["a"] - scores["b"]).Should().BeLessThan(0.01);
    }

    #endregion

    #region Tokenization Tests

    [Fact]
    public void Tokenize_SplitsOnWhitespace()
    {
        // Act
        var tokens = ScoringUtilities.Tokenize("hello world");

        // Assert
        tokens.Should().Contain("hello");
        tokens.Should().Contain("world");
    }

    [Fact]
    public void Tokenize_RemovesSingleCharTokens()
    {
        // Act
        var tokens = ScoringUtilities.Tokenize("a quick fox");

        // Assert
        tokens.Should().NotContain("a");
        tokens.Should().Contain("quick");
        tokens.Should().Contain("fox");
    }

    [Fact]
    public void TokenizeUnique_ReturnsUniqueTokens()
    {
        // Act
        var tokens = ScoringUtilities.TokenizeUnique("hello hello world");

        // Assert
        tokens.Should().HaveCount(2);
        tokens.Should().Contain("hello");
        tokens.Should().Contain("world");
    }

    #endregion

    #region Centroid Tests

    [Fact]
    public void ComputeCentroid_SingleVector_ReturnsSame()
    {
        // Arrange
        var vectors = new List<float[]> { new float[] { 1.0f, 2.0f, 3.0f } };

        // Act
        var centroid = ScoringUtilities.ComputeCentroid(vectors);

        // Assert
        centroid.Should().BeEquivalentTo(new float[] { 1.0f, 2.0f, 3.0f });
    }

    [Fact]
    public void ComputeCentroid_MultipleVectors_ReturnsAverage()
    {
        // Arrange
        var vectors = new List<float[]>
        {
            new float[] { 0.0f, 0.0f },
            new float[] { 2.0f, 4.0f }
        };

        // Act
        var centroid = ScoringUtilities.ComputeCentroid(vectors);

        // Assert
        centroid[0].Should().BeApproximately(1.0f, 0.001f);
        centroid[1].Should().BeApproximately(2.0f, 0.001f);
    }

    #endregion
}
