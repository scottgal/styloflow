using System.Reflection;
using Microsoft.Extensions.Logging;
using StyloFlow.Entities;
using Xunit;

namespace StyloFlow.Tests;

public class EntityTypeLoaderTests
{
    private readonly EntityTypeLoader _loader;

    public EntityTypeLoaderTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _loader = new EntityTypeLoader(loggerFactory.CreateLogger<EntityTypeLoader>());
    }

    [Fact]
    public void LoadFromYaml_ValidYaml_ReturnsDefinition()
    {
        // Arrange
        var yaml = @"
type: test.entity
category: test
description: A test entity type
mime_type: application/test
signal_patterns:
  - test.signal.*
  - test.data
";

        // Act
        var result = _loader.LoadFromYaml(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test.entity", result.Type);
        Assert.Equal("test", result.Category);
        Assert.Equal("A test entity type", result.Description);
        Assert.Equal("application/test", result.MimeType);
        Assert.Contains("test.signal.*", result.SignalPatterns);
        Assert.Contains("test.data", result.SignalPatterns);
    }

    [Fact]
    public void LoadFromYaml_InvalidYaml_ReturnsNull()
    {
        // Arrange
        var yaml = "this is not valid yaml: [unclosed";

        // Act
        var result = _loader.LoadFromYaml(yaml);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void LoadFromYaml_EmptyYaml_ReturnsNull()
    {
        // Arrange
        var yaml = "";

        // Act
        var result = _loader.LoadFromYaml(yaml);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void LoadFromYaml_WithConstraints_ParsesCorrectly()
    {
        // Arrange
        var yaml = @"
type: file.document
category: file
description: Document with constraints
default_constraints:
  max_size_bytes: 10485760
  min_size_bytes: 100
  max_width: 1920
  max_height: 1080
";

        // Act
        var result = _loader.LoadFromYaml(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.DefaultConstraints);
        Assert.Equal(10485760, result.DefaultConstraints.MaxSizeBytes);
        Assert.Equal(100, result.DefaultConstraints.MinSizeBytes);
        Assert.Equal(1920, result.DefaultConstraints.MaxWidth);
        Assert.Equal(1080, result.DefaultConstraints.MaxHeight);
    }

    [Fact]
    public void LoadFromYaml_WithSchema_ParsesCorrectly()
    {
        // Arrange
        var yaml = @"
type: structured.data
category: structured
description: Structured data with schema
schema:
  format: json-schema
  location: schemas/data.schema.json
  version: '1.0'
";

        // Act
        var result = _loader.LoadFromYaml(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Schema);
        Assert.Equal("json-schema", result.Schema.Format);
        Assert.Equal("schemas/data.schema.json", result.Schema.Location);
        Assert.Equal("1.0", result.Schema.Version);
    }

    [Fact]
    public void LoadFromYaml_WithExtends_ParsesCorrectly()
    {
        // Arrange
        var yaml = @"
type: custom.request
category: custom
description: Custom request extending http.request
extends: http.request
";

        // Act
        var result = _loader.LoadFromYaml(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("http.request", result.Extends);
    }

    [Fact]
    public void LoadFromYaml_WithTags_ParsesCorrectly()
    {
        // Arrange
        var yaml = @"
type: tagged.entity
category: test
description: Entity with tags
tags:
  - fast-path
  - cacheable
  - v2
";

        // Act
        var result = _loader.LoadFromYaml(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Tags.Count);
        Assert.Contains("fast-path", result.Tags);
        Assert.Contains("cacheable", result.Tags);
        Assert.Contains("v2", result.Tags);
    }

    [Fact]
    public void LoadFromDirectory_NonExistentDirectory_ReturnsEmpty()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var results = _loader.LoadFromDirectory(nonExistentPath).ToList();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void LoadFromDirectory_EmptyDirectory_ReturnsEmpty()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act
            var results = _loader.LoadFromDirectory(tempDir).ToList();

            // Assert
            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void LoadFromDirectory_WithValidFiles_LoadsAll()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var yaml = @"
entity_types:
  - type: test.type1
    category: test
    description: First test type
  - type: test.type2
    category: test
    description: Second test type
";
        File.WriteAllText(Path.Combine(tempDir, "test.entity.yaml"), yaml);

        try
        {
            // Act
            var results = _loader.LoadFromDirectory(tempDir).ToList();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Type == "test.type1");
            Assert.Contains(results, r => r.Type == "test.type2");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadFromDirectory_WithCustomPattern_FiltersCorrectly()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var yaml1 = @"
entity_types:
  - type: custom.type
    category: custom
    description: Custom type
";
        var yaml2 = @"
entity_types:
  - type: other.type
    category: other
    description: Other type
";
        File.WriteAllText(Path.Combine(tempDir, "custom.entity.yaml"), yaml1);
        File.WriteAllText(Path.Combine(tempDir, "other.types.yaml"), yaml2);

        try
        {
            // Act - only load *.entity.yaml
            var results = _loader.LoadFromDirectory(tempDir, "*.entity.yaml").ToList();

            // Assert
            Assert.Single(results);
            Assert.Equal("custom.type", results[0].Type);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadFromDirectory_WithInvalidFile_SkipsAndContinues()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var validYaml = @"
entity_types:
  - type: valid.type
    category: test
    description: Valid type
";
        var invalidYaml = "this is not: [valid yaml";

        File.WriteAllText(Path.Combine(tempDir, "valid.entity.yaml"), validYaml);
        File.WriteAllText(Path.Combine(tempDir, "invalid.entity.yaml"), invalidYaml);

        try
        {
            // Act
            var results = _loader.LoadFromDirectory(tempDir).ToList();

            // Assert - should still get the valid one
            Assert.Single(results);
            Assert.Equal("valid.type", results[0].Type);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadFromAssembly_NoMatchingResources_ReturnsEmpty()
    {
        // Arrange - use a pattern that won't match anything
        var assembly = Assembly.GetExecutingAssembly();

        // Act
        var results = _loader.LoadFromAssembly(assembly, ".nonexistent.pattern").ToList();

        // Assert
        Assert.Empty(results);
    }
}
