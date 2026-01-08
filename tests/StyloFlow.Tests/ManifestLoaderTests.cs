using Microsoft.Extensions.Logging;
using StyloFlow.Manifests;
using Xunit;

namespace StyloFlow.Tests;

public class ManifestLoaderTests
{
    private readonly ILogger<FileSystemManifestLoader> _fsLogger;
    private readonly ILogger<EmbeddedManifestLoader> _embeddedLogger;

    public ManifestLoaderTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _fsLogger = loggerFactory.CreateLogger<FileSystemManifestLoader>();
        _embeddedLogger = loggerFactory.CreateLogger<EmbeddedManifestLoader>();
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsManifestFromDirectory()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("SampleDetector", manifest.Name);
        Assert.Equal(50, manifest.Priority);
        Assert.True(manifest.Enabled);
        Assert.Equal("A sample detector for testing", manifest.Description);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsTaxonomy()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("sensor", manifest.Taxonomy.Kind);
        Assert.Equal("deterministic", manifest.Taxonomy.Determinism);
        Assert.Equal("ephemeral", manifest.Taxonomy.Persistence);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsTriggers()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Single(manifest.Triggers.Requires);
        Assert.Equal("request.present", manifest.Triggers.Requires[0].Signal);
        Assert.Single(manifest.Triggers.SkipWhen);
        Assert.Equal("verified.human", manifest.Triggers.SkipWhen[0]);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsEmits()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Single(manifest.Emits.OnStart);
        Assert.Equal("detector.sample.started", manifest.Emits.OnStart[0]);
        Assert.Single(manifest.Emits.OnComplete);
        Assert.Equal("detector.sample.confidence", manifest.Emits.OnComplete[0].Key);
        Assert.Single(manifest.Emits.OnFailure);
        Assert.Equal("detector.sample.failed", manifest.Emits.OnFailure[0]);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsDefaultWeights()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal(1.0, manifest.Defaults.Weights.Base);
        Assert.Equal(1.2, manifest.Defaults.Weights.BotSignal);
        Assert.Equal(0.9, manifest.Defaults.Weights.HumanSignal);
        Assert.Equal(1.5, manifest.Defaults.Weights.Verified);
        Assert.Equal(2.0, manifest.Defaults.Weights.EarlyExit);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsDefaultConfidence()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal(0.0, manifest.Defaults.Confidence.Neutral);
        Assert.Equal(0.3, manifest.Defaults.Confidence.BotDetected);
        Assert.Equal(-0.1, manifest.Defaults.Confidence.HumanIndicated);
        Assert.Equal(0.6, manifest.Defaults.Confidence.StrongSignal);
        Assert.Equal(0.7, manifest.Defaults.Confidence.HighThreshold);
        Assert.Equal(0.2, manifest.Defaults.Confidence.LowThreshold);
        Assert.Equal(0.5, manifest.Defaults.Confidence.EscalationThreshold);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsDefaultTiming()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal(200, manifest.Defaults.Timing.TimeoutMs);
        Assert.Equal(600, manifest.Defaults.Timing.CacheRefreshSec);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsDefaultFeatures()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.True(manifest.Defaults.Features.DetailedLogging);
        Assert.True(manifest.Defaults.Features.EnableCache);
        Assert.True(manifest.Defaults.Features.CanEarlyExit);
        Assert.False(manifest.Defaults.Features.CanEscalate);
    }

    [Fact]
    public void FileSystemManifestLoader_LoadsParameters()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.True(manifest.Defaults.Parameters.ContainsKey("max_retries"));
        Assert.True(manifest.Defaults.Parameters.ContainsKey("patterns"));
        Assert.True(manifest.Defaults.Parameters.ContainsKey("custom_threshold"));
    }

    [Fact]
    public void FileSystemManifestLoader_GetAllManifests_ReturnsAll()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var all = loader.GetAllManifests();

        // Assert
        Assert.NotEmpty(all);
        Assert.True(all.ContainsKey("SampleDetector"));
    }

    [Fact]
    public void FileSystemManifestLoader_ReturnsNullForMissingManifest()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        var manifest = loader.GetManifest("NonExistentDetector");

        // Assert
        Assert.Null(manifest);
    }

    [Fact]
    public void EmbeddedManifestLoader_LoadsFromAssembly()
    {
        // Arrange
        var loader = new EmbeddedManifestLoader(
            _embeddedLogger,
            [typeof(ManifestLoaderTests).Assembly]);

        // Act
        var manifest = loader.GetManifest("EmbeddedDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("EmbeddedDetector", manifest.Name);
        Assert.Equal(100, manifest.Priority);
    }

    [Fact]
    public void EmbeddedManifestLoader_LoadsCorrectTaxonomy()
    {
        // Arrange
        var loader = new EmbeddedManifestLoader(
            _embeddedLogger,
            [typeof(ManifestLoaderTests).Assembly]);

        // Act
        var manifest = loader.GetManifest("EmbeddedDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("aggregator", manifest.Taxonomy.Kind);
        Assert.Equal("probabilistic", manifest.Taxonomy.Determinism);
        Assert.Equal("cached", manifest.Taxonomy.Persistence);
    }

    [Fact]
    public void EmbeddedManifestLoader_LoadsCorrectWeights()
    {
        // Arrange
        var loader = new EmbeddedManifestLoader(
            _embeddedLogger,
            [typeof(ManifestLoaderTests).Assembly]);

        // Act
        var manifest = loader.GetManifest("EmbeddedDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal(2.0, manifest.Defaults.Weights.Base);
        Assert.Equal(2.5, manifest.Defaults.Weights.BotSignal);
    }

    [Fact]
    public async Task FileSystemManifestLoader_ReloadAsync_Works()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(_fsLogger, [testDir]);

        // Act
        await loader.ReloadAsync();
        var manifest = loader.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
    }

    [Fact]
    public async Task EmbeddedManifestLoader_ReloadAsync_Works()
    {
        // Arrange
        var loader = new EmbeddedManifestLoader(
            _embeddedLogger,
            [typeof(ManifestLoaderTests).Assembly]);

        // Act
        await loader.ReloadAsync();
        var manifest = loader.GetManifest("EmbeddedDetector");

        // Assert
        Assert.NotNull(manifest);
    }
}
