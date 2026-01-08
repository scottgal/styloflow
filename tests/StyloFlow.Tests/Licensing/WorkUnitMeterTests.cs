using Microsoft.Extensions.Logging.Abstractions;
using StyloFlow.Licensing;
using StyloFlow.Licensing.Models;
using StyloFlow.Licensing.Services;
using Xunit;

namespace StyloFlow.Tests.Licensing;

public class WorkUnitMeterTests : IDisposable
{
    private readonly StyloFlowOptions _options;
    private readonly ILicenseManager _licenseManager;
    private WorkUnitMeter? _meter;

    public WorkUnitMeterTests()
    {
        _options = new StyloFlowOptions
        {
            FreeTierMaxWorkUnitsPerMinute = 100,
            WorkUnitWindowSize = TimeSpan.FromMinutes(1),
            WorkUnitWindowBuckets = 60,
            WorkUnitThresholds = new[] { 50, 80, 90, 100 }
        };
        _licenseManager = new FreeTierLicenseManager(_options);
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }

    #region Recording Tests

    [Fact]
    public void Record_IncreasesCurrentWorkUnits()
    {
        // Arrange
        _meter = CreateMeter();

        // Act
        _meter.Record(10.0);

        // Assert
        Assert.Equal(10.0, _meter.CurrentWorkUnits);
    }

    [Fact]
    public void Record_MultipleRecordings_Accumulate()
    {
        // Arrange
        _meter = CreateMeter();

        // Act
        _meter.Record(10.0);
        _meter.Record(20.0);
        _meter.Record(15.0);

        // Assert
        Assert.Equal(45.0, _meter.CurrentWorkUnits);
    }

    [Fact]
    public void Record_ZeroOrNegative_IsIgnored()
    {
        // Arrange
        _meter = CreateMeter();

        // Act
        _meter.Record(10.0);
        _meter.Record(0.0);
        _meter.Record(-5.0);

        // Assert
        Assert.Equal(10.0, _meter.CurrentWorkUnits);
    }

    [Fact]
    public void Record_WithMoleculeType_TracksPerType()
    {
        // Arrange
        _meter = CreateMeter();

        // Act
        _meter.Record(10.0, "documents");
        _meter.Record(20.0, "images");
        _meter.Record(5.0, "documents");

        // Assert
        var snapshot = _meter.GetSnapshot();
        Assert.Equal(15.0, snapshot.ByMoleculeType["documents"]);
        Assert.Equal(20.0, snapshot.ByMoleculeType["images"]);
    }

    #endregion

    #region Throttle Factor Tests

    [Fact]
    public void ThrottleFactor_BelowThreshold_ReturnsOne()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(50.0); // 50% of 100

