using Mostlylucid.Ephemeral;
using StyloFlow.Licensing.Components;
using StyloFlow.Licensing.Models;
using StyloFlow.Licensing.Services;
using Xunit;

namespace StyloFlow.Tests.Licensing;

public class LicensedComponentBaseTests
{
    #region License Validation Tests

    [Fact]
    public void IsLicensed_FreeTierComponent_ReturnsTrue()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("free");
        var component = new TestComponent(licenseManager, meter, sink, LicenseRequirements.FreeTier);

        // Act & Assert
        Assert.True(component.IsLicensed);
    }

    [Fact]
    public void IsLicensed_StarterTier_FreeLicense_ReturnsFalse()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("free");
        var component = new TestComponent(licenseManager, meter, sink,
            LicenseRequirements.Licensed("starter"));

        // Act & Assert
        Assert.False(component.IsLicensed);
    }

    [Fact]
    public void IsLicensed_StarterTier_StarterLicense_ReturnsTrue()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("starter", new[] { "feature.*" });
        var component = new TestComponent(licenseManager, meter, sink,
            LicenseRequirements.Licensed("starter"));

        // Act & Assert
        Assert.True(component.IsLicensed);
    }

    [Fact]
    public void IsLicensed_RequiresFeature_FeatureMissing_ReturnsFalse()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional", new[] { "other.*" });
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            MinimumTier = "starter",
            RequiredFeatures = new[] { "required.feature" }
        });

        // Act & Assert
        Assert.False(component.IsLicensed);
    }

    [Fact]
    public void IsLicensed_RequiresFeature_FeaturePresent_ReturnsTrue()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional", new[] { "required.*" });
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            MinimumTier = "starter",
            RequiredFeatures = new[] { "required.feature" }
        });

        // Act & Assert
        Assert.True(component.IsLicensed);
    }

    [Fact]
    public void IsLicensed_CachesResult()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, LicenseRequirements.Licensed());

        // Act
        var first = component.IsLicensed;
        var second = component.IsLicensed;

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(1, component.CheckLicenseCallCount);
    }

    #endregion

    #region ValidateLicense Tests

    [Fact]
    public void ValidateLicense_NotLicensed_DegradationAllowed_NoException()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("free");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            MinimumTier = "professional",
            AllowFreeTierDegradation = true
        });

        // Act & Assert - should not throw
        component.CallValidateLicense();
    }

    [Fact]
    public void ValidateLicense_NotLicensed_DegradationNotAllowed_ThrowsException()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("free");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            MinimumTier = "professional",
            AllowFreeTierDegradation = false
        });

        // Act & Assert
        var ex = Assert.Throws<LicenseRequiredException>(() => component.CallValidateLicense());
        Assert.Equal("professional", ex.RequiredTier);
        Assert.Contains("test.component", ex.Message);
    }

    [Fact]
    public void ValidateLicense_Licensed_NoException()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            MinimumTier = "starter",
            AllowFreeTierDegradation = false
        });

        // Act & Assert - should not throw
        component.CallValidateLicense();
    }

    #endregion

    #region Work Unit Recording Tests

    [Fact]
    public void RecordWorkUnits_RecordsBaseAmount()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            WorkUnits = 5.0
        });

        // Act
        component.CallRecordWorkUnits(0);

        // Assert
        Assert.Equal(5.0, meter.RecordedWorkUnits);
        Assert.Equal("test.component", meter.RecordedMoleculeType);
    }

    [Fact]
    public void RecordWorkUnits_WithDataSize_AddsProportional()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            WorkUnits = 2.0,
            WorkUnitsPerKb = 0.5
        });

        // Act - 4KB of data
        component.CallRecordWorkUnits(4096);

        // Assert - 2.0 base + 4 * 0.5 = 4.0
        Assert.Equal(4.0, meter.RecordedWorkUnits);
    }

    [Fact]
    public void CanPerformOperation_BelowLimit_ReturnsTrue()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        meter.SetCanConsume(true);
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            WorkUnits = 5.0
        });

        // Act & Assert
        Assert.True(component.CallCanPerformOperation(0));
    }

    [Fact]
    public void CanPerformOperation_AboveLimit_ReturnsFalse()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        meter.SetCanConsume(false);
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            WorkUnits = 5.0
        });

        // Act & Assert
        Assert.False(component.CallCanPerformOperation(0));
    }

    #endregion

    #region Signal Emission Tests

    [Fact]
    public void EmitSignal_EmitsWithComponentPrefix()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, LicenseRequirements.FreeTier);

        // Act
        component.CallEmitSignal("processed", "abc123");

        // Assert
        Assert.Single(sink.RaisedSignals);
        Assert.Equal("test.component.processed", sink.RaisedSignals[0].Signal);
        Assert.Equal("abc123", sink.RaisedSignals[0].Key);
    }

    [Fact]
    public void EmitModeSignals_Licensed_EmitsLicensedSignals()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, LicenseRequirements.Licensed());
        component.ConfigureLicensedSignals("test.licensed", "test.full");

        // Act
        component.CallEmitModeSignals();

        // Assert
        Assert.Equal(2, sink.RaisedSignals.Count);
        Assert.Contains(sink.RaisedSignals, s => s.Signal == "test.licensed");
        Assert.Contains(sink.RaisedSignals, s => s.Signal == "test.full");
    }

    [Fact]
    public void EmitModeSignals_FreeTier_EmitsFreeTierSignals()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("free");
        var component = new TestComponent(licenseManager, meter, sink, LicenseRequirements.Licensed("starter"));
        component.ConfigureFreeTierSignals("test.degraded", "test.limited");

        // Act
        component.CallEmitModeSignals();

        // Assert
        Assert.Equal(2, sink.RaisedSignals.Count);
        Assert.Contains(sink.RaisedSignals, s => s.Signal == "test.degraded");
        Assert.Contains(sink.RaisedSignals, s => s.Signal == "test.limited");
    }

    #endregion

    #region Signal Configuration Tests

    [Fact]
    public void DeferOnSignals_RequiresSystemCoordinator_IncludesReadySignal()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            RequiresSystemCoordinator = true
        });

        // Act & Assert
        Assert.Contains(SystemCoordinator.Signals.Ready, component.DeferOnSignals);
        Assert.Contains(SystemCoordinator.Signals.Ready, component.ResumeOnSignals);
    }

    [Fact]
    public void DeferOnSignals_NoSystemCoordinator_Empty()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            RequiresSystemCoordinator = false
        });

        // Act & Assert
        Assert.Empty(component.DeferOnSignals);
    }

    [Fact]
    public void AddDeferOnSignal_AddsToList()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, new LicenseRequirements
        {
            RequiresSystemCoordinator = false
        });

        // Act
        component.CallAddDeferOnSignal("custom.signal");

        // Assert
        Assert.Contains("custom.signal", component.DeferOnSignals);
    }

    #endregion

    #region Reset Cache Tests

    [Fact]
    public void ResetLicenseCache_AllowsRecheck()
    {
        // Arrange
        var (licenseManager, meter, sink) = CreateDependencies("professional");
        var component = new TestComponent(licenseManager, meter, sink, LicenseRequirements.Licensed());

        // Act
        _ = component.IsLicensed;
        component.CallResetLicenseCache();
        _ = component.IsLicensed;

        // Assert
        Assert.Equal(2, component.CheckLicenseCallCount);
    }

    #endregion

    #region Requirements Tests

    [Fact]
    public void Requirements_ReturnsConfiguredRequirements()
    {
        // Arrange
        var requirements = new LicenseRequirements
        {
            MinimumTier = "enterprise",
            WorkUnits = 10.0,
            WorkUnitsPerKb = 1.0
        };
        var (licenseManager, meter, sink) = CreateDependencies("enterprise");
        var component = new TestComponent(licenseManager, meter, sink, requirements);

        // Act & Assert
        Assert.Equal("enterprise", component.Requirements.MinimumTier);
        Assert.Equal(10.0, component.Requirements.WorkUnits);
        Assert.Equal(1.0, component.Requirements.WorkUnitsPerKb);
    }

    [Fact]
    public void LicenseRequirements_FreeTier_HasCorrectDefaults()
    {
        // Act
        var requirements = LicenseRequirements.FreeTier;

        // Assert
        Assert.Equal("free", requirements.MinimumTier);
        Assert.False(requirements.RequiresSystemCoordinator);
        Assert.True(requirements.AllowFreeTierDegradation);
    }

    [Fact]
    public void LicenseRequirements_Licensed_HasCorrectDefaults()
    {
        // Act
        var requirements = LicenseRequirements.Licensed("professional", 5.0);

        // Assert
        Assert.Equal("professional", requirements.MinimumTier);
        Assert.Equal(5.0, requirements.WorkUnits);
        Assert.True(requirements.RequiresSystemCoordinator);
        Assert.True(requirements.AllowFreeTierDegradation);
    }

    #endregion

    #region LicenseRequiredException Tests

    [Fact]
    public void LicenseRequiredException_ContainsDetails()
    {
        // Arrange
        var features = new[] { "feature1", "feature2" };

        // Act
        var ex = new LicenseRequiredException("Test message", "professional", features);

        // Assert
        Assert.Equal("Test message", ex.Message);
        Assert.Equal("professional", ex.RequiredTier);
        Assert.Equal(features, ex.RequiredFeatures);
    }

    #endregion

    #region Helper Methods

    private static (MockLicenseManager, MockWorkUnitMeter, TrackedSignalSink) CreateDependencies(
        string tier,
        string[]? features = null)
    {
        var licenseManager = new MockLicenseManager(tier, features ?? Array.Empty<string>());
        var meter = new MockWorkUnitMeter();
        var sink = new TrackedSignalSink();
        return (licenseManager, meter, sink);
    }

    #endregion
}

