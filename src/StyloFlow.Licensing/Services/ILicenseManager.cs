using StyloFlow.Licensing.Models;

namespace StyloFlow.Licensing.Services;

/// <summary>
/// Manages license validation, state, and enforcement.
/// </summary>
public interface ILicenseManager
{
    /// <summary>
    /// Current license state.
    /// </summary>
    LicenseState CurrentState { get; }

    /// <summary>
    /// Current license tier (or "free" if no license).
    /// </summary>
    string CurrentTier { get; }

    /// <summary>
    /// Maximum molecule slots allowed.
    /// </summary>
    int MaxSlots { get; }

    /// <summary>
    /// Maximum work units per minute allowed.
    /// </summary>
    int MaxWorkUnitsPerMinute { get; }

    /// <summary>
    /// Maximum nodes allowed in mesh.
    /// </summary>
    int? MaxNodes { get; }

    /// <summary>
    /// Whether the license is expiring within the grace period.
    /// </summary>
    bool IsExpiringSoon { get; }

    /// <summary>
    /// Time until license expires (or TimeSpan.Zero if expired/no license).
    /// </summary>
    TimeSpan TimeUntilExpiry { get; }

    /// <summary>
    /// Currently loaded license (null if none).
    /// </summary>
    LicenseToken? CurrentLicense { get; }

    /// <summary>
    /// Enabled features.
    /// </summary>
    IReadOnlyList<string> EnabledFeatures { get; }

    /// <summary>
    /// Validate and load a license.
    /// </summary>
    Task<LicenseValidationResult> ValidateLicenseAsync(CancellationToken ct = default);

    /// <summary>
    /// Check if a specific feature is enabled.
    /// </summary>
    bool HasFeature(string feature);

    /// <summary>
    /// Check if a tier is sufficient for the current license.
    /// </summary>
    bool MeetsTierRequirement(string requiredTier);

    /// <summary>
    /// Event raised when license state changes.
    /// </summary>
    event EventHandler<LicenseStateChangedEvent>? LicenseStateChanged;
}
