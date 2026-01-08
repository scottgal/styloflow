using StyloFlow.Retrieval.Data;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Data;

public class PatternDetectionTests
{
    #region Text Pattern Detection Tests

    [Fact]
    public void DetectTextPatterns_EmptyValues_ReturnsEmpty()
    {
        // Arrange
        var values = Array.Empty<string?>();

        // Act
        var patterns = PatternDetection.DetectTextPatterns(values);

        // Assert
        Assert.Empty(patterns);
    }

    [Fact]
    public void DetectTextPatterns_AllNull_ReturnsEmpty()
    {
        // Arrange
        var values = new string?[] { null, null, null };

        // Act
        var patterns = PatternDetection.DetectTextPatterns(values);

        // Assert
        Assert.Empty(patterns);
    }

    [Fact]
    public void DetectTextPatterns_Emails_DetectsEmailPattern()
    {
        // Arrange
        var values = new[]
        {
            "test@example.com",
            "user@domain.org",
            "admin@company.net",
            "other@site.io"
        };

        // Act
        var patterns = PatternDetection.DetectTextPatterns(values, minMatchPercent: 0.5);

        // Assert
        Assert.Contains(patterns, p => p.PatternType == PatternDetection.TextPatternType.Email);
    }

    [Fact]
    public void DetectTextPatterns_Urls_DetectsUrlPattern()
    {
        // Arrange
        var values = new[]
        {
            "https://example.com",
            "http://test.org/path",
            "https://domain.net/api/v1"
        };

        // Act
        var patterns = PatternDetection.DetectTextPatterns(values, minMatchPercent: 0.5);

        // Assert
        Assert.Contains(patterns, p => p.PatternType == PatternDetection.TextPatternType.Url);
    }

    [Fact]
    public void DetectTextPatterns_UUIDs_DetectsUuidPattern()
    {
        // Arrange
        var values = new[]
        {
            "123e4567-e89b-12d3-a456-426614174000",
            "550e8400-e29b-41d4-a716-446655440000",
            "6ba7b810-9dad-11d1-80b4-00c04fd430c8"
        };

        // Act
        var patterns = PatternDetection.DetectTextPatterns(values, minMatchPercent: 0.5);

        // Assert
        Assert.Contains(patterns, p => p.PatternType == PatternDetection.TextPatternType.Uuid);
    }

    [Fact]
    public void DetectTextPatterns_IpAddresses_DetectsIpPattern()
    {
        // Arrange
        var values = new[]
        {
            "192.168.1.1",
            "10.0.0.1",
            "172.16.0.1",
            "8.8.8.8"
        };

        // Act
        var patterns = PatternDetection.DetectTextPatterns(values, minMatchPercent: 0.5);

        // Assert
        Assert.Contains(patterns, p => p.PatternType == PatternDetection.TextPatternType.IpAddress);
    }

    [Fact]
    public void DetectTextPatterns_IncludesExamples()
    {
        // Arrange
        var values = new[]
        {
            "test@example.com",
            "user@domain.org",
            "admin@company.net"
        };

        // Act
        var patterns = PatternDetection.DetectTextPatterns(values, minMatchPercent: 0.5);

        // Assert
        var emailPattern = patterns.FirstOrDefault(p => p.PatternType == PatternDetection.TextPatternType.Email);
        Assert.NotNull(emailPattern);
        Assert.NotEmpty(emailPattern.Examples);
    }

    [Fact]
    public void DetectNovelPattern_NoPattern_ReturnsNull()
    {
        // Arrange
        var values = new[] { "abc", "xyz", "123", "!!!", "---" };

        // Act
        var pattern = PatternDetection.DetectNovelPattern(values);

        // Assert
        Assert.Null(pattern);
    }

    [Fact]
    public void DetectNovelPattern_ConsistentFormat_ReturnsPattern()
    {
        // Arrange
        var values = Enumerable.Range(0, 100)
            .Select(i => $"ID{i:D4}") // e.g., ID0001, ID0002, etc.
            .ToArray();

        // Act
        var pattern = PatternDetection.DetectNovelPattern(values);

        // Assert
        Assert.NotNull(pattern);
        Assert.Equal(PatternDetection.TextPatternType.Novel, pattern.PatternType);
        Assert.NotNull(pattern.DetectedRegex);
    }

