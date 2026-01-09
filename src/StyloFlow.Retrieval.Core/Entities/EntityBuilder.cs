using StyloFlow.Retrieval.Analysis;

namespace StyloFlow.Retrieval.Entities;

/// <summary>
/// Fluent builder for creating RetrievalEntities from analysis results.
/// Works across all content types.
/// </summary>
public class EntityBuilder
{
    private string? _id;
    private ContentType _contentType = ContentType.Unknown;
    private string? _source;
    private string? _contentHash;
    private string? _collection;
    private string? _textContent;
    private string? _summary;
    private string? _title;
    private float[]? _embedding;
    private string? _embeddingModel;
    private readonly Dictionary<string, float[]> _additionalEmbeddings = new();
    private readonly List<ExtractedEntity> _entities = new();
    private readonly List<EntityRelationship> _relationships = new();
    private readonly List<Signal> _signals = new();
    private ContentMetadata? _metadata;
    private readonly Dictionary<string, object> _customMetadata = new();
    private readonly List<string> _tags = new();
    private double _qualityScore = 1.0;
    private double _contentConfidence = 1.0;
    private bool _needsReview = false;
    private string? _reviewReason;

    public EntityBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public EntityBuilder WithContentType(ContentType type)
    {
        _contentType = type;
        return this;
    }

    public EntityBuilder WithSource(string source)
    {
        _source = source;
        return this;
    }

    public EntityBuilder WithContentHash(string hash)
    {
        _contentHash = hash;
        return this;
    }

    public EntityBuilder WithCollection(string collection)
    {
        _collection = collection;
        return this;
    }

    public EntityBuilder WithTextContent(string text)
    {
        _textContent = text;
        return this;
    }

    public EntityBuilder WithSummary(string summary)
    {
        _summary = summary;
        return this;
    }

    public EntityBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public EntityBuilder WithEmbedding(float[] embedding, string? model = null)
    {
        _embedding = embedding;
        _embeddingModel = model;
        return this;
    }

    public EntityBuilder WithAdditionalEmbedding(string name, float[] embedding)
    {
        _additionalEmbeddings[name] = embedding;
        return this;
    }

    public EntityBuilder WithEntity(ExtractedEntity entity)
    {
        _entities.Add(entity);
        return this;
    }

    public EntityBuilder WithEntities(IEnumerable<ExtractedEntity> entities)
    {
        _entities.AddRange(entities);
        return this;
    }

    public EntityBuilder WithRelationship(EntityRelationship relationship)
    {
        _relationships.Add(relationship);
        return this;
    }

    public EntityBuilder WithRelationships(IEnumerable<EntityRelationship> relationships)
    {
        _relationships.AddRange(relationships);
        return this;
    }

    public EntityBuilder WithSignal(Signal signal)
    {
        _signals.Add(signal);
        return this;
    }

    public EntityBuilder WithSignals(IEnumerable<Signal> signals)
    {
        _signals.AddRange(signals);
        return this;
    }

    public EntityBuilder WithMetadata(ContentMetadata metadata)
    {
        _metadata = metadata;
        return this;
    }

    public EntityBuilder WithCustomMetadata(string key, object value)
    {
        _customMetadata[key] = value;
        return this;
    }

    public EntityBuilder WithTag(string tag)
    {
        _tags.Add(tag);
        return this;
    }

    public EntityBuilder WithTags(IEnumerable<string> tags)
    {
        _tags.AddRange(tags);
        return this;
    }

    public EntityBuilder WithQualityScore(double score)
    {
        _qualityScore = score;
        return this;
    }

    public EntityBuilder WithContentConfidence(double confidence)
    {
        _contentConfidence = confidence;
        return this;
    }

    public EntityBuilder NeedsReview(string reason)
    {
        _needsReview = true;
        _reviewReason = reason;
        return this;
    }