        // Act & Assert
        Assert.Equal(1.0, _meter.ThrottleFactor);
    }

    [Fact]
    public void ThrottleFactor_At80Percent_ReturnsOne()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(80.0);

        // Act & Assert
        Assert.Equal(1.0, _meter.ThrottleFactor);
    }

    [Fact]
    public void ThrottleFactor_At90Percent_ReturnsHalf()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(90.0);

        // Act & Assert
        Assert.Equal(0.5, _meter.ThrottleFactor, precision: 2);
    }

    [Fact]
    public void ThrottleFactor_At100Percent_ReturnsZero()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(100.0);

        // Act & Assert
        Assert.Equal(0.0, _meter.ThrottleFactor);
    }

    [Fact]
    public void ThrottleFactor_Above100Percent_ReturnsZero()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(150.0);

        // Act & Assert
        Assert.Equal(0.0, _meter.ThrottleFactor);
    }

    #endregion

    #region Threshold Events Tests

    [Fact]
    public void ThresholdCrossed_Event_FiredOnce()
    {
        // Arrange
        _meter = CreateMeter();
        var events = new List<WorkUnitThresholdEvent>();
        _meter.ThresholdCrossed += (_, e) => events.Add(e);

        // Act
        _meter.Record(51.0); // Cross 50%

        // Assert
        Assert.Single(events);
        Assert.Equal(50, events[0].ThresholdPercent);
    }

    [Fact]
    public void ThresholdCrossed_MultipleThresholds_FiredInOrder()
    {
        // Arrange
        _meter = CreateMeter();
        var thresholds = new List<int>();
        _meter.ThresholdCrossed += (_, e) => thresholds.Add(e.ThresholdPercent);

        // Act
        _meter.Record(95.0); // Cross 50%, 80%, 90%

        // Assert
        Assert.Equal(new[] { 50, 80, 90 }, thresholds);
    }

    [Fact]
    public void ThresholdCrossed_SameThreshold_NotRepeated()
    {
        // Arrange
        _meter = CreateMeter();
        var count = 0;
        _meter.ThresholdCrossed += (_, _) => count++;

        // Act
        _meter.Record(30.0);
        _meter.Record(25.0); // Still above 50%

        // Assert
        Assert.Equal(1, count); // Only crossed 50% once
    }

    [Fact]
    public void ThresholdCrossed_Callback_Invoked()
    {
        // Arrange
        var callbackInvoked = false;
        var localOptions = new StyloFlowOptions
        {
            FreeTierMaxWorkUnitsPerMinute = 100,
            WorkUnitThresholds = new[] { 50 },
            OnWorkUnitThreshold = _ => callbackInvoked = true
        };
        _meter = new WorkUnitMeter(localOptions, _licenseManager, NullLogger<WorkUnitMeter>.Instance);

        // Act
        _meter.Record(55.0);

        // Assert
        Assert.True(callbackInvoked);
    }

    #endregion

    #region CanConsume Tests

    [Fact]
    public void CanConsume_BelowLimit_ReturnsTrue()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(50.0);

        // Act & Assert
        Assert.True(_meter.CanConsume(40.0));
    }

    [Fact]
    public void CanConsume_AtLimit_ReturnsTrue()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(50.0);

        // Act & Assert
        Assert.True(_meter.CanConsume(50.0)); // Exactly at 100
    }

    [Fact]
    public void CanConsume_AboveLimit_ReturnsFalse()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(50.0);

        // Act & Assert
        Assert.False(_meter.CanConsume(51.0)); // Would exceed 100
    }

    #endregion

    #region Snapshot Tests

    [Fact]
    public void GetSnapshot_ReturnsAccurateData()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(50.0, "type1");
        _meter.Record(25.0, "type2");

        // Act
        var snapshot = _meter.GetSnapshot();

        // Assert
        Assert.Equal(75.0, snapshot.CurrentWorkUnits);
        Assert.Equal(100.0, snapshot.MaxWorkUnits);
        Assert.Equal(75.0, snapshot.PercentUsed, precision: 1);
        Assert.False(snapshot.IsThrottling);
        Assert.Equal(1.0, snapshot.ThrottleFactor);
        Assert.Equal(2, snapshot.ByMoleculeType.Count);
    }

    [Fact]
    public void GetSnapshot_WindowTimes_AreReasonable()
    {
        // Arrange
        _meter = CreateMeter();
        var before = DateTimeOffset.UtcNow;

        // Act
        var snapshot = _meter.GetSnapshot();
        var after = DateTimeOffset.UtcNow;

        // Assert
        Assert.True(snapshot.WindowEnd >= before && snapshot.WindowEnd <= after);
        var windowDuration = snapshot.WindowEnd - snapshot.WindowStart;
        Assert.True(Math.Abs((windowDuration - _options.WorkUnitWindowSize).TotalSeconds) < 1);
    }

    #endregion

    #region Properties Tests

    [Fact]
    public void MaxWorkUnits_ReturnsLicenseValue()
    {
        // Arrange
        _meter = CreateMeter();

        // Act & Assert
        Assert.Equal(100.0, _meter.MaxWorkUnits);
    }

    [Fact]
    public void PercentUsed_CalculatesCorrectly()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(75.0);

        // Act & Assert
        Assert.Equal(75.0, _meter.PercentUsed);
    }

    [Fact]
    public void IsThrottling_True_At100Percent()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(100.0);

        // Act & Assert
        Assert.True(_meter.IsThrottling);
    }

    [Fact]
    public void IsThrottling_False_Below100Percent()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(99.0);

        // Act & Assert
        Assert.False(_meter.IsThrottling);
    }

    [Fact]
    public void HeadroomRemaining_CalculatesCorrectly()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(60.0);

        // Act & Assert
        Assert.Equal(40.0, _meter.HeadroomRemaining);
    }

    [Fact]
    public void HeadroomRemaining_AtOrAboveLimit_ReturnsZero()
    {
        // Arrange
        _meter = CreateMeter();
        _meter.Record(120.0);

        // Act & Assert
        Assert.Equal(0.0, _meter.HeadroomRemaining);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task Record_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        _meter = CreateMeter();
        var tasks = new List<Task>();
        var iterations = 100;
        var workUnitsPerTask = 1.0;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            tasks.Add(Task.Run(() => _meter.Record(workUnitsPerTask)));
        }
        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(iterations * workUnitsPerTask, _meter.CurrentWorkUnits);
    }

    #endregion

    #region Helper Methods

    private WorkUnitMeter CreateMeter()
    {
        return new WorkUnitMeter(_options, _licenseManager, NullLogger<WorkUnitMeter>.Instance);
    }

    #endregion
}

internal sealed class FreeTierLicenseManager : ILicenseManager
{
    private readonly StyloFlowOptions _options;

    public FreeTierLicenseManager(StyloFlowOptions options)
    {
        _options = options;
    }

    public LicenseState CurrentState => LicenseState.FreeTier;
    public string CurrentTier => "free";
    public int MaxSlots => _options.FreeTierMaxSlots;
    public int MaxWorkUnitsPerMinute => _options.FreeTierMaxWorkUnitsPerMinute;
    public int? MaxNodes => _options.FreeTierMaxNodes;
    public bool IsExpiringSoon => false;
    public TimeSpan TimeUntilExpiry => TimeSpan.Zero;
    public LicenseToken? CurrentLicense => null;
    public IReadOnlyList<string> EnabledFeatures => Array.Empty<string>();

#pragma warning disable CS0067 // Event is never used - required by interface
    public event EventHandler<LicenseStateChangedEvent>? LicenseStateChanged;
#pragma warning restore CS0067

    public Task<LicenseValidationResult> ValidateLicenseAsync(CancellationToken ct = default)
        => Task.FromResult(LicenseValidationResult.Failure("Free tier"));

    public bool HasFeature(string feature) => false;
    public bool MeetsTierRequirement(string requiredTier) => requiredTier == "free";
}
