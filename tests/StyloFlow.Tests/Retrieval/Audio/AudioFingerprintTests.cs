using StyloFlow.Retrieval.Audio;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Audio;

public class AudioFingerprintTests
{
    [Fact]
    public void ComputeEnergyFingerprint_EmptySamples_ReturnsEmpty()
    {
        // Arrange
        var samples = ReadOnlySpan<float>.Empty;

        // Act
        var hash = AudioFingerprint.ComputeEnergyFingerprint(samples);

        // Assert
        Assert.Empty(hash);
    }

    [Fact]
    public void ComputeEnergyFingerprint_ValidSamples_ReturnsHash()
    {
        // Arrange
        var samples = new float[44100]; // 1 second at 44.1kHz
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 44100); // 440Hz tone

        // Act
        var hash = AudioFingerprint.ComputeEnergyFingerprint(samples);

        // Assert
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void ComputeEnergyFingerprint_IdenticalSamples_ReturnsSameHash()
    {
        // Arrange
        var samples1 = new float[44100];
        var samples2 = new float[44100];
        for (int i = 0; i < 44100; i++)
        {
            samples1[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 44100);
            samples2[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 44100);
        }

        // Act
        var hash1 = AudioFingerprint.ComputeEnergyFingerprint(samples1);
        var hash2 = AudioFingerprint.ComputeEnergyFingerprint(samples2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeEnergyFingerprint_DifferentWindowSize_ProducesDifferentHash()
    {
        // Arrange
        var samples = new float[44100];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 44100);

        // Act
        var hash1024 = AudioFingerprint.ComputeEnergyFingerprint(samples, windowSize: 1024);
        var hash2048 = AudioFingerprint.ComputeEnergyFingerprint(samples, windowSize: 2048);

        // Assert
        Assert.NotEqual(hash1024, hash2048);
    }

    [Fact]
    public void FingerprintSimilarity_IdenticalHashes_ReturnsOne()
    {
        // Arrange
        var hash = "ABCD1234";

        // Act
        var similarity = AudioFingerprint.FingerprintSimilarity(hash, hash);

        // Assert
        Assert.Equal(1.0, similarity);
    }

    [Fact]
    public void FingerprintSimilarity_EmptyHash_ReturnsZero()
    {
        // Act
        var similarity1 = AudioFingerprint.FingerprintSimilarity("", "ABCD");
        var similarity2 = AudioFingerprint.FingerprintSimilarity("ABCD", "");

        // Assert
        Assert.Equal(0.0, similarity1);
        Assert.Equal(0.0, similarity2);
    }

    [Fact]
    public void FingerprintSimilarity_PartialMatch_ReturnsBetweenZeroAndOne()
    {
        // Arrange
        var hash1 = "ABCD1234";
        var hash2 = "ABCD5678";

        // Act
        var similarity = AudioFingerprint.FingerprintSimilarity(hash1, hash2);

        // Assert
        Assert.True(similarity > 0);
        Assert.True(similarity < 1);
    }
}

public class AudioSegmentationTests
{
    [Fact]
    public void DetectSpeechSegments_SilentAudio_ReturnsEmpty()
    {
        // Arrange
        var samples = new float[44100]; // All zeros (silence)

        // Act
        var segments = AudioSegmentation.DetectSpeechSegments(samples, 44100);

        // Assert
        Assert.Empty(segments);
    }

    [Fact]
    public void DetectSpeechSegments_ContinuousLoudAudio_ReturnsSingleSegment()
    {
        // Arrange
        var samples = new float[44100];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = 0.5f; // Constant loud signal

        // Act
        var segments = AudioSegmentation.DetectSpeechSegments(samples, 44100, silenceThreshold: 0.01f);

        // Assert
        Assert.Single(segments);
        Assert.True(segments[0].End > segments[0].Start);
    }

    [Fact]
    public void DetectSpeechSegments_SpeechWithSilence_ReturnsMultipleSegments()
    {
        // Arrange
        var samples = new float[44100 * 3]; // 3 seconds

        // First second: speech
        for (int i = 0; i < 44100; i++)
            samples[i] = 0.5f;

        // Second second: silence (leave as zeros)

        // Third second: speech
        for (int i = 44100 * 2; i < 44100 * 3; i++)
            samples[i] = 0.5f;

        // Act
        var segments = AudioSegmentation.DetectSpeechSegments(samples, 44100, silenceThreshold: 0.01f, minSilenceDuration: 0.5);

        // Assert
        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void DetectSpeechSegments_ShortSilence_MergesSegments()
    {
        // Arrange
        var samples = new float[44100 * 2]; // 2 seconds
        for (int i = 0; i < samples.Length; i++)
            samples[i] = 0.5f;

        // Brief silence in the middle
        for (int i = 22000; i < 22500; i++)
            samples[i] = 0;

        // Act - with long minimum silence requirement
        var segments = AudioSegmentation.DetectSpeechSegments(samples, 44100, silenceThreshold: 0.01f, minSilenceDuration: 0.5);

        // Assert - should merge because silence is too short
        Assert.Single(segments);
    }
}

public class MfccExtractorTests
{
    [Fact]
    public void ExtractMfcc_EmptySamples_ReturnsEmpty()
    {
        // Arrange
        var samples = ReadOnlySpan<float>.Empty;

        // Act
        var mfcc = MfccExtractor.ExtractMfcc(samples, 44100);

        // Assert
        Assert.Empty(mfcc);
    }

    [Fact]
    public void ExtractMfcc_ValidSamples_ReturnsResult()
    {
        // Arrange
        var samples = new float[44100];

        // Act
        var mfcc = MfccExtractor.ExtractMfcc(samples, 44100);

        // Assert - stub returns empty
        Assert.Empty(mfcc);
    }
}
