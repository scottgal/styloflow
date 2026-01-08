namespace StyloFlow.Retrieval.Video;

/// <summary>
/// Video fingerprinting and analysis algorithms.
///
/// Future implementations:
/// - Keyframe extraction
/// - Scene boundary detection
/// - Motion-based segmentation
/// - Video perceptual hashing
/// - Shot detection
/// </summary>
public static class VideoFingerprint
{
    /// <summary>
    /// Compute video fingerprint from a sequence of frame hashes.
    /// Combines temporal information with frame-level perceptual hashes.
    /// </summary>
    /// <param name="frameHashes">Perceptual hashes of sampled frames.</param>
    /// <returns>Combined video fingerprint.</returns>
    public static string ComputeFromFrameHashes(IReadOnlyList<string> frameHashes)
    {
        if (frameHashes.Count == 0)
            return string.Empty;

        // XOR all frame hashes together for a composite fingerprint
        var result = frameHashes[0].ToCharArray();

        for (int i = 1; i < frameHashes.Count; i++)
        {
            var hash = frameHashes[i];
            for (int j = 0; j < Math.Min(result.Length, hash.Length); j++)
            {
                var a = Convert.ToInt32(result[j].ToString(), 16);
                var b = Convert.ToInt32(hash[j].ToString(), 16);
                result[j] = ((a ^ b) & 0xF).ToString("X")[0];
            }
        }

        return new string(result);
    }

    /// <summary>
    /// Calculate similarity between two video fingerprints.
    /// </summary>
    public static double FingerprintSimilarity(string hash1, string hash2)
    {
        if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
            return 0;

        var distance = 0;
        var minLen = Math.Min(hash1.Length, hash2.Length);

        for (int i = 0; i < minLen; i++)
        {
            var a = Convert.ToInt32(hash1[i].ToString(), 16);
            var b = Convert.ToInt32(hash2[i].ToString(), 16);
            distance += System.Numerics.BitOperations.PopCount((uint)(a ^ b));
        }

        var maxDistance = minLen * 4;
        return 1.0 - (double)distance / maxDistance;
    }
}

/// <summary>
/// Keyframe extraction strategies for video summarization.
/// </summary>
public static class KeyframeExtractor
{
    /// <summary>
    /// Extract keyframes using uniform sampling.
    /// Simple but effective for many use cases.
    /// </summary>
    /// <param name="totalFrames">Total number of frames in video.</param>
    /// <param name="targetKeyframes">Number of keyframes to extract.</param>
    /// <returns>Frame indices to sample.</returns>
    public static int[] UniformSample(int totalFrames, int targetKeyframes)
    {
        if (totalFrames <= 0 || targetKeyframes <= 0)
            return Array.Empty<int>();

        if (targetKeyframes >= totalFrames)
            return Enumerable.Range(0, totalFrames).ToArray();

        var step = (double)totalFrames / targetKeyframes;
        return Enumerable.Range(0, targetKeyframes)
            .Select(i => (int)(i * step))
            .ToArray();
    }

    /// <summary>
    /// Select keyframes based on novelty/difference from previous frames.
    /// Requires frame embeddings or hash differences.
    /// </summary>
    /// <param name="frameDifferences">Difference scores between consecutive frames.</param>
    /// <param name="maxKeyframes">Maximum number of keyframes.</param>
    /// <param name="noveltyThreshold">Minimum difference to consider novel (0-1).</param>
    /// <returns>Indices of selected keyframes.</returns>
    public static int[] SelectByNovelty(
        IReadOnlyList<double> frameDifferences,
        int maxKeyframes,
        double noveltyThreshold = 0.1)
    {
        var keyframes = new List<int> { 0 }; // Always include first frame

        for (int i = 1; i < frameDifferences.Count && keyframes.Count < maxKeyframes; i++)
        {
            if (frameDifferences[i] >= noveltyThreshold)
            {
                keyframes.Add(i);
            }
        }

        // Always include last frame if not already
        if (frameDifferences.Count > 0 && keyframes[^1] != frameDifferences.Count)
        {
            if (keyframes.Count < maxKeyframes)
                keyframes.Add(frameDifferences.Count);
        }

        return keyframes.ToArray();
    }
}

/// <summary>
/// Scene boundary detection for video segmentation.
/// </summary>
public static class SceneDetector
{
    /// <summary>
    /// Detect scene boundaries using frame difference threshold.
    /// </summary>
    /// <param name="frameDifferences">Difference scores between consecutive frames.</param>
    /// <param name="threshold">Threshold for scene change detection.</param>
    /// <returns>Frame indices where scene changes occur.</returns>
    public static int[] DetectSceneBoundaries(
        IReadOnlyList<double> frameDifferences,
        double threshold = 0.3)
    {
        return frameDifferences
            .Select((diff, idx) => (diff, idx))
            .Where(x => x.diff >= threshold)
            .Select(x => x.idx)
            .ToArray();
    }

    /// <summary>
    /// Detect scenes using adaptive threshold based on local statistics.
    /// More robust than fixed threshold.
    /// </summary>
    /// <param name="frameDifferences">Difference scores.</param>
    /// <param name="windowSize">Window for local statistics.</param>
    /// <param name="sensitivity">Sensitivity multiplier (higher = fewer scenes).</param>
    /// <returns>Scene boundary indices.</returns>
    public static int[] DetectScenesAdaptive(
        IReadOnlyList<double> frameDifferences,
        int windowSize = 30,
        double sensitivity = 2.0)
    {
        var boundaries = new List<int>();

        for (int i = windowSize; i < frameDifferences.Count - windowSize; i++)
        {
            // Calculate local mean and std
            var window = frameDifferences.Skip(i - windowSize).Take(windowSize * 2).ToList();
            var mean = window.Average();
            var std = Math.Sqrt(window.Average(x => (x - mean) * (x - mean)));

            var threshold = mean + sensitivity * std;

            if (frameDifferences[i] > threshold)
            {
                boundaries.Add(i);
                // Skip ahead to avoid detecting same scene change multiple times
                i += windowSize / 2;
            }
        }

        return boundaries.ToArray();
    }
}

/// <summary>
/// Motion analysis for video content.
/// </summary>
public static class MotionAnalyzer
{
    /// <summary>
    /// Classify motion level from frame differences.
    /// </summary>
    public static MotionLevel ClassifyMotion(double averageFrameDifference)
    {
        return averageFrameDifference switch
        {
            < 0.02 => MotionLevel.Static,
            < 0.1 => MotionLevel.Low,
            < 0.3 => MotionLevel.Medium,
            _ => MotionLevel.High
        };
    }

    /// <summary>
    /// Detect if video is likely a static image or slideshow.
    /// </summary>
    public static bool IsLikelyStatic(IReadOnlyList<double> frameDifferences, double threshold = 0.02)
    {
        if (frameDifferences.Count == 0) return true;
        return frameDifferences.Average() < threshold;
    }
}

/// <summary>
/// Motion intensity levels.
/// </summary>
public enum MotionLevel
{
    Static,
    Low,
    Medium,
    High
}
