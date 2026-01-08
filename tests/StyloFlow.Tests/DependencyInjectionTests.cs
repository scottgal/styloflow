using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StyloFlow.Configuration;
using StyloFlow.Manifests;
using Xunit;

namespace StyloFlow.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddStyloFlow_RegistersServices()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStyloFlow([testDir]);

        // Act
        var provider = services.BuildServiceProvider();

        // Assert
        var manifestLoader = provider.GetService<IManifestLoader>();
        var configProvider = provider.GetService<IConfigProvider>();

        Assert.NotNull(manifestLoader);
        Assert.NotNull(configProvider);
        Assert.IsType<FileSystemManifestLoader>(manifestLoader);
    }

    [Fact]
    public void AddStyloFlow_LoadsManifests()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStyloFlow([testDir]);

        // Act
        var provider = services.BuildServiceProvider();
        var configProvider = provider.GetRequiredService<IConfigProvider>();

        // Assert
        var manifest = configProvider.GetManifest("SampleDetector");
        Assert.NotNull(manifest);
        Assert.Equal("SampleDetector", manifest.Name);
    }

    [Fact]
    public void AddStyloFlowFromAssemblies_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStyloFlowFromAssemblies([typeof(DependencyInjectionTests).Assembly]);

        // Act
        var provider = services.BuildServiceProvider();

        // Assert
        var manifestLoader = provider.GetService<IManifestLoader>();
        var configProvider = provider.GetService<IConfigProvider>();

        Assert.NotNull(manifestLoader);
        Assert.NotNull(configProvider);
        Assert.IsType<EmbeddedManifestLoader>(manifestLoader);
    }

    [Fact]
    public void AddStyloFlowFromAssemblies_LoadsEmbeddedManifests()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStyloFlowFromAssemblies([typeof(DependencyInjectionTests).Assembly]);

        // Act
        var provider = services.BuildServiceProvider();
        var configProvider = provider.GetRequiredService<IConfigProvider>();

        // Assert
        var manifest = configProvider.GetManifest("EmbeddedDetector");
        Assert.NotNull(manifest);
        Assert.Equal("EmbeddedDetector", manifest.Name);
    }

    [Fact]
    public void AddStyloFlow_WithConfiguration_IntegratesAppSettings()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["Components:SampleDetector:Weights:Base"] = "9.9"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddStyloFlow([testDir]);

        // Act
        var provider = services.BuildServiceProvider();
        var configProvider = provider.GetRequiredService<IConfigProvider>();
        var defaults = configProvider.GetDefaults("SampleDetector");

        // Assert - appsettings override should apply
        Assert.Equal(9.9, defaults.Weights.Base);
    }

    [Fact]
    public void AddStyloFlow_CustomConfigSection_Works()
    {
        // Arrange
        var configDict = new Dictionary<string, string?>
        {
            ["CustomSection:SampleDetector:Weights:Base"] = "3.3"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();

        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddStyloFlow([testDir], configSectionPath: "CustomSection");

        // Act
        var provider = services.BuildServiceProvider();
        var configProvider = provider.GetRequiredService<IConfigProvider>();
        var defaults = configProvider.GetDefaults("SampleDetector");

        // Assert
        Assert.Equal(3.3, defaults.Weights.Base);
    }

    [Fact]
    public void AddStyloFlow_CustomManifestLoader_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStyloFlow(_ => new TestManifestLoader());

        // Act
        var provider = services.BuildServiceProvider();
        var manifestLoader = provider.GetRequiredService<IManifestLoader>();
        var configProvider = provider.GetRequiredService<IConfigProvider>();

        // Assert
        Assert.IsType<TestManifestLoader>(manifestLoader);
        var manifest = configProvider.GetManifest("TestManifest");
        Assert.NotNull(manifest);
    }

    [Fact]
    public void AddStyloFlow_DoesNotOverwriteExistingRegistrations()
    {
        // Arrange
        var testDir = Path.Combine(AppContext.BaseDirectory, "TestManifests");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IManifestLoader>(new TestManifestLoader());
        services.AddStyloFlow([testDir]);

        // Act
        var provider = services.BuildServiceProvider();
        var manifestLoader = provider.GetRequiredService<IManifestLoader>();

        // Assert - existing registration should be preserved
        Assert.IsType<TestManifestLoader>(manifestLoader);
    }

    private class TestManifestLoader : IManifestLoader
    {
        private readonly Dictionary<string, ComponentManifest> _manifests = new()
        {
            ["TestManifest"] = new ComponentManifest
            {
                Name = "TestManifest",
                Priority = 1,
                Enabled = true
            }
        };

        public ComponentManifest? GetManifest(string componentName)
        {
            return _manifests.TryGetValue(componentName, out var manifest) ? manifest : null;
        }

        public IReadOnlyDictionary<string, ComponentManifest> GetAllManifests()
        {
            return _manifests;
        }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
