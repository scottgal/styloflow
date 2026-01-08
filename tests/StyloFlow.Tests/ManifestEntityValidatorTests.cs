using Microsoft.Extensions.Logging;
using StyloFlow.Entities;
using StyloFlow.Manifests;
using Xunit;

namespace StyloFlow.Tests;

public class ManifestEntityValidatorTests
{
    private readonly EntityTypeRegistry _registry;
    private readonly ManifestEntityValidator _validator;

    public ManifestEntityValidatorTests()
    {
        _registry = new EntityTypeRegistry();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _validator = new ManifestEntityValidator(
            _registry,
            loggerFactory.CreateLogger<ManifestEntityValidator>());
    }

    [Fact]
    public void Validate_EmptyManifest_IsValid()
    {
        // Arrange
        var manifest = new ComponentManifest { Name = "TestComponent" };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithKnownInputType_IsValid()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Input = new InputContract
            {
                Accepts =
                [
                    new EntityTypeSpec { Type = "http.request", Required = true }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_WithUnknownInputType_HasWarning()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Input = new InputContract
            {
                Accepts =
                [
                    new EntityTypeSpec { Type = "unknown.type", Required = true }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid); // Warnings don't cause invalid
        Assert.Empty(result.Errors);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("unknown.type"));
    }

    [Fact]
    public void Validate_WithEmptyType_HasError()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Input = new InputContract
            {
                Accepts =
                [
                    new EntityTypeSpec { Type = "", Required = true }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("empty type"));
    }

    [Fact]
    public void Validate_WithKnownOutputType_IsValid()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Output = new OutputContract
            {
                Produces =
                [
                    new EntityTypeSpec { Type = "detection.contribution" }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithPrimitiveSignalType_IsValid()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Output = new OutputContract
            {
                Signals =
                [
                    new SignalSpec { Key = "test.signal", EntityType = "number" },
                    new SignalSpec { Key = "test.flag", EntityType = "bool" },
                    new SignalSpec { Key = "test.name", EntityType = "string" }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_WithWildcardType_MatchesRegistered()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Input = new InputContract
            {
                Accepts =
                [
                    new EntityTypeSpec { Type = "image.*", Required = true }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings); // image.* should match built-in types
    }

    [Fact]
    public void Validate_WithUnmatchedWildcard_HasWarning()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Input = new InputContract
            {
                Accepts =
                [
                    new EntityTypeSpec { Type = "nonexistent.*", Required = true }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid); // Warnings don't cause invalid
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("nonexistent.*"));
    }

    [Fact]
    public void Validate_ConstraintExceedsDefault_HasWarning()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Input = new InputContract
            {
                Accepts =
                [
                    new EntityTypeSpec
                    {
                        Type = "image.*",
                        Constraints = new EntityConstraints
                        {
                            MaxSizeBytes = 100 * 1024 * 1024 // 100MB, exceeds 50MB default
                        }
                    }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("exceeds"));
    }

    [Fact]
    public void Validate_MultipleInputsAndOutputs_ValidatesAll()
    {
        // Arrange
        var manifest = new ComponentManifest
        {
            Name = "TestComponent",
            Input = new InputContract
            {
                Accepts =
                [
                    new EntityTypeSpec { Type = "http.request" },
                    new EntityTypeSpec { Type = "behavioral.signature" }
                ]
            },
            Output = new OutputContract
            {
                Produces =
                [
                    new EntityTypeSpec { Type = "detection.contribution" },
                    new EntityTypeSpec { Type = "data.json" }
                ],
                Signals =
                [
                    new SignalSpec { Key = "confidence", EntityType = "double" }
                ]
            }
        };

        // Act
        var result = _validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ManifestValidationResult_DefaultValues()
    {
        // Arrange & Act
        var result = new ManifestValidationResult();

        // Assert
        Assert.Equal("", result.ManifestName);
        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }
}
