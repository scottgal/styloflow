using StyloFlow.Retrieval.Analysis;

namespace StyloFlow.Retrieval.Entities;

/// <summary>
/// Universal entity for cross-modal retrieval.
/// Documents, images, audio, video all share this structure.
/// Enables unified search across all content types.
/// </summary>
public class RetrievalEntity
{
    /// <summary>
    /// Unique identifier for this entity.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Content type: document, image, audio, video.
    /// </summary>
    public required ContentType ContentType { get; init; }

    /// <summary>
    /// Source file path or URL.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// SHA256 hash for deduplication.
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// When this entity was created/indexed.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this entity was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Collection/folder this entity belongs to.
    /// </summary>
    public string? Collection { get; init; }

    // === TEXT CONTENT (for search) ===

    /// <summary>
    /// Primary text content for search.
    /// - Documents: Full text
    /// - Images: OCR text + captions
    /// - Audio: Transcription
    /// - Video: Transcription + frame captions
    /// </summary>
    public string? TextContent { get; init; }

    /// <summary>
    /// Brief summary or caption.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Title or filename.
    /// </summary>
    public string? Title { get; init; }

    // === EMBEDDINGS (for semantic search) ===

    /// <summary>
    /// Primary semantic embedding vector.
    /// Same dimension across all content types for unified search.
    /// </summary>
    public float[]? Embedding { get; init; }

    /// <summary>
    /// Embedding model used.
    /// </summary>
    public string? EmbeddingModel { get; init; }

    /// <summary>
    /// Additional embeddings (e.g., CLIP visual, audio features).
    /// </summary>
    public Dictionary<string, float[]>? AdditionalEmbeddings { get; init; }

    // === EXTRACTED ENTITIES (for GraphRAG) ===

    /// <summary>
    /// Named entities extracted from content.
    /// Unified across modalities for knowledge graph construction.
    /// </summary>
    public List<ExtractedEntity>? Entities { get; init; }

    /// <summary>
    /// Relationships between entities.
    /// </summary>
    public List<EntityRelationship>? Relationships { get; init; }

    // === SIGNALS (analysis results) ===

    /// <summary>
    /// All signals from wave analysis.
    /// Preserves full analysis context.
    /// </summary>
    public List<Signal>? Signals { get; init; }

    /// <summary>
    /// Get a signal value by key.
    /// </summary>
    public T? GetSignal<T>(string key) where T : class =>
        Signals?.FirstOrDefault(s => s.Key == key)?.GetValue<T>();

    /// <summary>
    /// Get a value-type signal value by key.
    /// </summary>
    public T? GetSignalValue<T>(string key) where T : struct =>
        Signals?.FirstOrDefault(s => s.Key == key)?.Value is T val ? val : null;

    // === METADATA ===

    /// <summary>
    /// Content-type specific metadata.
    /// </summary>
    public ContentMetadata? Metadata { get; init; }

    /// <summary>
    /// Custom user-defined metadata.
    /// </summary>
    public Dictionary<string, object>? CustomMetadata { get; init; }

    /// <summary>
    /// Tags for categorization.
    /// </summary>
    public List<string>? Tags { get; init; }

    // === QUALITY METRICS ===

    /// <summary>
    /// Overall quality score (0-1).
    /// </summary>
    public double QualityScore { get; init; } = 1.0;

    /// <summary>
    /// Confidence in extracted content.
    /// </summary>
    public double ContentConfidence { get; init; } = 1.0;

    /// <summary>
    /// Whether this entity needs review.
    /// </summary>
    public bool NeedsReview { get; init; } = false;

    /// <summary>
    /// Reason for needing review.
    /// </summary>
    public string? ReviewReason { get; init; }
}

/// <summary>
/// Content types for retrieval entities.
/// </summary>
public enum ContentType
{
    Document,
    Image,
    Audio,
    Video,
    Mixed,
    Unknown
}

