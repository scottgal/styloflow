using StyloFlow.Retrieval.Video;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Video;

public class VideoFingerprintTests
{
    [Fact]
    public void ComputeFromFrameHashes_EmptyList_ReturnsEmpty()
    {
        // Arrange
        var hashes = Array.Empty<string>();

        // Act
        var fingerprint = VideoFingerprint.ComputeFromFrameHashes(hashes);

        // Assert
        Assert.Empty(fingerprint);
    }

    [Fact]
    public void ComputeFromFrameHashes_SingleHash_ReturnsHash()
    {
        // Arrange
        var hashes = new[] { "ABCD1234" };

        // Act
        var fingerprint = VideoFingerprint.ComputeFromFrameHashes(hashes);

        // Assert
        Assert.Equal("ABCD1234", fingerprint);
    }

    [Fact]
    public void ComputeFromFrameHashes_MultipleHashes_CombinesWithXor()
    {
        // Arrange
        var hashes = new[] { "00000000", "FFFFFFFF" };

        // Act
        var fingerprint = VideoFingerprint.ComputeFromFrameHashes(hashes);

        // Assert - XOR of 0 and F is F
        Assert.Equal("FFFFFFFF", fingerprint);
    }

    [Fact]
    public void ComputeFromFrameHashes_IdenticalHashes_ReturnsZeros()
    {
        // Arrange
        var hashes = new[] { "ABCD1234", "ABCD1234" };

        // Act
        var fingerprint = VideoFingerprint.ComputeFromFrameHashes(hashes);

        // Assert - XOR of same values is 0
        Assert.Equal("00000000", fingerprint);
    }

    [Fact]
    public void FingerprintSimilarity_IdenticalHashes_ReturnsOne()
    {
        // Arrange
        var hash = "ABCD1234";

        // Act
        var similarity = VideoFingerprint.FingerprintSimilarity(hash, hash);

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void FingerprintSimilarity_EmptyHash_ReturnsZero()
    {
        // Act
        var similarity1 = VideoFingerprint.FingerprintSimilarity("", "ABCD");
        var similarity2 = VideoFingerprint.FingerprintSimilarity("ABCD", "");

        // Assert
        Assert.Equal(0.0, similarity1);
        Assert.Equal(0.0, similarity2);
    }

    [Fact]
    public void FingerprintSimilarity_CompletelyDifferent_ReturnsZero()
    {
        // Arrange - 0 XOR F = F, all bits different
        var hash1 = "00000000";
        var hash2 = "FFFFFFFF";

        // Act
        var similarity = VideoFingerprint.FingerprintSimilarity(hash1, hash2);

        // Assert
        Assert.Equal(0.0, similarity);
    }
}

public class KeyframeExtractorTests
{
    [Fact]
    public void UniformSample_ZeroFrames_ReturnsEmpty()
    {
        // Act
        var indices = KeyframeExtractor.UniformSample(0, 5);

        // Assert
        Assert.Empty(indices);
    }

    [Fact]
    public void UniformSample_ZeroKeyframes_ReturnsEmpty()
    {
        // Act
        var indices = KeyframeExtractor.UniformSample(100, 0);

        // Assert
        Assert.Empty(indices);
    }

    [Fact]
    public void UniformSample_MoreKeyframesThanFrames_ReturnsAllFrames()
    {
        // Act
        var indices = KeyframeExtractor.UniformSample(5, 10);

        // Assert
        Assert.Equal(5, indices.Length);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, indices);
    }

    [Fact]
    public void UniformSample_FewerKeyframesThanFrames_ReturnsUniformDistribution()
    {
        // Act
        var indices = KeyframeExtractor.UniformSample(100, 5);

        // Assert
        Assert.Equal(5, indices.Length);
        Assert.Equal(0, indices[0]); // First frame
        Assert.True(indices[4] >= 80); // Last keyframe near end
    }

    [Fact]
    public void UniformSample_IndicesAreDistinct()
    {
        // Act
        var indices = KeyframeExtractor.UniformSample(100, 10);

        // Assert
        Assert.Equal(indices.Length, indices.Distinct().Count());
    }

    [Fact]
    public void SelectByNovelty_EmptyDifferences_ReturnsFirstFrame()
    {
        // Arrange
        var differences = Array.Empty<double>();

        // Act
        var indices = KeyframeExtractor.SelectByNovelty(differences, 5);

        // Assert
        Assert.Single(indices);
        Assert.Equal(0, indices[0]);
    }

    [Fact]
    public void SelectByNovelty_NoNovelFrames_ReturnsFirstAndLast()
    {
        // Arrange
        var differences = new[] { 0.01, 0.01, 0.01, 0.01 };

        // Act
        var indices = KeyframeExtractor.SelectByNovelty(differences, 10, noveltyThreshold: 0.1);

        // Assert
        Assert.Contains(0, indices);
        Assert.Contains(differences.Length, indices);
    }