    /// <summary>
    /// Build entity from analysis context signals.
    /// Automatically extracts relevant data from signals.
    /// </summary>
    public EntityBuilder FromAnalysisContext(AnalysisContext context)
    {
        // Add all signals
        _signals.AddRange(context.GetAllSignals());

        // Extract identity info
        var hash = context.GetValue<string>("identity.sha256");
        if (!string.IsNullOrEmpty(hash))
            _contentHash = hash;

        // Extract text content from various sources
        var ocrText = context.GetValue<string>("ocr.text");
        var transcription = context.GetValue<string>("speech.transcription");
        var caption = context.GetValue<string>("vision.caption");

        var textParts = new List<string>();
        if (!string.IsNullOrEmpty(ocrText)) textParts.Add(ocrText);
        if (!string.IsNullOrEmpty(transcription)) textParts.Add(transcription);
        if (!string.IsNullOrEmpty(caption)) textParts.Add(caption);
        if (textParts.Count > 0)
            _textContent = string.Join("\n\n", textParts);

        // Extract summary
        var summary = context.GetValue<string>("content.summary") ?? caption;
        if (!string.IsNullOrEmpty(summary))
            _summary = summary;

        // Extract embeddings
        var clipEmbedding = context.GetValue<float[]>("vision.clip.embedding");
        if (clipEmbedding != null)
            _additionalEmbeddings["clip_visual"] = clipEmbedding;

        // Extract quality score
        var quality = context.GetValue<double?>("quality.overall");
        if (quality.HasValue)
            _qualityScore = quality.Value;

        // Check for escalation/review needs
        var needsEscalation = context.GetValue<bool?>("route.needs_escalation");
        if (needsEscalation == true)
        {
            _needsReview = true;
            _reviewReason = context.GetValue<string>("route.escalation_reason") ?? "Auto-routing suggested review";
        }

        return this;
    }

    /// <summary>
    /// Build the final entity.
    /// </summary>
    public RetrievalEntity Build()
    {
        if (string.IsNullOrEmpty(_id))
            _id = Guid.NewGuid().ToString("N");

        if (string.IsNullOrEmpty(_source))
            throw new InvalidOperationException("Source is required");

        return new RetrievalEntity
        {
            Id = _id,
            ContentType = _contentType,
            Source = _source,
            ContentHash = _contentHash,
            Collection = _collection,
            TextContent = _textContent,
            Summary = _summary,
            Title = _title,
            Embedding = _embedding,
            EmbeddingModel = _embeddingModel,
            AdditionalEmbeddings = _additionalEmbeddings.Count > 0 ? _additionalEmbeddings : null,
            Entities = _entities.Count > 0 ? _entities : null,
            Relationships = _relationships.Count > 0 ? _relationships : null,
            Signals = _signals.Count > 0 ? _signals : null,
            Metadata = _metadata,
            CustomMetadata = _customMetadata.Count > 0 ? _customMetadata : null,
            Tags = _tags.Count > 0 ? _tags : null,
            QualityScore = _qualityScore,
            ContentConfidence = _contentConfidence,
            NeedsReview = _needsReview,
            ReviewReason = _reviewReason
        };
    }
}

/// <summary>
/// Interface for domain-specific entity extraction.
/// Implemented by document, image, audio, video processors.
/// </summary>
public interface IEntityExtractor
{
    /// <summary>
    /// Content type this extractor handles.
    /// </summary>
    ContentType SupportedContentType { get; }

    /// <summary>
    /// Extract a RetrievalEntity from content.
    /// </summary>
    Task<RetrievalEntity> ExtractAsync(
        string contentPath,
        AnalysisContext? context = null,
        string? collection = null,
        CancellationToken ct = default);
}

/// <summary>
/// Extensions for working with entities.
/// </summary>
public static class EntityExtensions
{
    /// <summary>
    /// Get all text for search indexing.
    /// </summary>
    public static string GetSearchableText(this RetrievalEntity entity)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(entity.Title))
            parts.Add(entity.Title);

        if (!string.IsNullOrEmpty(entity.Summary))
            parts.Add(entity.Summary);

        if (!string.IsNullOrEmpty(entity.TextContent))
            parts.Add(entity.TextContent);

        // Add entity names
        if (entity.Entities != null)
        {
            foreach (var e in entity.Entities)
            {
                parts.Add(e.Name);
                if (!string.IsNullOrEmpty(e.Description))
                    parts.Add(e.Description);
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Get all entity types present.
    /// </summary>
    public static IEnumerable<string> GetEntityTypes(this RetrievalEntity entity) =>
        entity.Entities?.Select(e => e.Type).Distinct() ?? Enumerable.Empty<string>();

    /// <summary>
    /// Find entities by type.
    /// </summary>
    public static IEnumerable<ExtractedEntity> GetEntitiesByType(this RetrievalEntity entity, string type) =>
        entity.Entities?.Where(e => e.Type == type) ?? Enumerable.Empty<ExtractedEntity>();

    /// <summary>
    /// Check if entity contains a specific entity type.
    /// </summary>
    public static bool HasEntityType(this RetrievalEntity entity, string type) =>
        entity.Entities?.Any(e => e.Type == type) ?? false;
}
