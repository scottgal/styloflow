namespace StyloFlow.Licensing.Models;

/// <summary>
/// Represents a signed license token with all claims.
/// </summary>
public sealed record LicenseToken
{
    /// <summary>
    /// Unique license identifier.
    /// </summary>
    public required string LicenseId { get; init; }

    /// <summary>
    /// License holder (email or organization).
    /// </summary>
    public required string IssuedTo { get; init; }

    /// <summary>
    /// When the license was issued.
    /// </summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// When the license expires.
    /// </summary>
    public required DateTimeOffset Expiry { get; init; }

    /// <summary>
    /// License capacity limits.
    /// </summary>
    public required LicenseLimits Limits { get; init; }

    /// <summary>
    /// Enabled feature flags.
    /// </summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>
    /// License tier: free, starter, professional, enterprise.
    /// </summary>
    public required string Tier { get; init; }

    /// <summary>
    /// Vendor signature for verification.
    /// </summary>
    public string? Signature { get; init; }
}

/// <summary>
/// License capacity limits.
/// </summary>
public sealed record LicenseLimits
{
    /// <summary>
    /// Maximum nodes in mesh cluster. Null = unlimited.
    /// </summary>
    public int? MaxNodes { get; init; }

    /// <summary>
    /// Maximum concurrent molecule instances (slots).
    /// </summary>
    public required int MaxMoleculeSlots { get; init; }

    /// <summary>
    /// Maximum work units per minute (rate limit).
    /// </summary>
    public required int MaxWorkUnitsPerMinute { get; init; }
}

/// <summary>
/// Result of license validation.
/// </summary>
public sealed record LicenseValidationResult
{
    /// <summary>
    /// Whether the license is valid.
    /// </summary>
    public required bool Valid { get; init; }

    /// <summary>
    /// License tier if valid.
    /// </summary>
    public string? Tier { get; init; }

    /// <summary>
    /// Validation error message if invalid.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The validated license token (if valid).
    /// </summary>
    public LicenseToken? License { get; init; }

    public static LicenseValidationResult Success(LicenseToken license) => new()
    {
        Valid = true,
        Tier = license.Tier,
        License = license
    };

    public static LicenseValidationResult Failure(string error) => new()
    {
        Valid = false,
        ErrorMessage = error
    };
}

/// <summary>
/// Event raised when license state changes.
/// </summary>
public sealed record LicenseStateChangedEvent
{
    public required LicenseState PreviousState { get; init; }
    public required LicenseState NewState { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Current license state.
/// </summary>
public enum LicenseState
{
    /// <summary>
    /// No license loaded yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// License is valid and active.
    /// </summary>
    Valid,

    /// <summary>
    /// License is expiring soon (within grace period).
    /// </summary>
    ExpiringSoon,

    /// <summary>
    /// License has expired.
    /// </summary>
    Expired,

    /// <summary>
    /// License signature is invalid.
    /// </summary>
    Invalid,

    /// <summary>
    /// Running in free tier (no license or degraded).
    /// </summary>
    FreeTier
}

/// <summary>
/// Event raised when work unit threshold is approached.
/// </summary>
public sealed record WorkUnitThresholdEvent
{
    /// <summary>
    /// Current work units consumed in the window.
    /// </summary>
    public required double CurrentWorkUnits { get; init; }

    /// <summary>
    /// Maximum work units allowed.
    /// </summary>
    public required double MaxWorkUnits { get; init; }

    /// <summary>
    /// Percentage of limit used (0-100+).
    /// </summary>
    public double PercentUsed => MaxWorkUnits > 0 ? (CurrentWorkUnits / MaxWorkUnits) * 100 : 0;

    /// <summary>
    /// Threshold that triggered this event (e.g., 80, 90, 100).
    /// </summary>
    public required int ThresholdPercent { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Slot lease for running a molecule instance.
/// </summary>
public sealed record SlotLease
{
    /// <summary>
    /// Unique slot identifier.
    /// </summary>
    public required string SlotId { get; init; }

    /// <summary>
    /// Molecule type this slot is for.
    /// </summary>
    public required string MoleculeType { get; init; }

    /// <summary>
    /// Node holding this lease.
    /// </summary>
    public required string HolderNodeId { get; init; }

    /// <summary>
    /// When the lease was issued.
    /// </summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// When the lease expires (short TTL).
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Lease Authority node that issued this lease.
    /// </summary>
    public required string LeaseAuthorityNodeId { get; init; }

    /// <summary>
    /// Signature from Lease Authority.
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// Whether this lease is still valid.
    /// </summary>
    public bool IsValid => DateTimeOffset.UtcNow < ExpiresAt;

    /// <summary>
    /// Time remaining on this lease.
    /// </summary>
    public TimeSpan TimeRemaining => ExpiresAt - DateTimeOffset.UtcNow;
}

/// <summary>
/// Work unit consumption report from a node.
/// </summary>
public sealed record WorkUnitReport
{
    /// <summary>
    /// Reporting node ID.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Start of the reporting window.
    /// </summary>
    public required DateTimeOffset WindowStart { get; init; }

    /// <summary>
    /// End of the reporting window.
    /// </summary>
    public required DateTimeOffset WindowEnd { get; init; }

    /// <summary>
    /// Total work units consumed in window.
    /// </summary>
    public required double WorkUnitsConsumed { get; init; }

    /// <summary>
    /// Breakdown by molecule type.
    /// </summary>
    public IReadOnlyDictionary<string, double> ByMoleculeType { get; init; } =
        new Dictionary<string, double>();

    /// <summary>
    /// Node signature for verification.
    /// </summary>
    public string? Signature { get; init; }
}
