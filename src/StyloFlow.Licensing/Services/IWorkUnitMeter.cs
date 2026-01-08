using StyloFlow.Licensing.Models;

namespace StyloFlow.Licensing.Services;

/// <summary>
/// Tracks work unit consumption using a sliding window.
/// </summary>
public interface IWorkUnitMeter
{
    /// <summary>
    /// Current work units consumed in the window.
    /// </summary>
    double CurrentWorkUnits { get; }

    /// <summary>
    /// Maximum work units allowed per window.
    /// </summary>
    double MaxWorkUnits { get; }

    /// <summary>
    /// Percentage of limit used (0-100+).
    /// </summary>
    double PercentUsed { get; }

    /// <summary>
    /// Whether the meter is currently throttling (at or over limit).
    /// </summary>
    bool IsThrottling { get; }

    /// <summary>
    /// Throttle factor (1.0 = no throttle, 0.0 = full stop).
    /// </summary>
    double ThrottleFactor { get; }

    /// <summary>
    /// Remaining headroom in current window.
    /// </summary>
    double HeadroomRemaining { get; }

    /// <summary>
    /// Record work units consumed.
    /// </summary>
    /// <param name="workUnits">Number of work units.</param>
    /// <param name="moleculeType">Optional molecule type for breakdown.</param>
    void Record(double workUnits, string? moleculeType = null);

    /// <summary>
    /// Check if consuming work units would exceed the limit.
    /// </summary>
    /// <param name="workUnits">Work units to check.</param>
    /// <returns>True if the operation should be allowed.</returns>
    bool CanConsume(double workUnits);

    /// <summary>
    /// Get current window snapshot.
    /// </summary>
    WorkUnitSnapshot GetSnapshot();

    /// <summary>
    /// Event raised when threshold is crossed.
    /// </summary>
    event EventHandler<WorkUnitThresholdEvent>? ThresholdCrossed;
}

/// <summary>
/// Snapshot of work unit meter state.
/// </summary>
public sealed record WorkUnitSnapshot
{
    public required double CurrentWorkUnits { get; init; }
    public required double MaxWorkUnits { get; init; }
    public required double PercentUsed { get; init; }
    public required bool IsThrottling { get; init; }
    public required double ThrottleFactor { get; init; }
    public required DateTimeOffset WindowStart { get; init; }
    public required DateTimeOffset WindowEnd { get; init; }
    public required IReadOnlyDictionary<string, double> ByMoleculeType { get; init; }
}
