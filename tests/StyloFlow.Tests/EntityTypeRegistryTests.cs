using StyloFlow.Entities;
using StyloFlow.Manifests;
using Xunit;

namespace StyloFlow.Tests;

public class EntityTypeRegistryTests
{
    [Fact]
    public void Registry_HasBuiltInTypes()
    {
        // Arrange & Act
        var registry = new EntityTypeRegistry();

        // Assert - check some built-in types exist
        Assert.NotNull(registry.Get("http.request"));
        Assert.NotNull(registry.Get("http.response"));
        Assert.NotNull(registry.Get("image.png"));
        Assert.NotNull(registry.Get("video.mp4"));
        Assert.NotNull(registry.Get("data.json"));
    }

    [Fact]
    public void Registry_WildcardMatch_Works()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act - request a specific image type that should fall back to wildcard
        var imagePng = registry.Get("image.png");
        var imageWildcard = registry.Get("image.*");

        // Assert
        Assert.NotNull(imagePng);
        Assert.NotNull(imageWildcard);
        Assert.Equal("image", imagePng.Category);
        Assert.Equal("image", imageWildcard.Category);
    }

    [Fact]
    public void Registry_Register_AddsNewType()
    {
        // Arrange
        var registry = new EntityTypeRegistry();
        var customType = new EntityTypeDefinition
        {
            Type = "custom.mytype",
            Category = "custom",
            Description = "My custom type",
            Persistence = EntityPersistence.Json
        };

        // Act
        registry.Register(customType);
        var retrieved = registry.Get("custom.mytype");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("custom.mytype", retrieved.Type);
        Assert.Equal("custom", retrieved.Category);
        Assert.Equal(EntityPersistence.Json, retrieved.Persistence);
    }

    [Fact]
    public void Registry_IsRegistered_ReturnsTrueForExisting()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act & Assert
        Assert.True(registry.IsRegistered("http.request"));
        Assert.True(registry.IsRegistered("data.json"));
        Assert.False(registry.IsRegistered("nonexistent.type"));
    }

    [Fact]
    public void Registry_GetAll_ReturnsAllTypes()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var all = registry.GetAll();

        // Assert
        Assert.True(all.Count > 10, "Should have many built-in types");
        Assert.Contains("http.request", all.Keys);
        Assert.Contains("data.json", all.Keys);
    }

    [Fact]
    public void Registry_GetByPattern_WithWildcard_ReturnsMatches()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var httpTypes = registry.GetByPattern("http.*").ToList();
        var imageTypes = registry.GetByPattern("image.*").ToList();

        // Assert
        Assert.True(httpTypes.Count >= 2, "Should have http.request and http.response");
        Assert.True(imageTypes.Count >= 2, "Should have image.* and specific types");
    }

    [Fact]
    public void Registry_GetByPattern_Exact_ReturnsSingle()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var exact = registry.GetByPattern("http.request").ToList();

        // Assert
        Assert.Single(exact);
        Assert.Equal("http.request", exact[0].Type);
    }

    [Fact]
    public void Registry_Validate_UnknownType_ReturnsInvalid()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var result = registry.Validate("unknown.type", new object());

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Unknown entity type", result.Errors[0]);
    }

    [Fact]
    public void Registry_Validate_KnownType_ReturnsValid()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var result = registry.Validate("http.request", new object());

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Registry_Validate_ByteArray_ChecksSize()
    {
        // Arrange
        var registry = new EntityTypeRegistry();
        var smallData = new byte[100];
        var largeData = new byte[100 * 1024 * 1024]; // 100MB

        var constraints = new EntityConstraints
        {
            MaxSizeBytes = 50 * 1024 * 1024, // 50MB
            MinSizeBytes = 10
        };

        // Act
        var smallResult = registry.Validate("image.png", smallData, constraints);
        var largeResult = registry.Validate("image.png", largeData, constraints);

        // Assert
        Assert.True(smallResult.IsValid);
        Assert.False(largeResult.IsValid);
        Assert.Contains("exceeds maximum", largeResult.Errors[0]);
    }

    [Fact]
    public void Registry_BuiltInTypes_HaveCorrectPersistence()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act & Assert
        var json = registry.Get("data.json");
        Assert.Equal(EntityPersistence.Json, json!.Persistence);

        var embedded = registry.Get("embedded.vector");
        Assert.Equal(EntityPersistence.Embedded, embedded!.Persistence);

        var persistence = registry.Get("persistence.record");
        Assert.Equal(EntityPersistence.Database, persistence!.Persistence);

        var cached = registry.Get("persistence.cached");
        Assert.Equal(EntityPersistence.Cached, cached!.Persistence);
    }

    [Fact]
    public void Registry_EmbeddedTypes_HaveVectorDimension()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var embedded = registry.Get("embedded.vector");

        // Assert
        Assert.NotNull(embedded);
        Assert.NotNull(embedded.VectorDimension);
        Assert.Equal(1536, embedded.VectorDimension);
    }

    [Fact]
    public void Registry_BotDetectionTypes_Exist()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act & Assert
        Assert.NotNull(registry.Get("botdetection.signature"));
        Assert.NotNull(registry.Get("botdetection.result"));
        Assert.NotNull(registry.Get("botdetection.learning"));
    }

    [Fact]
    public void Registry_BotDetectionLearning_HasStorageHint()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var learning = registry.Get("botdetection.learning");

        // Assert
        Assert.NotNull(learning);
        Assert.Equal(EntityPersistence.Database, learning.Persistence);
        Assert.Equal("botdetection_learning_records", learning.StorageHint);
    }

    [Fact]
    public void Registry_Types_HaveSignalPatterns()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var httpRequest = registry.Get("http.request");
        var behavioral = registry.Get("behavioral.signature");

        // Assert
        Assert.NotEmpty(httpRequest!.SignalPatterns);
        Assert.Contains("request.headers.*", httpRequest.SignalPatterns);

        Assert.NotEmpty(behavioral!.SignalPatterns);
        Assert.Contains("behavioral.timing.*", behavioral.SignalPatterns);
    }

    [Fact]
    public void Registry_ImageTypes_HaveConstraints()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var imageWildcard = registry.Get("image.*");

        // Assert
        Assert.NotNull(imageWildcard);
        Assert.NotNull(imageWildcard.DefaultConstraints);
        Assert.Equal(50 * 1024 * 1024, imageWildcard.DefaultConstraints.MaxSizeBytes);
        Assert.Equal(10000, imageWildcard.DefaultConstraints.MaxWidth);
        Assert.Equal(10000, imageWildcard.DefaultConstraints.MaxHeight);
    }

    [Fact]
    public void Registry_VideoTypes_HaveDurationConstraint()
    {
        // Arrange
        var registry = new EntityTypeRegistry();

        // Act
        var videoWildcard = registry.Get("video.*");

        // Assert
        Assert.NotNull(videoWildcard);
        Assert.NotNull(videoWildcard.DefaultConstraints);
        Assert.Equal(3600, videoWildcard.DefaultConstraints.MaxDurationSeconds);
    }

    [Fact]
    public void EntityTypeDefinition_DefaultValues()
    {
        // Arrange & Act
        var definition = new EntityTypeDefinition();

        // Assert
        Assert.Equal("", definition.Type);
        Assert.Null(definition.Category);
        Assert.Equal("", definition.Description);
        Assert.Null(definition.MimeType);
        Assert.Empty(definition.SignalPatterns);
        Assert.Null(definition.Schema);
        Assert.Null(definition.DefaultConstraints);
        Assert.Null(definition.Extends);
        Assert.Empty(definition.Tags);
        Assert.Equal(EntityPersistence.Ephemeral, definition.Persistence);
        Assert.Null(definition.VectorDimension);
        Assert.Null(definition.StorageHint);
    }

    [Fact]
    public void EntityValidationResult_DefaultValues()
    {
        // Arrange & Act
        var result = new EntityValidationResult();

        // Assert
        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void EntityConstraints_AllPropertiesNullable()
    {
        // Arrange & Act
        var constraints = new EntityConstraints();

        // Assert
        Assert.Null(constraints.MaxSizeBytes);
        Assert.Null(constraints.MinSizeBytes);
        Assert.Null(constraints.MaxDurationSeconds);
        Assert.Null(constraints.MaxWidth);
        Assert.Null(constraints.MaxHeight);
        Assert.Empty(constraints.Rules);
    }
}