/// <summary>
/// Extracted named entity.
/// </summary>
public class ExtractedEntity
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Description { get; init; }
    public double Confidence { get; init; } = 1.0;
    public string? Source { get; init; }

    /// <summary>
    /// Where in the content this entity appears.
    /// </summary>
    public List<EntityMention>? Mentions { get; init; }

    /// <summary>
    /// Additional attributes.
    /// </summary>
    public Dictionary<string, object>? Attributes { get; init; }
}

/// <summary>
/// A mention of an entity in content.
/// </summary>
public class EntityMention
{
    /// <summary>
    /// Character offset in text, or timestamp in audio/video.
    /// </summary>
    public int Start { get; init; }
    public int End { get; init; }

    /// <summary>
    /// The actual text/label of the mention.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// For images: bounding box.
    /// </summary>
    public BoundingBox? BoundingBox { get; init; }

    /// <summary>
    /// For audio/video: timestamp in seconds.
    /// </summary>
    public double? Timestamp { get; init; }
}

/// <summary>
/// Bounding box for visual entity mentions.
/// </summary>
public record BoundingBox(double X, double Y, double Width, double Height);

/// <summary>
/// Relationship between entities.
/// </summary>
public class EntityRelationship
{
    public required string SourceEntityId { get; init; }
    public required string TargetEntityId { get; init; }
    public required string RelationType { get; init; }
    public string? Description { get; init; }
    public double Confidence { get; init; } = 1.0;
    public string? Source { get; init; }
    public Dictionary<string, object>? Attributes { get; init; }
}

/// <summary>
/// Content-type specific metadata.
/// </summary>
public record ContentMetadata
{
    // === DOCUMENT METADATA ===
    public int? PageCount { get; init; }
    public int? WordCount { get; init; }
    public string? Language { get; init; }
    public string? Author { get; init; }

    // === IMAGE METADATA ===
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Format { get; init; }
    public bool? IsAnimated { get; init; }
    public int? FrameCount { get; init; }
    public string? DominantColor { get; init; }

    // === AUDIO METADATA ===
    public double? DurationSeconds { get; init; }
    public int? SampleRate { get; init; }
    public int? Channels { get; init; }
    public string? AudioCodec { get; init; }

    // === VIDEO METADATA ===
    public double? FrameRate { get; init; }
    public string? VideoCodec { get; init; }
    public int? SceneCount { get; init; }

    // === COMMON ===
    public long? FileSizeBytes { get; init; }
    public string? MimeType { get; init; }
    public DateTime? FileCreatedAt { get; init; }
    public DateTime? FileModifiedAt { get; init; }
}

/// <summary>
/// Standard entity types across all content modalities.
/// </summary>
public static class EntityTypes
{
    // People & Organizations
    public const string Person = "person";
    public const string Organization = "organization";
    public const string Location = "location";

    // Document-specific
    public const string Date = "date";
    public const string Event = "event";
    public const string Concept = "concept";
    public const string Product = "product";

    // Visual-specific
    public const string Object = "object";
    public const string Face = "face";
    public const string Scene = "scene";
    public const string Text = "text";
    public const string Logo = "logo";

    // Audio-specific
    public const string Speaker = "speaker";
    public const string Sound = "sound";
    public const string Music = "music";

    // Cross-modal
    public const string Topic = "topic";
    public const string Sentiment = "sentiment";
    public const string Action = "action";
}

/// <summary>
/// Standard relationship types.
/// </summary>
public static class RelationshipTypes
{
    public const string RelatedTo = "related_to";
    public const string PartOf = "part_of";
    public const string Contains = "contains";
    public const string References = "references";
    public const string CreatedBy = "created_by";
    public const string LocatedIn = "located_in";
    public const string MemberOf = "member_of";
    public const string OccurredAt = "occurred_at";
    public const string Depicts = "depicts";
    public const string Mentions = "mentions";
    public const string SimilarTo = "similar_to";
}
