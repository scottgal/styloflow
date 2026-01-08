using Microsoft.Extensions.Logging;
using StyloFlow.Configuration;
using StyloFlow.Manifests;
using StyloFlow.Orchestration;
using Xunit;

namespace StyloFlow.Tests;

public class ConfiguredComponentBaseTests
{
    private readonly IConfigProvider _configProvider;

    public ConfiguredComponentBaseTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var fsLogger = loggerFactory.CreateLogger<FileSystemManifestLoader>();
        var configLogger = loggerFactory.CreateLogger<ConfigProvider>();

        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var loader = new FileSystemManifestLoader(fsLogger, [testDir]);
        _configProvider = new ConfigProvider(loader, configLogger);
    }

    [Fact]
    public void ConfiguredComponentBase_WeightShortcuts_Work()
    {
        // Arrange
        var component = new TestComponent(_configProvider);

        // Act & Assert
        Assert.Equal(1.0, component.GetWeightBase());
        Assert.Equal(1.2, component.GetWeightBotSignal());
        Assert.Equal(0.9, component.GetWeightHumanSignal());
        Assert.Equal(1.5, component.GetWeightVerified());
        Assert.Equal(2.0, component.GetWeightEarlyExit());
    }

    [Fact]
    public void ConfiguredComponentBase_ConfidenceShortcuts_Work()
    {
        // Arrange
        var component = new TestComponent(_configProvider);

        // Act & Assert
        Assert.Equal(0.0, component.GetConfidenceNeutral());
        Assert.Equal(0.3, component.GetConfidenceBotDetected());
        Assert.Equal(-0.1, component.GetConfidenceHumanIndicated());
        Assert.Equal(0.6, component.GetConfidenceStrongSignal());
        Assert.Equal(0.7, component.GetConfidenceHighThreshold());
        Assert.Equal(0.2, component.GetConfidenceLowThreshold());
        Assert.Equal(0.5, component.GetConfidenceEscalationThreshold());
    }

    [Fact]
    public void ConfiguredComponentBase_TimingShortcuts_Work()
    {
        // Arrange - use SampleDetectorComponent which has a manifest
        var component = new SampleDetectorComponent(_configProvider);

        // Act & Assert
        Assert.Equal(200, component.GetTimeoutMs());
        Assert.Equal(600, component.GetCacheRefreshSec());
    }

    [Fact]
    public void ConfiguredComponentBase_FeatureShortcuts_Work()
    {
        // Arrange - use SampleDetectorComponent which has a manifest
        var component = new SampleDetectorComponent(_configProvider);

        // Act & Assert
        Assert.True(component.GetDetailedLogging());
        Assert.True(component.GetCacheEnabled());
        Assert.True(component.GetCanEarlyExit());
        Assert.False(component.GetCanEscalate());
    }

    [Fact]
    public void ConfiguredComponentBase_GetParam_ReturnsYamlValue()
    {
        // Arrange - use SampleDetectorComponent which has a manifest
        var component = new SampleDetectorComponent(_configProvider);

        // Act
        var maxRetries = component.GetParamPublic("max_retries", 1);
        var threshold = component.GetParamPublic("custom_threshold", 0.5);

        // Assert
        Assert.Equal(3, maxRetries);
        Assert.Equal(0.65, threshold);
    }

    [Fact]
    public void ConfiguredComponentBase_GetParam_ReturnsDefaultForMissing()
    {
        // Arrange
        var component = new TestComponent(_configProvider);

        // Act
        var value = component.GetParamPublic("nonexistent", 42);

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void ConfiguredComponentBase_GetStringListParam_Works()
    {
        // Arrange - use SampleDetectorComponent which has a manifest with patterns
        var component = new SampleDetectorComponent(_configProvider);

        // Act
        var patterns = component.GetStringListParamPublic("patterns");

        // Assert
        Assert.NotNull(patterns);
        Assert.Equal(2, patterns.Count);
        Assert.Contains("pattern1", patterns);
        Assert.Contains("pattern2", patterns);
    }

    [Fact]
    public void ConfiguredComponentBase_GetStringListParam_ReturnsEmptyForMissing()
    {
        // Arrange
        var component = new TestComponent(_configProvider);

        // Act
        var patterns = component.GetStringListParamPublic("nonexistent_list");

        // Assert
        Assert.NotNull(patterns);
        Assert.Empty(patterns);
    }

    [Fact]
    public void ConfiguredComponentBase_IsFeatureEnabled_Works()
    {
        // Arrange
        var component = new TestComponent(_configProvider);

        // Act
        var enabled = component.IsFeatureEnabledPublic("some_feature");
        var disabled = component.IsFeatureEnabledPublic("nonexistent_feature");

        // Assert
        Assert.False(enabled); // Not in YAML
        Assert.False(disabled);
    }

    [Fact]
    public void ConfiguredComponentBase_ManifestName_DefaultsToTypeName()
    {
        // Arrange
        var component = new TestComponent(_configProvider);

        // Act
        var name = component.GetManifestName();

        // Assert
        Assert.Equal("TestComponent", name);
    }

    [Fact]
    public void ConfiguredComponentBase_CustomManifestName_Works()
    {
        // Arrange - SampleDetectorComponent maps to SampleDetector
        var component = new SampleDetectorComponent(_configProvider);

        // Act & Assert
        Assert.Equal(1.0, component.GetWeightBase());
        Assert.Equal(200, component.GetTimeoutMs());
    }

    [Fact]
    public void ConfiguredComponentBase_Manifest_IsAccessible()
    {
        // Arrange
        var component = new SampleDetectorComponent(_configProvider);

        // Act
        var manifest = component.GetManifest();

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("SampleDetector", manifest.Name);
        Assert.Equal(50, manifest.Priority);
    }

    [Fact]
    public void ConfiguredComponentBase_Config_IsCached()
    {
        // Arrange
        var component = new TestComponent(_configProvider);

        // Act
        var config1 = component.GetConfig();
        var config2 = component.GetConfig();

        // Assert
        Assert.Same(config1, config2);
    }

    // Test component that exposes protected members for testing
    private class TestComponent : ConfiguredComponentBase
    {
        public TestComponent(IConfigProvider configProvider)
            : base(configProvider) { }

        // No matching manifest, should get code defaults
        public string GetManifestName() => ManifestName;
        public ComponentDefaults GetConfig() => Config;

        public double GetWeightBase() => WeightBase;
        public double GetWeightBotSignal() => WeightBotSignal;
        public double GetWeightHumanSignal() => WeightHumanSignal;
        public double GetWeightVerified() => WeightVerified;
        public double GetWeightEarlyExit() => WeightEarlyExit;

        public double GetConfidenceNeutral() => ConfidenceNeutral;
        public double GetConfidenceBotDetected() => ConfidenceBotDetected;
        public double GetConfidenceHumanIndicated() => ConfidenceHumanIndicated;
        public double GetConfidenceStrongSignal() => ConfidenceStrongSignal;
        public double GetConfidenceHighThreshold() => ConfidenceHighThreshold;
        public double GetConfidenceLowThreshold() => ConfidenceLowThreshold;
        public double GetConfidenceEscalationThreshold() => ConfidenceEscalationThreshold;

        public int GetTimeoutMs() => TimeoutMs;
        public int GetCacheRefreshSec() => CacheRefreshSec;

        public bool GetDetailedLogging() => DetailedLogging;
        public bool GetCacheEnabled() => CacheEnabled;
        public bool GetCanEarlyExit() => CanEarlyExit;
        public bool GetCanEscalate() => CanEscalate;

        public T GetParamPublic<T>(string name, T defaultValue) => GetParam(name, defaultValue);
        public IReadOnlyList<string> GetStringListParamPublic(string name) => GetStringListParam(name);
        public bool IsFeatureEnabledPublic(string name) => IsFeatureEnabled(name);
    }

    // Component that maps to SampleDetector manifest
    private class SampleDetectorComponent : ConfiguredComponentBase
    {
        public SampleDetectorComponent(IConfigProvider configProvider)
            : base(configProvider) { }

        public override string ManifestName => "SampleDetector";

        public double GetWeightBase() => WeightBase;
        public int GetTimeoutMs() => TimeoutMs;
        public int GetCacheRefreshSec() => CacheRefreshSec;
        public bool GetDetailedLogging() => DetailedLogging;
        public bool GetCacheEnabled() => CacheEnabled;
        public bool GetCanEarlyExit() => CanEarlyExit;
        public bool GetCanEscalate() => CanEscalate;
        public ComponentManifest? GetManifest() => Manifest;
        public T GetParamPublic<T>(string name, T defaultValue) => GetParam(name, defaultValue);
        public IReadOnlyList<string> GetStringListParamPublic(string name) => GetStringListParam(name);
    }
}
