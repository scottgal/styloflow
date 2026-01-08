using StyloFlow.Licensing.Models;

namespace StyloFlow.Licensing;

/// <summary>
/// Configuration options for StyloFlow licensing and system coordinator.
/// Everything is code-configurable - no JSON required.
/// </summary>
public sealed class StyloFlowOptions
{
    // ─────────────────────────────────────────────────
    // LICENSE CONFIGURATION
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Path to license file. Optional - can configure license entirely in code.
    /// </summary>
    public string? LicenseFilePath { get; set; }

    /// <summary>
    /// License token (inline JSON/base64). Use this OR LicenseFilePath, not both.
    /// </summary>
    public string? LicenseToken { get; set; }

    /// <summary>
    /// Vendor public key for signature verification (Ed25519).
    /// Can be embedded in the assembly or loaded from options.
    /// </summary>
    public string? VendorPublicKey { get; set; }

    /// <summary>
    /// Override license limits in code (useful for testing/dev).
    /// Takes precedence over loaded license.
    /// </summary>
    public LicenseOverrides? LicenseOverrides { get; set; }

    // ─────────────────────────────────────────────────
    // MESH CONFIGURATION
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Enable mesh networking for multi-node clusters.
    /// Default: false (single-node mode).
    /// </summary>
    public bool EnableMesh { get; set; } = false;

    /// <summary>
    /// Mesh peer endpoints to join on startup (e.g., "node1:5200").
    /// </summary>
    public List<string> MeshPeers { get; set; } = new();

    /// <summary>
    /// Enable LAN discovery for automatic peer finding via multicast.
    /// </summary>
    public bool EnableLanDiscovery { get; set; } = false;

    /// <summary>
    /// Port for mesh gossip protocol.
    /// Default: 5200
    /// </summary>
    public int MeshPort { get; set; } = 5200;

    // ─────────────────────────────────────────────────
    // SYSTEM COORDINATOR
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Heartbeat interval for system health signals.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sliding window size for work unit metering.
    /// Default: 1 minute
    /// </summary>
    public TimeSpan WorkUnitWindowSize { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Number of buckets in the sliding window (granularity).
    /// Default: 60 (1-second buckets for 1-minute window)
    /// </summary>
    public int WorkUnitWindowBuckets { get; set; } = 60;

    /// <summary>
    /// Grace period before revoking leases on license issues.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan LicenseGracePeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Slot lease TTL (time-to-live).
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan SlotLeaseTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often to refresh slot leases (should be < TTL).
    /// Default: 2 minutes
    /// </summary>
    public TimeSpan SlotLeaseRefreshInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Work unit threshold percentages that trigger events.
    /// Default: 80%, 90%, 100%
    /// </summary>
    public int[] WorkUnitThresholds { get; set; } = { 80, 90, 100 };

    // ─────────────────────────────────────────────────
    // FREE TIER DEFAULTS
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Default slot limit for free tier (no license).
    /// Default: 10
    /// </summary>
    public int FreeTierMaxSlots { get; set; } = 10;

    /// <summary>
    /// Default work units per minute for free tier.
    /// Default: 1000
    /// </summary>
    public int FreeTierMaxWorkUnitsPerMinute { get; set; } = 1000;

    /// <summary>
    /// Maximum nodes allowed in free tier.
    /// Default: 3
    /// </summary>
    public int FreeTierMaxNodes { get; set; } = 3;

    // ─────────────────────────────────────────────────
    // EXTENSIBILITY
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Custom license validator (replace default implementation).
    /// Receives the license token string and returns validation result.
    /// </summary>
    public Func<string, CancellationToken, Task<LicenseValidationResult>>? CustomLicenseValidator { get; set; }

    /// <summary>
    /// Custom work unit calculator (replace default implementation).
    /// Receives the operation input and returns work units consumed.
    /// </summary>
    public Func<object, double>? CustomWorkUnitCalculator { get; set; }

    /// <summary>
    /// Callback when license state changes.
    /// </summary>
    public Action<LicenseStateChangedEvent>? OnLicenseStateChanged { get; set; }

    /// <summary>
    /// Callback when work unit threshold is approached.
    /// </summary>
    public Action<WorkUnitThresholdEvent>? OnWorkUnitThreshold { get; set; }

    /// <summary>
    /// Async callback when license state changes (for I/O operations).
    /// </summary>
    public Func<LicenseStateChangedEvent, CancellationToken, Task>? OnLicenseStateChangedAsync { get; set; }
}

/// <summary>
/// Override license limits in code (useful for testing/dev).
/// </summary>
public sealed class LicenseOverrides
{
    /// <summary>
    /// Override max molecule slots.
    /// </summary>
    public int? MaxSlots { get; set; }

    /// <summary>
    /// Override max work units per minute.
    /// </summary>
    public int? MaxWorkUnitsPerMinute { get; set; }

    /// <summary>
    /// Override max nodes.
    /// </summary>
    public int? MaxNodes { get; set; }

    /// <summary>
    /// Override tier.
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>
    /// Override enabled features.
    /// </summary>
    public List<string>? Features { get; set; }

    /// <summary>
    /// Override expiry (for testing).
    /// </summary>
    public DateTimeOffset? Expiry { get; set; }
}
