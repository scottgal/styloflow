using StyloFlow.Manifests;

namespace StyloFlow.Entities;

/// <summary>
/// Registry of entity types that atoms can accept/produce.
/// Entity types can be defined in YAML files or registered programmatically.
/// </summary>
public class EntityTypeRegistry
{
    private readonly Dictionary<string, EntityTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

    public EntityTypeRegistry()
    {
        // Register built-in entity types
        RegisterBuiltInTypes();
    }

    /// <summary>
    /// Register an entity type definition.
    /// </summary>
    public void Register(EntityTypeDefinition definition)
    {
        _types[definition.Type] = definition;
    }

    /// <summary>
    /// Get an entity type definition by type identifier.
    /// Supports wildcard matching (e.g., "image/*" matches "image/png").
    /// </summary>
    public EntityTypeDefinition? Get(string type)
    {
        // Exact match
        if (_types.TryGetValue(type, out var definition))
            return definition;

        // Try wildcard match (e.g., "image/*" for "image.png")
        var parts = type.Split('.');
        if (parts.Length > 1)
        {
            var wildcardKey = $"{parts[0]}.*";
            if (_types.TryGetValue(wildcardKey, out definition))
                return definition;
        }

        return null;
    }

    /// <summary>
    /// Check if an entity type is registered.
    /// </summary>
    public bool IsRegistered(string type)
    {
        return Get(type) != null;
    }

    /// <summary>
    /// Get all registered entity types.
    /// </summary>
    public IReadOnlyDictionary<string, EntityTypeDefinition> GetAll()
    {
        return _types;
    }