    [Fact]
    public void SelectByNovelty_HighNoveltyFrames_ReturnsThose()
    {
        // Arrange
        var differences = new[] { 0.01, 0.5, 0.01, 0.8, 0.01 };

        // Act
        var indices = KeyframeExtractor.SelectByNovelty(differences, 10, noveltyThreshold: 0.1);

        // Assert
        Assert.Contains(1, indices); // High novelty at index 1
        Assert.Contains(3, indices); // High novelty at index 3
    }

    [Fact]
    public void SelectByNovelty_RespectsMaxKeyframes()
    {
        // Arrange
        var differences = Enumerable.Repeat(0.5, 100).ToArray();

        // Act
        var indices = KeyframeExtractor.SelectByNovelty(differences, maxKeyframes: 5, noveltyThreshold: 0.1);

        // Assert
        Assert.True(indices.Length <= 5);
    }
}

public class SceneDetectorTests
{
    [Fact]
    public void DetectSceneBoundaries_EmptyDifferences_ReturnsEmpty()
    {
        // Arrange
        var differences = Array.Empty<double>();

        // Act
        var boundaries = SceneDetector.DetectSceneBoundaries(differences);

        // Assert
        Assert.Empty(boundaries);
    }

    [Fact]
    public void DetectSceneBoundaries_NoBoundaries_ReturnsEmpty()
    {
        // Arrange
        var differences = new[] { 0.1, 0.1, 0.1, 0.1 };

        // Act
        var boundaries = SceneDetector.DetectSceneBoundaries(differences, threshold: 0.3);

        // Assert
        Assert.Empty(boundaries);
    }

    [Fact]
    public void DetectSceneBoundaries_ClearBoundaries_ReturnsIndices()
    {
        // Arrange
        var differences = new[] { 0.1, 0.5, 0.1, 0.8, 0.1 };

        // Act
        var boundaries = SceneDetector.DetectSceneBoundaries(differences, threshold: 0.3);

        // Assert
        Assert.Contains(1, boundaries);
        Assert.Contains(3, boundaries);
    }

    [Fact]
    public void DetectScenesAdaptive_ShortSequence_ReturnsEmpty()
    {
        // Arrange
        var differences = new[] { 0.1, 0.5, 0.1 };

        // Act
        var boundaries = SceneDetector.DetectScenesAdaptive(differences, windowSize: 30);

        // Assert
        Assert.Empty(boundaries);
    }

    [Fact]
    public void DetectScenesAdaptive_LongSequence_DetectsBoundaries()
    {
        // Arrange
        var differences = new double[100];
        Array.Fill(differences, 0.1);
        differences[50] = 0.9; // Clear scene change

        // Act
        var boundaries = SceneDetector.DetectScenesAdaptive(differences, windowSize: 10, sensitivity: 2.0);

        // Assert
        Assert.Contains(50, boundaries);
    }
}

public class MotionAnalyzerTests
{
    [Fact]
    public void ClassifyMotion_VeryLow_ReturnsStatic()
    {
        // Act
        var level = MotionAnalyzer.ClassifyMotion(0.01);

        // Assert
        Assert.Equal(MotionLevel.Static, level);
    }

    [Fact]
    public void ClassifyMotion_Low_ReturnsLow()
    {
        // Act
        var level = MotionAnalyzer.ClassifyMotion(0.05);

        // Assert
        Assert.Equal(MotionLevel.Low, level);
    }

    [Fact]
    public void ClassifyMotion_Medium_ReturnsMedium()
    {
        // Act
        var level = MotionAnalyzer.ClassifyMotion(0.2);

        // Assert
        Assert.Equal(MotionLevel.Medium, level);
    }

    [Fact]
    public void ClassifyMotion_High_ReturnsHigh()
    {
        // Act
        var level = MotionAnalyzer.ClassifyMotion(0.5);

        // Assert
        Assert.Equal(MotionLevel.High, level);
    }

    [Fact]
    public void IsLikelyStatic_VeryLowAverage_ReturnsTrue()
    {
        // Arrange
        var differences = new[] { 0.001, 0.002, 0.001 };

        // Act
        var isStatic = MotionAnalyzer.IsLikelyStatic(differences);

        // Assert
        Assert.True(isStatic);
    }

    [Fact]
    public void IsLikelyStatic_HighAverage_ReturnsFalse()
    {
        // Arrange
        var differences = new[] { 0.1, 0.2, 0.3 };

        // Act
        var isStatic = MotionAnalyzer.IsLikelyStatic(differences);

        // Assert
        Assert.False(isStatic);
    }

    [Fact]
    public void IsLikelyStatic_EmptyDifferences_ReturnsTrue()
    {
        // Arrange
        var differences = Array.Empty<double>();

        // Act
        var isStatic = MotionAnalyzer.IsLikelyStatic(differences);

        // Assert
        Assert.True(isStatic);
    }
}
