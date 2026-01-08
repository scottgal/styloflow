using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mostlylucid.Ephemeral;
using StyloFlow.Licensing;
using StyloFlow.Licensing.Services;
using Xunit;

namespace StyloFlow.Tests.Licensing;

public class StyloFlowServiceExtensionsTests
{
    #region AddStyloFlow Tests

    [Fact]
    public void AddStyloFlow_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlow();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<StyloFlowOptions>());
        Assert.NotNull(provider.GetService<SignalSink>());
        Assert.NotNull(provider.GetService<ILicenseManager>());
        Assert.NotNull(provider.GetService<IWorkUnitMeter>());
    }

    [Fact]
    public void AddStyloFlow_WithConfiguration_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlow(options =>
        {
            options.FreeTierMaxSlots = 25;
            options.HeartbeatInterval = TimeSpan.FromSeconds(10);
        });
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<StyloFlowOptions>();

        // Assert
        Assert.Equal(25, options.FreeTierMaxSlots);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HeartbeatInterval);
    }

    [Fact]
    public void AddStyloFlow_WithLicenseToken_ConfiguresToken()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        const string token = "{\"licenseId\":\"test\"}";

        // Act
        services.AddStyloFlow(token);
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<StyloFlowOptions>();

        // Assert
        Assert.Equal(token, options.LicenseToken);
    }

    [Fact]
    public void AddStyloFlow_LicenseManager_IsLicenseManager()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlow();
        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<ILicenseManager>();

        // Assert
        Assert.IsType<LicenseManager>(manager);
    }

    [Fact]
    public void AddStyloFlow_WorkUnitMeter_IsWorkUnitMeter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlow();
        var provider = services.BuildServiceProvider();
        var meter = provider.GetRequiredService<IWorkUnitMeter>();

        // Assert
        Assert.IsType<WorkUnitMeter>(meter);
    }

    [Fact]
    public void AddStyloFlow_SignalSink_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlow();
        var provider = services.BuildServiceProvider();

        // Assert
        var sink1 = provider.GetRequiredService<SignalSink>();
        var sink2 = provider.GetRequiredService<SignalSink>();
        Assert.Same(sink1, sink2);
    }

    #endregion

    #region AddStyloFlowFree Tests

    [Fact]
    public void AddStyloFlowFree_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlowFree();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<StyloFlowOptions>());
        Assert.NotNull(provider.GetService<SignalSink>());
        Assert.NotNull(provider.GetService<ILicenseManager>());
        Assert.NotNull(provider.GetService<IWorkUnitMeter>());
    }

    [Fact]
    public void AddStyloFlowFree_UsesFreeTierDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlowFree();
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<StyloFlowOptions>();

        // Assert
        Assert.Equal(5, options.FreeTierMaxSlots);
        Assert.Equal(100, options.FreeTierMaxWorkUnitsPerMinute);
        Assert.Equal(1, options.FreeTierMaxNodes);
        Assert.False(options.EnableMesh);
    }

    [Fact]
    public void AddStyloFlowFree_LicenseManager_IsFreeTierManager()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlowFree();
        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<ILicenseManager>();

        // Assert
        Assert.Equal("free", manager.CurrentTier);
        Assert.Equal(5, manager.MaxSlots);
        Assert.False(manager.HasFeature("any.feature"));
    }

    [Fact]
    public void AddStyloFlowFree_WorkUnitMeter_IsNoOp()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlowFree();
        var provider = services.BuildServiceProvider();
        var meter = provider.GetRequiredService<IWorkUnitMeter>();

        // Assert
        meter.Record(1000.0); // Should not throw or track
        Assert.Equal(0, meter.CurrentWorkUnits);
        Assert.Equal(double.MaxValue, meter.MaxWorkUnits);
        Assert.True(meter.CanConsume(double.MaxValue));
    }

    #endregion

    #region AddStyloFlowMesh Tests

    [Fact]
    public void AddStyloFlowMesh_EnablesMeshWithPeers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var peers = new[] { "node1:5200", "node2:5200" };

        // Act
        services.AddStyloFlowMesh(peers);
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<StyloFlowOptions>();

        // Assert
        Assert.True(options.EnableMesh);
        Assert.Equal(peers, options.MeshPeers);
    }

    [Fact]
    public void AddStyloFlowMesh_AllowsAdditionalConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddStyloFlowMesh(new[] { "node1:5200" }, options =>
        {
            options.MeshPort = 6000;
            options.EnableLanDiscovery = true;
        });
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<StyloFlowOptions>();

        // Assert
        Assert.True(options.EnableMesh);
        Assert.Equal(6000, options.MeshPort);
        Assert.True(options.EnableLanDiscovery);
    }

    #endregion

    #region Service Lifetime Tests

    [Fact]
    public void AddStyloFlow_Services_AreSingletons()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStyloFlow();

        // Act
        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        // Assert
        Assert.Same(
            scope1.ServiceProvider.GetService<ILicenseManager>(),
            scope2.ServiceProvider.GetService<ILicenseManager>());
        Assert.Same(
            scope1.ServiceProvider.GetService<IWorkUnitMeter>(),
            scope2.ServiceProvider.GetService<IWorkUnitMeter>());
        Assert.Same(
            scope1.ServiceProvider.GetService<SignalSink>(),
            scope2.ServiceProvider.GetService<SignalSink>());
    }

    [Fact]
    public void AddStyloFlow_DoesNotOverwrite_ExistingSignalSink()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var existingSink = new SignalSink();
        services.AddSingleton(existingSink);

        // Act
        services.AddStyloFlow();
        var provider = services.BuildServiceProvider();

        // Assert
        Assert.Same(existingSink, provider.GetRequiredService<SignalSink>());
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task AddStyloFlow_FullIntegration_ServicesWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStyloFlow(options =>
        {
            options.LicenseToken = CreateValidLicenseJson();
        });
        var provider = services.BuildServiceProvider();

        // Act
        var manager = provider.GetRequiredService<ILicenseManager>();
        var meter = provider.GetRequiredService<IWorkUnitMeter>();
        var result = await manager.ValidateLicenseAsync();

        // Assert
        Assert.True(result.Valid);
        Assert.Equal("professional", manager.CurrentTier);

        meter.Record(10.0, "test");
        Assert.Equal(10.0, meter.CurrentWorkUnits);
    }

    #endregion

    #region Helper Methods

    private static string CreateValidLicenseJson()
    {
        return $$"""
        {
            "licenseId": "test-{{Guid.NewGuid():N}}",
            "issuedTo": "test@example.com",
            "issuedAt": "{{DateTimeOffset.UtcNow:O}}",
            "expiry": "{{DateTimeOffset.UtcNow.AddDays(30):O}}",
            "tier": "professional",
            "features": ["*"],
            "limits": {
                "maxMoleculeSlots": 100,
                "maxWorkUnitsPerMinute": 1000,
                "maxNodes": 10
            }
        }
        """;
    }

    #endregion
}