#region Test Implementations

internal class TestComponent : LicensedComponentBase
{
    public int CheckLicenseCallCount { get; private set; }

    public TestComponent(
        ILicenseManager licenseManager,
        IWorkUnitMeter workUnitMeter,
        SignalSink signalSink,
        LicenseRequirements requirements)
        : base(licenseManager, workUnitMeter, signalSink, requirements)
    {
    }

    public void ConfigureFreeTierSignals(params string[] signals)
    {
        foreach (var s in signals)
            AddFreeTierSignal(s);
    }

    public void ConfigureLicensedSignals(params string[] signals)
    {
        foreach (var s in signals)
            AddLicensedSignal(s);
    }

    public override string ComponentId => "test.component";

    protected override bool CheckLicense()
    {
        CheckLicenseCallCount++;
        return base.CheckLicense();
    }

    public void CallValidateLicense() => ValidateLicense();
    public void CallRecordWorkUnits(long bytes) => RecordWorkUnits(bytes);
    public bool CallCanPerformOperation(long bytes) => CanPerformOperation(bytes);
    public void CallEmitSignal(string suffix, string? key = null) => EmitSignal(suffix, key);
    public void CallEmitModeSignals() => EmitModeSignals();
    public void CallAddDeferOnSignal(string signal) => AddDeferOnSignal(signal);
    public void CallResetLicenseCache() => ResetLicenseCache();
}

