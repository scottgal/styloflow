# StyloFlow.Licensing

Comprehensive licensing system for StyloFlow with tiered features, work unit metering, signal-based coordination, and mesh networking support.

## Installation

```bash
dotnet add package Mostlylucid.StyloFlow.Licensing
```

## Quick Start

### Full Licensing Mode

```csharp
services.AddStyloFlow(options =>
{
    options.LicenseToken = licenseJson;
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.OnLicenseStateChanged = evt =>
    {
        Console.WriteLine($"License: {evt.PreviousState} -> {evt.NewState}");
    };
});
```

### Free Tier Mode (Development/Testing)

```csharp
services.AddStyloFlowFree();
```

### Mesh Mode (Distributed Deployments)

```csharp
services.AddStyloFlowMesh(
    meshPeers: new[] { "node1:5200", "node2:5200" },
    configure: options =>
    {
        options.EnableLanDiscovery = true;
    });
```

## Core Components

### ILicenseManager

Manages license validation, state, and enforcement.

```csharp
public interface ILicenseManager
{
    LicenseState CurrentState { get; }      // Unknown, Valid, ExpiringSoon, Expired, Invalid, FreeTier
    string CurrentTier { get; }             // free, starter, professional, enterprise
    int MaxSlots { get; }                   // Maximum concurrent molecule instances
    int MaxWorkUnitsPerMinute { get; }      // Rate limit
    bool IsExpiringSoon { get; }
    TimeSpan TimeUntilExpiry { get; }
    IReadOnlyList<string> EnabledFeatures { get; }

    Task<LicenseValidationResult> ValidateLicenseAsync(CancellationToken ct = default);
    bool HasFeature(string feature);         // Supports wildcards: "documents.*"
    bool MeetsTierRequirement(string tier);

    event EventHandler<LicenseStateChangedEvent> LicenseStateChanged;
}
```

**Feature Matching:**
- Exact match: `"documents.parse"` matches `["documents.parse"]`
- Wildcard: `"documents.parse"` matches `["documents.*"]`
- Global wildcard: Any feature matches `["*"]`

**Tier Hierarchy:**
```
free < starter < professional < enterprise
```

### IWorkUnitMeter

Tracks work unit consumption with sliding window metering.

```csharp
public interface IWorkUnitMeter
{
    double CurrentWorkUnits { get; }        // Current consumption in window
    double MaxWorkUnits { get; }            // Limit from license
    double PercentUsed { get; }             // 0-100+
    bool IsThrottling { get; }              // true when at or above 100%
    double ThrottleFactor { get; }          // 1.0 (full speed) to 0.0 (throttled)
    double HeadroomRemaining { get; }       // Units available before limit

    void Record(double workUnits, string? moleculeType = null);
    bool CanConsume(double workUnits);
    WorkUnitSnapshot GetSnapshot();

    event EventHandler<WorkUnitThresholdEvent> ThresholdCrossed;
}
```

**Throttle Factor Calculation:**
- Below 80%: Factor = 1.0 (full speed)
- 80-100%: Linear ramp down from 1.0 to 0.0
- At/above 100%: Factor = 0.0 (fully throttled)

### SystemCoordinator

Background service that manages license validation, heartbeat, and signal emission.

**Emitted Signals:**

| Signal | Key | When |
|--------|-----|------|
| `styloflow.system.ready` | - | System initialized |
| `styloflow.system.heartbeat` | Unix timestamp | Every heartbeat interval |
| `styloflow.system.license.valid` | - | License validated |
| `styloflow.system.license.revoked` | - | License revoked/invalid |
| `styloflow.system.license.expires_soon` | - | Within grace period |
| `styloflow.system.license.tier.{tier}` | - | Current tier |
| `styloflow.system.license.slots` | count | Available slots |
| `styloflow.system.license.workunit_limit` | limit | Work unit limit |
| `styloflow.system.slots.available` | count | Current available slots |
| `styloflow.system.workunit.rate` | rate | Current work unit rate |
| `styloflow.system.workunit.throttling` | - | When throttling starts |
| `styloflow.system.mesh.mode.standalone` | - | Single-node mode |
| `styloflow.system.mesh.mode.cluster` | - | Cluster mode |

## Licensed Components

### LicenseRequirements

Define what a component needs to operate:

```csharp
public sealed record LicenseRequirements
{
    public string MinimumTier { get; init; } = "free";
    public IReadOnlyList<string> RequiredFeatures { get; init; } = Array.Empty<string>();
    public double WorkUnits { get; init; } = 1.0;
    public double WorkUnitsPerKb { get; init; } = 0.0;
    public bool RequiresSystemCoordinator { get; init; } = true;
    public bool AllowFreeTierDegradation { get; init; } = true;

    // Factory methods
    public static LicenseRequirements FreeTier => new() { ... };
    public static LicenseRequirements Licensed(string tier = "starter", double workUnits = 1.0) => new() { ... };
}
```

### LicensedComponentBase

Base class for license-aware components:

