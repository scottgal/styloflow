using StyloFlow.Retrieval;
using Xunit;

namespace StyloFlow.Tests.Retrieval;

public class StringSimilarityTests
{
    private const double Tolerance = 0.001;

    #region ComputeCombinedSimilarity Tests

    [Fact]
    public void ComputeCombinedSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.ComputeCombinedSimilarity("hello", "hello");

        // Assert
        Assert.Equal(1.0, similarity, precision: 3);
    }

    [Fact]
    public void ComputeCombinedSimilarity_EmptyStrings_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.ComputeCombinedSimilarity("", "test");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void ComputeCombinedSimilarity_NullStrings_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.ComputeCombinedSimilarity(null!, "test");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void ComputeCombinedSimilarity_Containment_ReturnsHighScore()
    {
        // Act
        var similarity = StringSimilarity.ComputeCombinedSimilarity("test", "testing");

        // Assert
        Assert.True(similarity >= 0.9);
    }

    [Fact]
    public void ComputeCombinedSimilarity_SimilarStrings_ReturnsModerateScore()
    {
        // Act
        var similarity = StringSimilarity.ComputeCombinedSimilarity("hello world", "hello earth");

        // Assert
        Assert.True(similarity > 0.5);
        Assert.True(similarity < 1.0);
    }

    [Fact]
    public void ComputeCombinedSimilarity_DifferentStrings_ReturnsLowScore()
    {
        // Act
        var similarity = StringSimilarity.ComputeCombinedSimilarity("abc", "xyz");

        // Assert
        Assert.True(similarity < 0.5);
    }

    #endregion

    #region JaroWinklerSimilarity Tests

    [Fact]
    public void JaroWinklerSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.JaroWinklerSimilarity("MARTHA", "MARTHA");

        // Assert
        Assert.Equal(1.0, similarity, precision: 3);
    }

    [Fact]
    public void JaroWinklerSimilarity_EmptyStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.JaroWinklerSimilarity("", "");

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void JaroWinklerSimilarity_OneEmpty_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.JaroWinklerSimilarity("", "test");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void JaroWinklerSimilarity_KnownExample_ReturnsExpectedValue()
    {
        // Known example: MARTHA vs MARHTA
        var similarity = StringSimilarity.JaroWinklerSimilarity("MARTHA", "MARHTA");

        // Assert - should be around 0.961
        Assert.True(similarity > 0.9);
    }

    [Fact]
    public void JaroWinklerSimilarity_CommonPrefix_BoostsScore()
    {
        // Arrange - same Jaro base but different prefixes
        var jaro = StringSimilarity.JaroSimilarity("DWAYNE", "DUANE");
        var jaroWinkler = StringSimilarity.JaroWinklerSimilarity("DWAYNE", "DUANE");

        // Assert - Winkler boost for common prefix 'D'
        Assert.True(jaroWinkler >= jaro);
    }

    [Fact]
    public void JaroWinklerSimilarity_CaseInsensitive()
    {
        // Act
        var similarity1 = StringSimilarity.JaroWinklerSimilarity("Hello", "hello");
        var similarity2 = StringSimilarity.JaroWinklerSimilarity("hello", "hello");

        // Assert
        Assert.Equal(similarity1, similarity2, precision: 3);
    }

    #endregion

    #region JaroSimilarity Tests

    [Fact]
    public void JaroSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.JaroSimilarity("test", "test");

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void JaroSimilarity_NoMatches_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.JaroSimilarity("abc", "xyz");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void JaroSimilarity_EmptyStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.JaroSimilarity("", "");

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void JaroSimilarity_OneEmpty_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.JaroSimilarity("test", "");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    #endregion

    #region NormalizedLevenshteinSimilarity Tests

    [Fact]
    public void NormalizedLevenshteinSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.NormalizedLevenshteinSimilarity("hello", "hello");

        // Assert
        Assert.Equal(1.0, similarity, precision: 3);
    }

    [Fact]
    public void NormalizedLevenshteinSimilarity_CompletelyDifferent_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.NormalizedLevenshteinSimilarity("abc", "xyz");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void NormalizedLevenshteinSimilarity_OneEdit_ReturnsHighScore()
    {
        // Act
        var similarity = StringSimilarity.NormalizedLevenshteinSimilarity("hello", "hallo");

        // Assert - one substitution in 5 chars = 0.8
        Assert.Equal(0.8, similarity, precision: 3);
    }

    [Fact]
    public void NormalizedLevenshteinSimilarity_BothEmpty_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.NormalizedLevenshteinSimilarity("", "");

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void NormalizedLevenshteinSimilarity_OneEmpty_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.NormalizedLevenshteinSimilarity("test", "");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    #endregion

    #region LevenshteinDistance Tests

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        // Act
        var distance = StringSimilarity.LevenshteinDistance("hello", "hello");

        // Assert
        Assert.Equal(0, distance);
    }

    [Fact]
    public void LevenshteinDistance_OneInsertion_ReturnsOne()
    {
        // Act
        var distance = StringSimilarity.LevenshteinDistance("cat", "cats");

        // Assert
        Assert.Equal(1, distance);
    }

    [Fact]
    public void LevenshteinDistance_OneDeletion_ReturnsOne()
    {
        // Act
        var distance = StringSimilarity.LevenshteinDistance("cats", "cat");

        // Assert
        Assert.Equal(1, distance);
    }

    [Fact]
    public void LevenshteinDistance_OneSubstitution_ReturnsOne()
    {
        // Act
        var distance = StringSimilarity.LevenshteinDistance("cat", "bat");

        // Assert
        Assert.Equal(1, distance);
    }

    [Fact]
    public void LevenshteinDistance_KnownExample_ReturnsCorrectValue()
    {
        // kitten -> sitten (substitution) -> sittin (substitution) -> sitting (insertion)
        var distance = StringSimilarity.LevenshteinDistance("kitten", "sitting");

        // Assert
        Assert.Equal(3, distance);
    }

    [Fact]
    public void LevenshteinDistance_EmptyToNonEmpty_ReturnsLength()
    {
        // Act
        var distance = StringSimilarity.LevenshteinDistance("", "test");

        // Assert
        Assert.Equal(4, distance);
    }

    [Fact]
    public void LevenshteinDistance_CaseInsensitive()
    {
        // Act
        var distance = StringSimilarity.LevenshteinDistance("Hello", "HELLO");

        // Assert
        Assert.Equal(0, distance);
    }

    #endregion

    #region CosineNGramSimilarity Tests

    [Fact]
    public void CosineNGramSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.CosineNGramSimilarity("hello", "hello");

        // Assert
        Assert.Equal(1.0, similarity, precision: 3);
    }

    [Fact]
    public void CosineNGramSimilarity_EmptyStrings_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.CosineNGramSimilarity("", "test");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineNGramSimilarity_NoCommonNGrams_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.CosineNGramSimilarity("abc", "xyz");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void CosineNGramSimilarity_SomeCommonNGrams_ReturnsModerateScore()
    {
        // Act
        var similarity = StringSimilarity.CosineNGramSimilarity("hello world", "hello earth");

        // Assert
        Assert.True(similarity > 0);
        Assert.True(similarity < 1);
    }

    [Fact]
    public void CosineNGramSimilarity_ShortStrings_HandlesGracefully()
    {
        // Act
        var similarity = StringSimilarity.CosineNGramSimilarity("a", "a");

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void CosineNGramSimilarity_DifferentNGramSize_Works()
    {
        // Act
        var sim2 = StringSimilarity.CosineNGramSimilarity("hello", "hella", ngramSize: 2);
        var sim3 = StringSimilarity.CosineNGramSimilarity("hello", "hella", ngramSize: 3);

        // Assert - both should be positive
        Assert.True(sim2 > 0);
        Assert.True(sim3 > 0);
    }

    #endregion

    #region JaccardSimilarity Tests

    [Fact]
    public void JaccardSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.JaccardSimilarity("hello", "hello");

        // Assert
        Assert.Equal(1.0, similarity, precision: 3);
    }

    [Fact]
    public void JaccardSimilarity_BothEmpty_ReturnsOne()
    {
        // Act
        var similarity = StringSimilarity.JaccardSimilarity("", "");

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void JaccardSimilarity_OneEmpty_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.JaccardSimilarity("test", "");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void JaccardSimilarity_NoOverlap_ReturnsZero()
    {
        // Act
        var similarity = StringSimilarity.JaccardSimilarity("abc", "xyz");

        // Assert
        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void JaccardSimilarity_PartialOverlap_ReturnsCorrectValue()
    {
        // Act
        var similarity = StringSimilarity.JaccardSimilarity("abc", "bcd");

        // Assert - ngrams: abc has {ab, bc}, bcd has {bc, cd}
        // intersection = {bc}, union = {ab, bc, cd}
        // Jaccard = 1/3
        Assert.True(similarity > 0.3);
        Assert.True(similarity < 0.4);
    }

    #endregion

    #region ExtractNGrams Tests

    [Fact]
    public void ExtractNGrams_ReturnsCorrectNGrams()
    {
        // Act
        var ngrams = StringSimilarity.ExtractNGrams("hello", 2);

        // Assert
        Assert.Contains("he", ngrams);
        Assert.Contains("el", ngrams);
        Assert.Contains("ll", ngrams);
        Assert.Contains("lo", ngrams);
    }

    [Fact]
    public void ExtractNGrams_ShortString_ReturnsEmpty()
    {
        // Act
        var ngrams = StringSimilarity.ExtractNGrams("a", 2);

        // Assert
        Assert.Empty(ngrams);
    }

    [Fact]
    public void ExtractNGrams_CaseInsensitive()
    {
        // Act
        var ngrams1 = StringSimilarity.ExtractNGrams("Hello", 2);
        var ngrams2 = StringSimilarity.ExtractNGrams("hello", 2);

        // Assert
        Assert.True(ngrams1.SetEquals(ngrams2));
    }

    [Fact]
    public void ExtractNGrams_Trigrams_Works()
    {
        // Act
        var ngrams = StringSimilarity.ExtractNGrams("hello", 3);

        // Assert
        Assert.Contains("hel", ngrams);
        Assert.Contains("ell", ngrams);
        Assert.Contains("llo", ngrams);
    }

    #endregion

    #region NormalizeForComparison Tests

    [Fact]
    public void NormalizeForComparison_LowercasesText()
    {
        // Act
        var result = StringSimilarity.NormalizeForComparison("Hello World");

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void NormalizeForComparison_RemovesPunctuation()
    {
        // Act
        var result = StringSimilarity.NormalizeForComparison("Hello, World!");

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void NormalizeForComparison_CollapsesWhitespace()
    {
        // Act
        var result = StringSimilarity.NormalizeForComparison("Hello   World");

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void NormalizeForComparison_EmptyString_ReturnsEmpty()
    {
        // Act
        var result = StringSimilarity.NormalizeForComparison("");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeForComparison_NullString_ReturnsEmpty()
    {
        // Act
        var result = StringSimilarity.NormalizeForComparison(null!);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void NormalizeForComparison_KeepsAlphanumeric()
    {
        // Act
        var result = StringSimilarity.NormalizeForComparison("Test123");

        // Assert
        Assert.Equal("test123", result);
    }

    #endregion
}