    [Fact]
    public void DetectNovelPattern_TooFewValues_ReturnsNull()
    {
        // Arrange
        var values = new[] { "ABC123", "DEF456" }; // Less than 10

        // Act
        var pattern = PatternDetection.DetectNovelPattern(values);

        // Assert
        Assert.Null(pattern);
    }

    #endregion

    #region Distribution Classification Tests

    [Fact]
    public void ClassifyDistribution_Normal_ReturnsNormal()
    {
        // Act
        var result = PatternDetection.ClassifyDistribution(skewness: 0.1, kurtosis: 3.2);

        // Assert
        Assert.Equal(PatternDetection.DistributionType.Normal, result);
    }

    [Fact]
    public void ClassifyDistribution_RightSkewed_ReturnsRightSkewed()
    {
        // Act
        var result = PatternDetection.ClassifyDistribution(skewness: 2.0, kurtosis: 5.0);

        // Assert
        Assert.Equal(PatternDetection.DistributionType.RightSkewed, result);
    }

    [Fact]
    public void ClassifyDistribution_LeftSkewed_ReturnsLeftSkewed()
    {
        // Act
        var result = PatternDetection.ClassifyDistribution(skewness: -2.0, kurtosis: 5.0);

        // Assert
        Assert.Equal(PatternDetection.DistributionType.LeftSkewed, result);
    }

    [Fact]
    public void ClassifyDistribution_Uniform_ReturnsUniform()
    {
        // Act
        var result = PatternDetection.ClassifyDistribution(skewness: 0.1, kurtosis: 1.5, iqrRatio: 0.5);

        // Assert
        Assert.Equal(PatternDetection.DistributionType.Uniform, result);
    }

    [Fact]
    public void ClassifyDistribution_PowerLaw_ReturnsPowerLaw()
    {
        // Act
        var result = PatternDetection.ClassifyDistribution(skewness: 5.0, kurtosis: 50.0);

        // Assert
        Assert.Equal(PatternDetection.DistributionType.PowerLaw, result);
    }

    [Fact]
    public void ClassifyDistribution_Exponential_ReturnsExponential()
    {
        // Act
        var result = PatternDetection.ClassifyDistribution(skewness: 1.5, kurtosis: 8.0);

        // Assert
        Assert.Equal(PatternDetection.DistributionType.Exponential, result);
    }

    [Fact]
    public void DetectBimodality_TwoPeaks_ReturnsTrue()
    {
        // Arrange - histogram with two peaks
        var binCounts = new[] { 10, 50, 20, 5, 5, 20, 60, 15 };

        // Act
        var isBimodal = PatternDetection.DetectBimodality(binCounts);

        // Assert
        Assert.True(isBimodal);
    }

    [Fact]
    public void DetectBimodality_OnePeak_ReturnsFalse()
    {
        // Arrange - histogram with one peak
        var binCounts = new[] { 5, 10, 30, 50, 30, 10, 5 };

        // Act
        var isBimodal = PatternDetection.DetectBimodality(binCounts);

        // Assert
        Assert.False(isBimodal);
    }

    [Fact]
    public void DetectBimodality_TooFewBins_ReturnsFalse()
    {
        // Arrange
        var binCounts = new[] { 10, 20 };

        // Act
        var isBimodal = PatternDetection.DetectBimodality(binCounts);

        // Assert
        Assert.False(isBimodal);
    }

    #endregion

    #region Trend Detection Tests

    [Fact]
    public void DetectTrend_TooFewValues_ReturnsNull()
    {
        // Arrange
        var values = new[] { 1.0, 2.0 };

        // Act
        var trend = PatternDetection.DetectTrend(values);

        // Assert
        Assert.Null(trend);
    }