    /// <summary>
    /// Get entity types matching a pattern.
    /// </summary>
    public IEnumerable<EntityTypeDefinition> GetByPattern(string pattern)
    {
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1].TrimEnd('.');
            return _types.Values.Where(t =>
                t.Type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                t.Category?.Equals(prefix, StringComparison.OrdinalIgnoreCase) == true);
        }

        var exact = Get(pattern);
        return exact != null ? [exact] : Enumerable.Empty<EntityTypeDefinition>();
    }

    /// <summary>
    /// Validate that an entity matches its type specification.
    /// </summary>
    public EntityValidationResult Validate(string type, object? entity, EntityConstraints? constraints = null)
    {
        var definition = Get(type);
        if (definition == null)
        {
            return new EntityValidationResult
            {
                IsValid = false,
                Errors = [$"Unknown entity type: {type}"]
            };
        }

        var errors = new List<string>();

        // Validate constraints
        var effectiveConstraints = constraints ?? definition.DefaultConstraints;
        if (effectiveConstraints != null && entity != null)
        {
            // File size validation
            if (entity is byte[] bytes)
            {
                if (effectiveConstraints.MaxSizeBytes.HasValue && bytes.Length > effectiveConstraints.MaxSizeBytes.Value)
                    errors.Add($"Size {bytes.Length} exceeds maximum {effectiveConstraints.MaxSizeBytes.Value}");
                if (effectiveConstraints.MinSizeBytes.HasValue && bytes.Length < effectiveConstraints.MinSizeBytes.Value)
                    errors.Add($"Size {bytes.Length} is below minimum {effectiveConstraints.MinSizeBytes.Value}");
            }

            // Add more validation as needed...
        }

        return new EntityValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private void RegisterBuiltInTypes()
    {
        // HTTP entities
        Register(new EntityTypeDefinition
        {
            Type = "http.request",
            Category = "http",
            Description = "HTTP request with headers, body, and metadata",
            MimeType = "application/http-request",
            SignalPatterns = ["request.headers.*", "request.body", "request.method", "request.path"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "http.response",
            Category = "http",
            Description = "HTTP response with headers, body, and status",
            MimeType = "application/http-response",
            SignalPatterns = ["response.headers.*", "response.body", "response.status"]
        });

        // Image entities
        Register(new EntityTypeDefinition
        {
            Type = "image.*",
            Category = "image",
            Description = "Any image file",
            MimeType = "image/*",
            DefaultConstraints = new EntityConstraints
            {
                MaxSizeBytes = 50 * 1024 * 1024, // 50MB default max
                MaxWidth = 10000,
                MaxHeight = 10000
            }
        });

        Register(new EntityTypeDefinition
        {
            Type = "image.png",
            Category = "image",
            Description = "PNG image",
            MimeType = "image/png"
        });

        Register(new EntityTypeDefinition
        {
            Type = "image.jpeg",
            Category = "image",
            Description = "JPEG image",
            MimeType = "image/jpeg"
        });

        // Video entities
        Register(new EntityTypeDefinition
        {
            Type = "video.*",
            Category = "video",
            Description = "Any video file",
            MimeType = "video/*",
            DefaultConstraints = new EntityConstraints
            {
                MaxSizeBytes = 500 * 1024 * 1024, // 500MB default max
                MaxDurationSeconds = 3600 // 1 hour max
            }
        });

        Register(new EntityTypeDefinition
        {
            Type = "video.mp4",
            Category = "video",
            Description = "MP4 video",
            MimeType = "video/mp4"
        });

        // Audio entities
        Register(new EntityTypeDefinition
        {
            Type = "audio.*",
            Category = "audio",
            Description = "Any audio file",
            MimeType = "audio/*"
        });

        // Document entities
        Register(new EntityTypeDefinition
        {
            Type = "document.pdf",
            Category = "document",
            Description = "PDF document",
            MimeType = "application/pdf"
        });

        // Behavioral entities
        Register(new EntityTypeDefinition
        {
            Type = "behavioral.signature",
            Category = "behavioral",
            Description = "User behavioral signature - timing patterns, mouse movements, etc.",
            SignalPatterns = ["behavioral.timing.*", "behavioral.mouse.*", "behavioral.keyboard.*"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "behavioral.session",
            Category = "behavioral",
            Description = "User session data",
            SignalPatterns = ["session.*"]
        });

        // Network entities
        Register(new EntityTypeDefinition
        {
            Type = "network.ip",
            Category = "network",
            Description = "IP address with geolocation and reputation",
            SignalPatterns = ["ip.address", "ip.geo.*", "ip.reputation.*"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "network.tls",
            Category = "network",
            Description = "TLS fingerprint and certificate info",
            SignalPatterns = ["tls.fingerprint", "tls.cipher", "tls.version"]
        });

        // Detection entities
        Register(new EntityTypeDefinition
        {
            Type = "detection.contribution",
            Category = "detection",
            Description = "A detection contribution from a detector atom",
            SignalPatterns = ["contribution.*"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "detection.ledger",
            Category = "detection",
            Description = "The accumulated detection evidence",
            SignalPatterns = ["ledger.*"]
        });

        // Generic entities
        Register(new EntityTypeDefinition
        {
            Type = "text.plain",
            Category = "text",
            Description = "Plain text content",
            MimeType = "text/plain"
        });

        Register(new EntityTypeDefinition
        {
            Type = "data.json",
            Category = "data",
            Description = "JSON data",
            MimeType = "application/json",
            Persistence = EntityPersistence.Json
        });

        // Persistence-aware entity types

        Register(new EntityTypeDefinition
        {
            Type = "embedded.vector",
            Category = "embedded",
            Description = "Vector embedding for similarity search",
            Persistence = EntityPersistence.Embedded,
            VectorDimension = 1536, // OpenAI default, can override
            SignalPatterns = ["embedding.vector", "embedding.model", "embedding.dimensions"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "embedded.multivector",
            Category = "embedded",
            Description = "Multi-vector embedding (ColBERT-style late interaction)",
            Persistence = EntityPersistence.Embedded,
            SignalPatterns = ["embedding.vectors", "embedding.tokens", "embedding.model"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "persistence.record",
            Category = "persistence",
            Description = "Database-stored record with ID",
            Persistence = EntityPersistence.Database,
            SignalPatterns = ["record.id", "record.table", "record.data"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "persistence.cached",
            Category = "persistence",
            Description = "Cached entity with TTL",
            Persistence = EntityPersistence.Cached,
            SignalPatterns = ["cache.key", "cache.ttl", "cache.data"]
        });

        // Bot detection domain entities
        Register(new EntityTypeDefinition
        {
            Type = "botdetection.signature",
            Category = "botdetection",
            Description = "Bot detection signature - aggregated signals for classification",
            Persistence = EntityPersistence.Ephemeral,
            SignalPatterns = ["botdetection.ua.*", "botdetection.ip.*", "botdetection.behavioral.*", "botdetection.tls.*"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "botdetection.result",
            Category = "botdetection",
            Description = "Bot detection result with confidence and classification",
            Persistence = EntityPersistence.Json,
            SignalPatterns = ["botdetection.confidence", "botdetection.classification", "botdetection.reasons"]
        });

        Register(new EntityTypeDefinition
        {
            Type = "botdetection.learning",
            Category = "botdetection",
            Description = "Bot detection learning record for training",
            Persistence = EntityPersistence.Database,
            StorageHint = "botdetection_learning_records",
            SignalPatterns = ["learning.fingerprint", "learning.signals", "learning.label"]
        });
    }
}

/// <summary>
/// Definition of an entity type that can be accepted/produced by atoms.
/// </summary>
public class EntityTypeDefinition
{
    /// <summary>
    /// Entity type identifier. Hierarchical dot-notation.
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Category for grouping related entity types.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// MIME type if this is a file-based entity.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Signal patterns that compose this entity from SignalSink data.
    /// </summary>
    public List<string> SignalPatterns { get; set; } = [];

    /// <summary>
    /// Schema reference for structured entities.
    /// </summary>
    public SchemaRef? Schema { get; set; }

    /// <summary>
    /// Default validation constraints.
    /// </summary>
    public EntityConstraints? DefaultConstraints { get; set; }

    /// <summary>
    /// Parent entity type (for inheritance).
    /// </summary>
    public string? Extends { get; set; }

    /// <summary>
    /// Tags for categorization.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Persistence hint - where/how this entity is typically stored.
    /// </summary>
    public EntityPersistence Persistence { get; set; } = EntityPersistence.Ephemeral;

    /// <summary>
    /// For embedded entities - dimension of the vector.
    /// </summary>
    public int? VectorDimension { get; set; }

    /// <summary>
    /// For DB entities - table or collection name hint.
    /// </summary>
    public string? StorageHint { get; set; }
}

/// <summary>
/// Hints about how an entity is persisted.
/// </summary>
public enum EntityPersistence
{
    /// <summary>
    /// Ephemeral - lives only in memory during processing.
    /// </summary>
    Ephemeral,

    /// <summary>
    /// JSON - serializable to JSON for storage/transport.
    /// </summary>
    Json,

    /// <summary>
    /// Database - stored in a persistence store (SQL, NoSQL, etc.)
    /// </summary>
    Database,

    /// <summary>
    /// Embedded - vector embedding for similarity search.
    /// </summary>
    Embedded,

    /// <summary>
    /// File - stored as a file on disk or blob storage.
    /// </summary>
    File,

    /// <summary>
    /// Cached - short-term cache storage (Redis, memory cache, etc.)
    /// </summary>
    Cached
}

/// <summary>
/// Result of entity validation.
/// </summary>
public class EntityValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