internal class MockLicenseManager : ILicenseManager
{
    private readonly string _tier;
    private readonly string[] _features;

    public MockLicenseManager(string tier, string[] features)
    {
        _tier = tier;
        _features = features;
    }

    public LicenseState CurrentState => _tier == "free" ? LicenseState.FreeTier : LicenseState.Valid;
    public string CurrentTier => _tier;
    public int MaxSlots => 100;
    public int MaxWorkUnitsPerMinute => 1000;
    public int? MaxNodes => 10;
    public bool IsExpiringSoon => false;
    public TimeSpan TimeUntilExpiry => TimeSpan.FromDays(30);
    public LicenseToken? CurrentLicense => null;
    public IReadOnlyList<string> EnabledFeatures => _features;

#pragma warning disable CS0067 // Event is never used - required by interface
    public event EventHandler<LicenseStateChangedEvent>? LicenseStateChanged;
#pragma warning restore CS0067

    public Task<LicenseValidationResult> ValidateLicenseAsync(CancellationToken ct = default)
        => Task.FromResult(LicenseValidationResult.Success(null!));

    public bool HasFeature(string feature)
    {
        if (string.IsNullOrEmpty(feature)) return true;
        if (_features.Contains("*")) return true;
        if (_features.Contains(feature)) return true;
        foreach (var f in _features)
        {
            if (f.EndsWith(".*") && feature.StartsWith(f[..^2]))
                return true;
        }
        return false;
    }

    public bool MeetsTierRequirement(string requiredTier)
    {
        var tiers = new[] { "free", "starter", "professional", "enterprise" };
        var currentIdx = Array.IndexOf(tiers, _tier.ToLowerInvariant());
        var requiredIdx = Array.IndexOf(tiers, requiredTier.ToLowerInvariant());
        if (currentIdx < 0) currentIdx = 0;
        if (requiredIdx < 0) requiredIdx = 0;
        return currentIdx >= requiredIdx;
    }
}

internal class MockWorkUnitMeter : IWorkUnitMeter
{
    public double RecordedWorkUnits { get; private set; }
    public string? RecordedMoleculeType { get; private set; }
    private bool _canConsume = true;

    public double CurrentWorkUnits => 0;
    public double MaxWorkUnits => 1000;
    public double PercentUsed => 0;
    public bool IsThrottling => false;
    public double ThrottleFactor => 1.0;
    public double HeadroomRemaining => 1000;

#pragma warning disable CS0067 // Event is never used - required by interface
    public event EventHandler<WorkUnitThresholdEvent>? ThresholdCrossed;
#pragma warning restore CS0067

    public void Record(double workUnits, string? moleculeType = null)
    {
        RecordedWorkUnits = workUnits;
        RecordedMoleculeType = moleculeType;
    }

    public bool CanConsume(double workUnits) => _canConsume;
    public void SetCanConsume(bool value) => _canConsume = value;

    public WorkUnitSnapshot GetSnapshot() => new()
    {
        CurrentWorkUnits = 0,
        MaxWorkUnits = 1000,
        PercentUsed = 0,
        IsThrottling = false,
        ThrottleFactor = 1.0,
        WindowStart = DateTimeOffset.UtcNow,
        WindowEnd = DateTimeOffset.UtcNow,
        ByMoleculeType = new Dictionary<string, double>()
    };
}

internal class TrackedSignalSink
{
    private readonly SignalSink _inner = new();
    public List<(string Signal, string? Key)> RaisedSignals { get; } = new();

    public TrackedSignalSink()
    {
        _inner.Subscribe(evt => RaisedSignals.Add((evt.Signal, evt.Key)));
    }

    // Implicit conversion to SignalSink so it can be passed to LicensedComponentBase
    public static implicit operator SignalSink(TrackedSignalSink tracked) => tracked._inner;
}

#endregion