```csharp
public class MyComponent : LicensedComponentBase
{
    public MyComponent(
        ILicenseManager licenseManager,
        IWorkUnitMeter workUnitMeter,
        SignalSink signalSink)
        : base(licenseManager, workUnitMeter, signalSink, new LicenseRequirements
        {
            MinimumTier = "starter",
            WorkUnits = 2.0,
            WorkUnitsPerKb = 0.5,
            RequiredFeatures = new[] { "myfeature.*" },
            AllowFreeTierDegradation = true
        })
    {
        // Configure signals
        AddLicensedSignal("mycomponent.ready");
        AddFreeTierSignal("mycomponent.degraded");
    }

    public override string ComponentId => "myapp.mycomponent";

    public async Task<Result> ProcessAsync(byte[] data)
    {
        // Validate license (throws if not licensed and degradation not allowed)
        ValidateLicense();

        // Check if we have capacity
        if (!CanPerformOperation(data.Length))
        {
            return Result.Throttled();
        }

        // Emit mode signals (licensed or free tier)
        EmitModeSignals();

        // Do work...
        var result = await DoWorkAsync(data);

        // Record consumption
        RecordWorkUnits(data.Length);

        // Emit completion signal
        EmitSignal("completed", data.Length.ToString());

        return result;
    }
}
```

**Protected Methods:**

| Method | Description |
|--------|-------------|
| `ValidateLicense()` | Throws `LicenseRequiredException` if not licensed and degradation not allowed |
| `CanPerformOperation(bytes)` | Check if work units are available |
| `RecordWorkUnits(bytes)` | Record work unit consumption |
| `EmitSignal(suffix, key?)` | Emit signal prefixed with ComponentId |
| `EmitModeSignals()` | Emit free tier or licensed signals |
| `AddFreeTierSignal(signal)` | Register signal for free tier mode |
| `AddLicensedSignal(signal)` | Register signal for licensed mode |
| `AddDeferOnSignal(signal)` | Register signal to wait for |
| `ResetLicenseCache()` | Force re-check of license status |

## Configuration Options

```csharp
public sealed class StyloFlowOptions
{
    // License Configuration
    public string? LicenseFilePath { get; set; }
    public string? LicenseToken { get; set; }
    public string? VendorPublicKey { get; set; }
    public LicenseOverrides? LicenseOverrides { get; set; }

    // Mesh Configuration
    public bool EnableMesh { get; set; } = false;
    public List<string> MeshPeers { get; set; } = new();
    public bool EnableLanDiscovery { get; set; } = false;
    public int MeshPort { get; set; } = 5200;

    // System Coordinator
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan WorkUnitWindowSize { get; set; } = TimeSpan.FromMinutes(1);
    public int WorkUnitWindowBuckets { get; set; } = 60;
    public TimeSpan LicenseGracePeriod { get; set; } = TimeSpan.FromMinutes(5);
    public int[] WorkUnitThresholds { get; set; } = { 80, 90, 100 };

    // Free Tier Defaults
    public int FreeTierMaxSlots { get; set; } = 10;
    public int FreeTierMaxWorkUnitsPerMinute { get; set; } = 1000;
    public int FreeTierMaxNodes { get; set; } = 3;

    // Extensibility
    public Func<string, CancellationToken, Task<LicenseValidationResult>>? CustomLicenseValidator { get; set; }
    public Action<LicenseStateChangedEvent>? OnLicenseStateChanged { get; set; }
    public Action<WorkUnitThresholdEvent>? OnWorkUnitThreshold { get; set; }
}
```

## License Token Format

```json
{
    "licenseId": "lic-xxxxx",
    "issuedTo": "customer@example.com",
    "issuedAt": "2024-01-01T00:00:00Z",
    "expiry": "2025-01-01T00:00:00Z",
    "tier": "professional",
    "features": ["documents.*", "images.*", "data.*", "premium.analytics"],
    "limits": {
        "maxMoleculeSlots": 100,
        "maxWorkUnitsPerMinute": 1000,
        "maxNodes": 10
    },
    "signature": "base64-signature"
}
```

## Attributes

### StyloFlowLicensedAttribute

Mark components with licensing requirements:

```csharp
[StyloFlowLicensed(
    Tier = "starter",
    WorkUnits = 2.0,
    WorkUnitsPerKb = 0.5,
    RequiresSystem = true,
    Features = new[] { "documents.*" })]
public class MyProcessor { }
```

### ProvidesAttribute / RequiresCapabilityAttribute

Declare capabilities for dependency resolution:

```csharp
[Provides("detection.bot", Version = "1.0")]
[RequiresCapability("retrieval.core", Optional = true)]
public class BotDetector { }
```

## YAML Configuration

Configure licensing in component manifests:

```yaml
license:
  tier: starter
  features:
    - documents.*
    - images.*
  work_units:
    base: 2.0
    per_kb: 0.5
  requires_system: true
  allow_degradation: true

signals:
  defer_on:
    - styloflow.system.ready
  resume_on:
    - styloflow.system.ready
  free_tier:
    - component.degraded
    - component.limited
  licensed:
    - component.ready
    - component.full_features
```

## Testing

Override license settings for testing:

```csharp
services.AddStyloFlow(options =>
{
    options.LicenseOverrides = new LicenseOverrides
    {
        MaxSlots = 50,
        MaxWorkUnitsPerMinute = 20,
        Tier = "starter",
        Features = new List<string> { "test.*" },
        Expiry = DateTimeOffset.UtcNow.AddDays(5)  // Test expiring soon
    };
});
```

## API Reference

See the XML documentation in the source files for detailed API documentation:

- `ILicenseManager` - License validation and state management
- `IWorkUnitMeter` - Work unit metering and throttling
- `LicensedComponentBase` - Base class for licensed components
- `SystemCoordinator` - Background service for system coordination
- `StyloFlowServiceExtensions` - DI registration helpers

---

*This documentation is actively being expanded.*
