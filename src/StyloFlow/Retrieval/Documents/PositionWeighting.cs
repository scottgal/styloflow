namespace StyloFlow.Retrieval.Documents;

/// <summary>
/// Position-based weighting for document chunks.
/// Introduction and conclusion sections typically contain more important content.
/// </summary>
public static class PositionWeighting
{
    /// <summary>
    /// Apply position weights to chunks based on content type.
    /// </summary>
    public static void ApplyWeights(
        IList<TextChunk> chunks,
        ContentType contentType = ContentType.Unknown)
    {
        if (chunks.Count == 0) return;

        var introThreshold = GetIntroThreshold(contentType);
        var conclusionThreshold = GetConclusionThreshold(contentType);

        for (var i = 0; i < chunks.Count; i++)
        {
            var position = (double)i / chunks.Count;
            var chunk = chunks[i];

            double multiplier;
            if (position < introThreshold)
                multiplier = GetWeight(ChunkPosition.Introduction, contentType);
            else if (position >= conclusionThreshold)
                multiplier = GetWeight(ChunkPosition.Conclusion, contentType);
            else
                multiplier = GetWeight(ChunkPosition.Body, contentType);

            chunk.PositionWeight *= multiplier;
        }
    }

    /// <summary>
    /// Get position weight for a specific position and content type.
    /// </summary>
    public static double GetWeight(ChunkPosition position, ContentType contentType)
    {
        return (position, contentType) switch
        {
            // Expository (technical, academic) - intro/conclusion are summary-like
            (ChunkPosition.Introduction, ContentType.Expository) => 1.5,
            (ChunkPosition.Conclusion, ContentType.Expository) => 1.4,
            (ChunkPosition.Body, ContentType.Expository) => 1.0,

            // Narrative (fiction) - position matters less, climax often in middle
            (ChunkPosition.Introduction, ContentType.Narrative) => 1.1,
            (ChunkPosition.Conclusion, ContentType.Narrative) => 1.2,
            (ChunkPosition.Body, ContentType.Narrative) => 1.0,

            // Unknown - moderate position weighting
            (ChunkPosition.Introduction, _) => 1.3,
            (ChunkPosition.Conclusion, _) => 1.2,
            _ => 1.0
        };
    }

    /// <summary>
    /// Get the threshold (0-1) marking end of introduction section.
    /// </summary>
    public static double GetIntroThreshold(ContentType contentType) => contentType switch
    {
        ContentType.Expository => 0.15, // First 15% is intro for technical docs
        ContentType.Narrative => 0.10,  // First 10% for fiction
        _ => 0.12
    };

    /// <summary>
    /// Get the threshold (0-1) marking start of conclusion section.
    /// </summary>
    public static double GetConclusionThreshold(ContentType contentType) => contentType switch
    {
        ContentType.Expository => 0.85, // Last 15% is conclusion
        ContentType.Narrative => 0.90,  // Last 10% for fiction
        _ => 0.88
    };
}

/// <summary>
/// Position within a document.
/// </summary>
public enum ChunkPosition
{
    Introduction,
    Body,
    Conclusion
}

/// <summary>
/// Content type for adaptive processing.
/// </summary>
public enum ContentType
{
    /// <summary>Unknown content type.</summary>
    Unknown,
    /// <summary>Technical, academic, informational content.</summary>
    Expository,
    /// <summary>Fiction, stories, narrative content.</summary>
    Narrative
}

/// <summary>
/// Automatic content type detection from text.
/// </summary>
public static class ContentTypeDetector
{
    /// <summary>
    /// Detect content type from text using heuristics.
    /// </summary>
    public static ContentType Detect(string text)
    {
        if (string.IsNullOrEmpty(text)) return ContentType.Unknown;

        var lower = text.ToLowerInvariant();
        var fictionScore = 0.0;
        var technicalScore = 0.0;

        // Fiction signals
        if (CountPattern(lower, @"\b(said|replied|asked|whispered|shouted)\b") > 3)
            fictionScore += 3;
        if (CountPattern(lower, @"\b(he|she|they)\s+(walked|ran|looked|felt|thought)\b") > 2)
            fictionScore += 3;
        if (lower.Contains("chapter")) fictionScore += 2;
        if (CountPattern(text, @"""[^""]+""") > 5) fictionScore += 2; // Dialogue

        // Technical signals
        if (CountPattern(lower, @"\b(function|class|method|api|http|json)\b") > 2)
            technicalScore += 3;
        if (CountPattern(lower, @"\b(install|configure|docker|kubernetes|database)\b") > 1)
            technicalScore += 2;
        if (CountPattern(text, @"```[\s\S]*?```") > 0) technicalScore += 3; // Code blocks
        if (lower.Contains("introduction") || lower.Contains("conclusion"))
            technicalScore += 1;

        if (fictionScore > technicalScore + 3) return ContentType.Narrative;
        if (technicalScore > fictionScore + 3) return ContentType.Expository;

        return ContentType.Unknown;
    }

    private static int CountPattern(string text, string pattern)
    {
        return System.Text.RegularExpressions.Regex.Matches(text, pattern).Count;
    }
}