    [Fact]
    public void DetectTrend_ClearIncreasingTrend_ReturnsIncreasing()
    {
        // Arrange
        var values = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();

        // Act
        var trend = PatternDetection.DetectTrend(values, minRSquared: 0.3);

        // Assert
        Assert.NotNull(trend);
        Assert.Equal(PatternDetection.TrendDirection.Increasing, trend.Direction);
        Assert.True(trend.Slope > 0);
    }

    [Fact]
    public void DetectTrend_ClearDecreasingTrend_ReturnsDecreasing()
    {
        // Arrange
        var values = Enumerable.Range(1, 20).Select(i => 100.0 - i).ToArray();

        // Act
        var trend = PatternDetection.DetectTrend(values, minRSquared: 0.3);

        // Assert
        Assert.NotNull(trend);
        Assert.Equal(PatternDetection.TrendDirection.Decreasing, trend.Direction);
        Assert.True(trend.Slope < 0);
    }

    [Fact]
    public void DetectTrend_NoTrend_ReturnsNoneOrNull()
    {
        // Arrange - constant values (zero slope) with no trend
        var values = new[] { 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0, 5.0 };

        // Act
        var trend = PatternDetection.DetectTrend(values, minRSquared: 0.8);

        // Assert - constant values have zero slope, so direction is None
        Assert.True(trend == null || trend.Direction == PatternDetection.TrendDirection.None);
    }

    [Fact]
    public void DetectTrend_RSquaredInValidRange()
    {
        // Arrange
        var values = Enumerable.Range(1, 20).Select(i => (double)i + (i % 3)).ToArray();

        // Act
        var trend = PatternDetection.DetectTrend(values);

        // Assert
        if (trend != null)
        {
            Assert.True(trend.RSquared >= 0);
            Assert.True(trend.RSquared <= 1);
        }
    }

    #endregion

    #region Periodicity Detection Tests

    [Fact]
    public void DetectPeriodicity_TooShortSequence_ReturnsNull()
    {
        // Arrange
        var values = new[] { 1.0, 2.0, 1.0, 2.0 };

        // Act
        var periodicity = PatternDetection.DetectPeriodicity(values, maxLag: 50);

        // Assert
        Assert.Null(periodicity);
    }

    [Fact]
    public void DetectPeriodicity_PeriodicSignal_DetectsPeriod()
    {
        // Arrange - sine wave with period 10
        var values = Enumerable.Range(0, 200)
            .Select(i => Math.Sin(2 * Math.PI * i / 10.0))
            .ToArray();

        // Act
        var periodicity = PatternDetection.DetectPeriodicity(values, maxLag: 50, minConfidence: 0.1);

        // Assert
        Assert.NotNull(periodicity);
        Assert.True(periodicity.HasPeriodicity);
        Assert.True(Math.Abs(periodicity.DominantPeriod - 10) <= 2); // Allow some tolerance
    }

    [Fact]
    public void DetectPeriodicity_RandomSignal_ReturnsNull()
    {
        // Arrange
        var rng = new Random(42);
        var values = Enumerable.Range(0, 200)
            .Select(_ => rng.NextDouble())
            .ToArray();

        // Act
        var periodicity = PatternDetection.DetectPeriodicity(values, maxLag: 50, minConfidence: 0.5);

        // Assert
        Assert.Null(periodicity);
    }

    [Fact]
    public void DetectPeriodicity_ConstantSignal_ReturnsNull()
    {
        // Arrange
        var values = Enumerable.Repeat(1.0, 200).ToArray();

        // Act
        var periodicity = PatternDetection.DetectPeriodicity(values);

        // Assert
        Assert.Null(periodicity);
    }

    #endregion
}

public class TextPatternMatchTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var match = new TextPatternMatch();

        // Assert
        Assert.Equal(PatternDetection.TextPatternType.Unknown, match.PatternType);
        Assert.Equal(0, match.MatchCount);
        Assert.Equal(0.0, match.MatchPercent);
        Assert.Empty(match.Examples);
    }
}

public class TrendInfoTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var trend = new TrendInfo();

        // Assert
        Assert.Equal(PatternDetection.TrendDirection.None, trend.Direction);
        Assert.Equal(0.0, trend.Slope);
        Assert.Equal(0.0, trend.RSquared);
    }
}
