using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StyloFlow.Configuration;
using StyloFlow.Manifests;
using Xunit;

namespace StyloFlow.Tests;

public class ConfigProviderTests
{
    private readonly ILogger<FileSystemManifestLoader> _fsLogger;
    private readonly ILogger<ConfigProvider> _configLogger;
    private readonly string _testDir;

    public ConfigProviderTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _fsLogger = loggerFactory.CreateLogger<FileSystemManifestLoader>();
        _configLogger = loggerFactory.CreateLogger<ConfigProvider>();
        _testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
    }

    [Fact]
    public void ConfigProvider_GetManifest_ReturnsManifest()
    {
        // Arrange
        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger);

        // Act
        var manifest = provider.GetManifest("SampleDetector");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("SampleDetector", manifest.Name);
    }

    [Fact]
    public void ConfigProvider_GetDefaults_ReturnsDefaultsFromYaml()
    {
        // Arrange
        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger);

        // Act
        var defaults = provider.GetDefaults("SampleDetector");

        // Assert
        Assert.Equal(1.0, defaults.Weights.Base);
        Assert.Equal(1.2, defaults.Weights.BotSignal);
        Assert.Equal(0.3, defaults.Confidence.BotDetected);
        Assert.Equal(200, defaults.Timing.TimeoutMs);
        Assert.True(defaults.Features.DetailedLogging);
    }

    [Fact]
    public void ConfigProvider_GetDefaults_ReturnsCodeDefaultsForMissingManifest()
    {
        // Arrange
        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger);

        // Act
        var defaults = provider.GetDefaults("NonExistentDetector");

        // Assert - should return code defaults
        Assert.Equal(1.0, defaults.Weights.Base);
        Assert.Equal(0.0, defaults.Confidence.Neutral);
        Assert.Equal(100, defaults.Timing.TimeoutMs);
        Assert.False(defaults.Features.DetailedLogging);
    }

    [Fact]
    public void ConfigProvider_GetParameter_ReturnsYamlValue()
    {
        // Arrange
        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger);

        // Act
        var maxRetries = provider.GetParameter("SampleDetector", "max_retries", 1);
        var threshold = provider.GetParameter("SampleDetector", "custom_threshold", 0.5);

        // Assert
        Assert.Equal(3, maxRetries);
        Assert.Equal(0.65, threshold);
    }

    [Fact]
    public void ConfigProvider_GetParameter_ReturnsDefaultForMissingParameter()
    {
        // Arrange
        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger);

        // Act
        var value = provider.GetParameter("SampleDetector", "nonexistent", 42);

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public void ConfigProvider_WithAppSettings_OverridesYaml()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["Components:SampleDetector:Weights:Base"] = "5.0",
            ["Components:SampleDetector:Confidence:BotDetected"] = "0.9",
            ["Components:SampleDetector:Timing:TimeoutMs"] = "999",
            ["Components:SampleDetector:Features:DetailedLogging"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger, configuration);

        // Act
        var defaults = provider.GetDefaults("SampleDetector");

        // Assert - appsettings should override YAML
        Assert.Equal(5.0, defaults.Weights.Base);
        Assert.Equal(0.9, defaults.Confidence.BotDetected);
        Assert.Equal(999, defaults.Timing.TimeoutMs);
        Assert.False(defaults.Features.DetailedLogging);
    }

    [Fact]
    public void ConfigProvider_WithAppSettings_OverridesParameters()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["Components:SampleDetector:Parameters:max_retries"] = "10",
            ["Components:SampleDetector:Parameters:custom_threshold"] = "0.99"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger, configuration);

        // Act
        var maxRetries = provider.GetParameter("SampleDetector", "max_retries", 1);
        var threshold = provider.GetParameter("SampleDetector", "custom_threshold", 0.5);

        // Assert
        Assert.Equal(10, maxRetries);
        Assert.Equal(0.99, threshold);
    }

    [Fact]
    public void ConfigProvider_GetAllManifests_ReturnsAll()
    {
        // Arrange
        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger);

        // Act
        var all = provider.GetAllManifests();

        // Assert
        Assert.NotEmpty(all);
    }

    [Fact]
    public void ConfigProvider_CachesDefaults()
    {
        // Arrange
        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger);

        // Act
        var defaults1 = provider.GetDefaults("SampleDetector");
        var defaults2 = provider.GetDefaults("SampleDetector");

        // Assert - should be same reference (cached)
        Assert.Same(defaults1, defaults2);
    }

    [Fact]
    public void ConfigProvider_CustomConfigSection_Works()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["MySection:SampleDetector:Weights:Base"] = "7.0"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var loader = new FileSystemManifestLoader(_fsLogger, [_testDir]);
        var provider = new ConfigProvider(loader, _configLogger, configuration, "MySection");

        // Act
        var defaults = provider.GetDefaults("SampleDetector");

        // Assert
        Assert.Equal(7.0, defaults.Weights.Base);
    }
}
