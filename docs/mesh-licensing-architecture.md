# StyloFlow Mesh Licensing Architecture

> **Status**: Draft
> **Last Updated**: 2025-01-08
> **Decision**: Limit model = **(C) Both** - concurrent instance cap + work unit rate limit

## Overview

This document describes a distributed licensing model for StyloFlow where licensing becomes a **cluster-level capability signal** that the mesh can agree on, without requiring complex distributed consensus.

### Design Principles

- **Signals over locks**: Licensing is just another signal in the orchestration model
- **Safe failures**: Uncertainty reduces capacity, never increases it
- **No fingerprinting**: Node identity via cryptographic keys, not hardware
- **Offline-first**: Core verification works without network connectivity
- **Graceful degradation**: Unknown state → free-tier, not broken

---

## 1. Node Identity and Membership

### Node Identity

Each node generates identity on first start - no machine fingerprinting required.

```
┌─────────────────────────────────────────┐
│ Node Bootstrap                          │
├─────────────────────────────────────────┤
│ 1. Generate Ed25519 keypair             │
│ 2. node_id = hash(public_key)           │
│ 3. Persist keypair locally              │
│ 4. Begin gossip with signed heartbeats  │
└─────────────────────────────────────────┘
```

### Membership Gossip (SWIM-style)

Nodes gossip membership via signed heartbeats:

```yaml
# Heartbeat message structure
heartbeat:
  node_id: "sha256:abc123..."
  endpoint: "192.168.1.10:5000"
  capabilities:
    roles: [worker, sensor]
    resources: [gpu, cpu_16, ram_32gb]
    molecule_types: [detector, analyzer, sink]
  timestamp: "2025-01-08T12:00:00Z"
  signature: "ed25519:..."
```

### Alive Set

- **Alive set** = nodes seen within last `T` seconds (e.g., 30s)
- No Raft required initially - TTL-based membership is sufficient
- Trust comes from signature verification, not discovery method

### Discovery Methods

| Method | Purpose | Trust Level |
|--------|---------|-------------|
| LAN multicast | Convenience startup | Discovery only |
| Explicit peer | Join via known node | Discovery only |
| Signed heartbeat | Membership proof | Trusted |

---

## 2. License Token Structure

Licenses are signed tokens that nodes can verify offline.

### Token Claims

```yaml
# License token structure
license:
  license_id: "lic_abc123"
  issued_to: "customer@example.com"
  issued_at: "2025-01-01T00:00:00Z"
  expiry: "2026-01-01T00:00:00Z"

  # Capacity limits (DUAL MODEL)
  limits:
    max_nodes: 25                    # Optional node count cap
    max_molecule_slots: 100          # Hard cap on concurrent instances
    max_work_units_per_minute: 10000 # Throughput rate limit

  # Feature gates
  features:
    - detector.*
    - analyzer.*
    - sink.*
    - escalator.llm          # Premium feature
    - dashboard.realtime     # Premium feature

  # Tier metadata
  tier: "professional"       # free | starter | professional | enterprise

  signature: "ed25519:vendor_signature..."
```

### Verification

All nodes embed vendor's public key and verify licenses offline:

```
verify(license.signature, license.claims, vendor_public_key) → bool
```

---

## 3. Slot Allocation Mechanism

### The Problem

Without coordination, every node might think it can run all licensed slots.

### Solution: Lease Authority (LA)

A single node acts as **Lease Authority** using deterministic election:

```
LA = node with lowest node_id among alive set
```

This provides:
- No election protocol needed
- Automatic failover when LA dies
- Deterministic - all nodes agree on who LA is

### Slot Lease Structure

```yaml
# Lease issued by LA
slot_lease:
  slot_id: "slot_00042"
  molecule_type: "detector.bot"
  holder_node_id: "sha256:abc123..."
  issued_at: "2025-01-08T12:00:00Z"
  expires_at: "2025-01-08T12:05:00Z"  # Short TTL (e.g., 5 min)

  # LA signs with its node keypair
  la_node_id: "sha256:def456..."
  signature: "ed25519:la_signature..."
```

### Lease Acceptance Rules

Node accepts a lease only if ALL conditions met:
1. `signature` verifies against LA's public key
2. `expires_at` is in the future
3. `holder_node_id` matches this node
4. LA is the expected LA (lowest node_id in alive set)

### Lease Lifecycle

```
┌──────────┐    request    ┌──────────┐
│  Node    │──────────────▶│    LA    │
│          │               │          │
│          │◀──────────────│          │
└──────────┘  signed lease └──────────┘
     │                           │
     │ (use lease to run         │ (track total issued
     │  molecule)                │  vs license.max_slots)
     │                           │
     ▼                           ▼
┌──────────┐               ┌──────────┐
│ Molecule │               │  Ledger  │
│ Instance │               │          │
└──────────┘               └──────────┘
```

---

## 4. Component Authentication

Without authentication, malicious code could forge signals to bypass licensing. The core principle: **only vendor-signed components can emit licensing-critical signals**.

### Trust Hierarchy

```
┌─────────────────────────────────────────────────────────┐
│                    VENDOR KEY                           │
│    (signs licenses AND component manifests/images)      │
└────────────────────────┬────────────────────────────────┘
                         │ signs
            ┌────────────┴────────────┐
            │                         │
            ▼                         ▼
┌───────────────────────┐  ┌─────────────────────────────┐
│    LICENSE TOKEN      │  │   COMPONENT SIGNATURE       │
│ (cluster operation)   │  │  (molecule image/manifest)  │
└───────────────────────┘  └──────────────┬──────────────┘
                                          │ verified by
                                          ▼
                           ┌─────────────────────────────┐
                           │       NODE RUNTIME          │
                           │  (refuses unsigned code)    │
                           └──────────────┬──────────────┘
                                          │ issues
                                          ▼
                           ┌─────────────────────────────┐
                           │   RUNTIME ATTESTATION       │
                           │ (proof of verified launch)  │
                           └─────────────────────────────┘
```

### Vendor-Signed Components

The vendor (you) signs molecule manifests and/or container images:

```yaml
# Molecule manifest with vendor signature
molecule_manifest:
  type: "detector.bot"
  version: "1.2.0"

  # Container image reference
  image: "ghcr.io/styloflow/detector-bot:1.2.0"
  image_digest: "sha256:abc123..."

  # What this component is allowed to emit
  signal_permissions:
    - "styloflow.molecule.detector.*"
    - "styloflow.workunit.consumed"

  # Work unit costs (enforced by runtime)
  work_units:
    per_invocation: 1
    per_kb_input: 0.1

  # Vendor signature over entire manifest
  vendor_signature: "ed25519:vendor_key_signature..."
```

### Signal Permission Model

Only vendor-signed components can emit licensing-critical signals:

```yaml
signal_categories:
  # OPEN: Any code can emit (debugging, app-level)
  open:
    - "app.*"
    - "debug.*"
    - "custom.*"

  # SIGNED: Only vendor-signed molecules can emit
  signed_required:
    - "styloflow.workunit.*"      # Consumption metering
    - "styloflow.molecule.*"      # Molecule lifecycle
    - "styloflow.detection.*"     # Detection results
    - "styloflow.analysis.*"      # Analysis outputs

  # RUNTIME: Only the runtime itself can emit
  runtime_only:
    - "styloflow.node.*"
    - "styloflow.mesh.*"
    - "styloflow.leases.*"
    - "styloflow.slots.*"
    - "styloflow.capacity.*"

  # LA_ONLY: Only the Lease Authority can emit
  la_only:
    - "styloflow.cluster.*"
```

### Verification Flow

```
┌─────────────────────────────────────────────────────────┐
│                  MOLECULE START                         │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │ Fetch manifest      │
              └──────────┬──────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │ Verify vendor sig?  │──── NO ───▶ REJECT
              └──────────┬──────────┘            (unsigned)
                         │ YES
                         ▼
              ┌─────────────────────┐
              │ Verify image digest │──── NO ───▶ REJECT
              │ matches manifest?   │            (tampered)
              └──────────┬──────────┘
                         │ YES
                         ▼
              ┌─────────────────────┐
              │ Valid slot lease?   │──── NO ───▶ QUEUE
              └──────────┬──────────┘
                         │ YES
                         ▼
              ┌─────────────────────┐
              │ Issue attestation   │
              │ (includes perms     │
              │  from manifest)     │
              └──────────┬──────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │ START CONTAINER     │
              └─────────────────────┘
```

### Why This Matters

| Without Code Signing | With Vendor Code Signing |
|---------------------|--------------------------|
| Anyone writes molecule that reports 0 work units | Only your signed molecules can report WU |
| Fork the runtime, emit fake signals | Runtime signature check is unskippable |
| Run unofficial molecules on licensed cluster | Unsigned molecules can't consume slots |
| Game the licensing system | Licensing is cryptographically enforced |

### Capability Advertising

The signed manifest becomes a **capability contract** - components declare what they can do, and the mesh can route/compose based on these capabilities.

```yaml
# Full molecule manifest with capabilities
molecule_manifest:
  type: "detector.bot"
  version: "1.2.0"

  # ─────────────────────────────────────────────────
  # CAPABILITIES: What this component can do
  # ─────────────────────────────────────────────────
  capabilities:
    provides:
      - "detection.bot"              # Can detect bots
      - "detection.automation"       # Can detect automation
      - "scoring.risk"               # Can produce risk scores

    # Semantic capability tags for discovery
    tags:
      - "security"
      - "fraud-prevention"
      - "realtime"

  # ─────────────────────────────────────────────────
  # INPUTS: What this component consumes
  # ─────────────────────────────────────────────────
  inputs:
    required:
      - name: "http_request"
        schema: "styloflow://schemas/http-request.v1"
    optional:
      - name: "session_context"
        schema: "styloflow://schemas/session.v1"
      - name: "historical_signals"
        schema: "styloflow://schemas/signal-batch.v1"

  # ─────────────────────────────────────────────────
  # OUTPUTS: What this component produces
  # ─────────────────────────────────────────────────
  outputs:
    signals:
      - name: "styloflow.detection.bot.result"
        schema: "styloflow://schemas/detection-result.v1"
      - name: "styloflow.detection.bot.confidence"
        schema: "styloflow://schemas/score.v1"

    # Can feed into other components
    feeds:
      - "analyzer.*"                 # Any analyzer can consume
      - "sink.logging"               # Logging sink

  # ─────────────────────────────────────────────────
  # REQUIREMENTS: What this component needs to run
  # ─────────────────────────────────────────────────
  requirements:
    resources:
      cpu: "100m"                    # Minimum CPU
      memory: "128Mi"                # Minimum memory
      gpu: false                     # No GPU needed

    node_capabilities:
      - "network.egress"             # Needs outbound network

    dependencies:
      # Other molecules that must be available
      - type: "retrieval.core"
        version: ">=1.0.0"

  # ─────────────────────────────────────────────────
  # LICENSING: Cost and permissions
  # ─────────────────────────────────────────────────
  licensing:
    signal_permissions:
      - "styloflow.detection.bot.*"
      - "styloflow.workunit.consumed"

    work_units:
      per_invocation: 1
      per_kb_input: 0.1

    tier_required: "starter"         # Minimum license tier

  # ─────────────────────────────────────────────────
  # Container image + vendor signature
  # ─────────────────────────────────────────────────
  image: "ghcr.io/styloflow/detector-bot:1.2.0"
  image_digest: "sha256:abc123..."
  vendor_signature: "ed25519:vendor_signature..."
```

### Capability-Based Discovery

The mesh can discover and route based on capabilities:

```yaml
# "I need something that provides bot detection"
capability_query:
  requires:
    - "detection.bot"
  prefers:
    - "realtime"

# Returns: detector.bot, detector.advanced-bot, partner.acme.bot-detector
```

### Capability-Based Composition

Molecules can be automatically composed into pipelines:

```
┌─────────────────────────────────────────────────────────┐
│                    PIPELINE ASSEMBLY                     │
└─────────────────────────────────────────────────────────┘

Request: "Build a bot detection pipeline with logging"

1. Find component providing "detection.bot"
   → detector.bot (outputs: detection.bot.result)

2. Find component that can consume detection results
   → analyzer.risk (inputs: detection.*.result)

3. Find sink for logging
   → sink.logging (inputs: any)

Result:
┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│ detector.bot │───▶│ analyzer.risk│───▶│ sink.logging │
└──────────────┘    └──────────────┘    └──────────────┘
```

### Why Signing Capabilities Matters

| Unsigned Capabilities | Signed Capabilities |
|----------------------|---------------------|
| Component claims any capability | Capabilities are vendor-verified |
| Fake "premium" detectors | Premium capabilities require signing |
| Schema mismatches at runtime | Input/output contracts enforced |
| Unknown resource consumption | Resource requirements are declared |
| Shadow components | All components discoverable and auditable |

### Capability Signals

Components can advertise runtime capability status:

```yaml
# Emitted by runtime based on signed manifest
signals:
  styloflow.capability.available:
    component: "detector.bot"
    capabilities: ["detection.bot", "scoring.risk"]
    node_id: "sha256:..."

  styloflow.capability.degraded:
    component: "detector.bot"
    reason: "dependency_unavailable"
    missing: ["retrieval.core"]
```

### System Coordinator (Trust Anchor)

StyloFlow runs a **System Coordinator** at startup that manages licensing, clustering, and authentication. Licensed components **require** this coordinator to be running and authenticated.

```
┌─────────────────────────────────────────────────────────┐
│                  STYLOFLOW STARTUP                      │
└────────────────────────┬────────────────────────────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │  System Coordinator │
              │  (singleton)        │
              └──────────┬──────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
   ┌───────────┐  ┌───────────┐  ┌───────────┐
   │ Licensing │  │   Mesh    │  │  Lease    │
   │  Manager  │  │ Membership│  │ Authority │
   └─────┬─────┘  └─────┬─────┘  └─────┬─────┘
         │               │               │
         └───────────────┼───────────────┘
                         │
                         ▼
              ┌─────────────────────┐
              │ Emits system signals│
              │ (proof of auth)     │
              └─────────────────────┘
```

**System signals emitted:**

```yaml
# System coordinator emits these on successful startup
signals:
  styloflow.system.ready: true
  styloflow.system.license.valid: true
  styloflow.system.license.tier: "professional"
  styloflow.system.mesh.joined: true
  styloflow.system.la.active: true  # If this node is LA
```

**Licensed components require system signals:**

```yaml
# Licensed molecule manifest
molecule_manifest:
  type: "detector.bot"

  # MUST have system coordinator running
  requires_signals:
    - "styloflow.system.ready"
    - "styloflow.system.license.valid"

  # Defers if license is degraded
  defer_on_signals:
    - "styloflow.system.license.degraded"

  # Cancels if system goes down
  cancel_on_signals:
    - "styloflow.system.shutdown"
```

**Why this works:**

| Without System Coordinator | With System Coordinator |
|---------------------------|------------------------|
| Components self-report licensing | System coordinator is single source of truth |
| Anyone can emit license signals | Only system coordinator can emit `styloflow.system.*` |
| No enforcement point | Clear gate: no system = no licensed components |
| Hard to audit | All license decisions flow through one point |

### Integration with Ephemeral.Core

**Ephemeral already provides almost everything.** Looking at the actual codebase:

```
┌─────────────────────────────────────────────────────────┐
│              EPHEMERAL (already built)                  │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ATOMS & MANIFESTS (mostlylucid.ephemeral.atoms.taxonomy)│
│  ├── AtomManifest: Full YAML-driven config              │
│  │   ├── Taxonomy (kind, determinism, persistence)      │
│  │   ├── Triggers (requires, signals, skipWhen)         │
│  │   ├── Emits (onStart, onComplete, onFailure)         │
│  │   ├── Preserve (echo, escalate, propagate)           │
│  │   ├── Budget (maxDuration, maxTokens, maxCost)       │
│  │   ├── Lane (concurrency lanes)                       │
│  │   └── NuGetReference.RequiresLicense (!)             │
│  ├── MoleculeManifest: Atom composition                 │
│  └── ManifestRegistry: Discovery and loading            │
│                                                         │
│  COORDINATORS (mostlylucid.ephemeral)                   │
│  ├── EphemeralWorkCoordinator                           │
│  ├── EphemeralKeyedWorkCoordinator                      │
│  ├── PriorityWorkCoordinator                            │
│  └── EphemeralOptions with:                             │
│      ├── DeferOnSignals (wait for signals)              │
│      ├── CancelOnSignals (skip on signals)              │
│      ├── ResumeOnSignals (explicit trigger)             │
│      ├── DrainOnSignals (graceful shutdown)             │
│      └── ClearOnSignals (state reset)                   │
│                                                         │
│  SIGNALS (mostlylucid.ephemeral)                        │
│  ├── SignalSink (lock-free, high-performance)           │
│  ├── SignalConstraints (cycle detection, depth limits)  │
│  └── ResponsibilitySignalManager (pin until ack)        │
│                                                         │
│  ATTRIBUTES (mostlylucid.ephemeral.attributes)          │
│  ├── [EphemeralJob] - triggers, lanes, emit signals     │
│  ├── [EphemeralJobs] - class defaults                   │
│  ├── [EphemeralLane] - lane configuration               │
│  └── [KeySource] - operation keying                     │
│                                                         │
└─────────────────────────────────────────────────────────┘
                         │
                         │ StyloFlow adds ONLY
                         ▼
┌─────────────────────────────────────────────────────────┐
│              STYLOFLOW (minimal delta)                  │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  SYSTEM COORDINATOR (auto-starts with AddStyloFlow)     │
│  ├── License validation and signal emission             │
│  ├── Mesh membership (future)                           │
│  └── Lease Authority (future)                           │
│                                                         │
│  LICENSING EXTENSION TO MANIFESTS                       │
│  ├── licensing.work_units (metering costs)              │
│  ├── licensing.tier_required (tier gating)              │
│  ├── licensing.signal_permissions (auth enforcement)    │
│  └── vendor_signature (cryptographic verification)      │
│                                                         │
│  LICENSING ATTRIBUTES                                   │
│  ├── [StyloFlowLicensed] - work units, tier, requires   │
│  ├── [Provides] - capability advertising                │
│  └── [RequiresCapability] - capability dependencies     │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

**Key insight:** Ephemeral's `DeferOnSignals` + `ResumeOnSignals` IS the licensing gate mechanism. Licensed components simply:

```csharp
// Component configuration via EphemeralOptions
new EphemeralOptions
{
    // Wait for system coordinator to emit license signal
    DeferOnSignals = new HashSet<string> { "styloflow.system.license.required" },
    ResumeOnSignals = new HashSet<string> { "styloflow.system.license.valid" },

    // Cancel if license revoked
    CancelOnSignals = new HashSet<string> { "styloflow.system.license.revoked" }
}
```

### DI Registration

```csharp
// Full StyloFlow with licensing (System Coordinator starts automatically)
services.AddStyloFlow();

// With configuration
services.AddStyloFlow(options =>
{
    options.LicenseFilePath = "license.json";
    options.EnableMesh = false;              // Single-node mode
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
    options.WorkUnitWindowSize = TimeSpan.FromMinutes(1);
});

// Free tier - no licensing, no system coordinator
// Use for development, testing, or truly unlicensed deployments
services.AddStyloFlowFree();
```

**What each variant provides:**

| Method | System Coordinator | Licensing | Signals |
|--------|-------------------|-----------|---------|
| `AddStyloFlow()` | Yes (auto-start) | Full | `styloflow.system.*` |
| `AddStyloFlow(opts)` | Yes (configurable) | Configurable | `styloflow.system.*` |
| `AddStyloFlowFree()` | No | None | No system signals |

**Implementation:**

```csharp
public static class StyloFlowServiceExtensions
{
    /// <summary>
    /// Adds StyloFlow with full licensing. System coordinator starts automatically.
    /// </summary>
    public static IServiceCollection AddStyloFlow(
        this IServiceCollection services,
        Action<StyloFlowOptions>? configure = null)
    {
        var options = new StyloFlowOptions();
        configure?.Invoke(options);

        // Register options
        services.AddSingleton(options);

        // Core Ephemeral (if not already registered)
        services.AddEphemeralSignals();

        // System coordinator - starts on host startup
        services.AddHostedService<SystemCoordinator>();

        // License manager
        services.AddSingleton<ILicenseManager, LicenseManager>();

        // Work unit meter
        services.AddSingleton<IWorkUnitMeter, WorkUnitMeter>();

        return services;
    }

    /// <summary>
    /// Adds StyloFlow without licensing. No system coordinator.
    /// Use for development, testing, or free/unlicensed deployments.
    /// </summary>
    public static IServiceCollection AddStyloFlowFree(this IServiceCollection services)
    {
        // Core Ephemeral only
        services.AddEphemeralSignals();

        // No system coordinator, no licensing
        // Licensed atoms will defer forever (or use [StyloFlowLicensed(RequiresSystem = false)])
        return services;
    }
}

public class StyloFlowOptions
{
    // ─────────────────────────────────────────────────
    // LICENSE CONFIGURATION (all code-configurable, no JSON required)
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Path to license file. Optional - can configure license entirely in code.
    /// </summary>
    public string? LicenseFilePath { get; set; }

    /// <summary>
    /// License token (inline). Use this OR LicenseFilePath, not both.
    /// </summary>
    public string? LicenseToken { get; set; }

    /// <summary>
    /// Vendor public key for signature verification.
    /// Can be embedded or loaded from options.
    /// </summary>
    public string? VendorPublicKey { get; set; }

    /// <summary>
    /// Override license limits in code (useful for testing/dev).
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
    /// Mesh peer endpoints to join on startup.
    /// </summary>
    public List<string> MeshPeers { get; set; } = new();

    /// <summary>
    /// Enable LAN discovery for automatic peer finding.
    /// </summary>
    public bool EnableLanDiscovery { get; set; } = false;

    /// <summary>
    /// Port for mesh gossip protocol.
    /// </summary>
    public int MeshPort { get; set; } = 5200;

    // ─────────────────────────────────────────────────
    // SYSTEM COORDINATOR
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Heartbeat interval for system health signals.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sliding window size for work unit metering.
    /// </summary>
    public TimeSpan WorkUnitWindowSize { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Grace period before revoking leases on license issues.
    /// </summary>
    public TimeSpan LicenseGracePeriod { get; set; } = TimeSpan.FromMinutes(5);

    // ─────────────────────────────────────────────────
    // EXTENSIBILITY
    // ─────────────────────────────────────────────────

    /// <summary>
    /// Custom license validator (replace default implementation).
    /// </summary>
    public Func<string, CancellationToken, Task<LicenseValidationResult>>? CustomLicenseValidator { get; set; }

    /// <summary>
    /// Custom work unit calculator (replace default implementation).
    /// </summary>
    public Func<object, double>? CustomWorkUnitCalculator { get; set; }

    /// <summary>
    /// Callback when license state changes.
    /// </summary>
    public Action<LicenseStateChangedEvent>? OnLicenseStateChanged { get; set; }

    /// <summary>
    /// Callback when work unit threshold approached.
    /// </summary>
    public Action<WorkUnitThresholdEvent>? OnWorkUnitThreshold { get; set; }
}

public class LicenseOverrides
{
    public int? MaxSlots { get; set; }
    public int? MaxWorkUnitsPerMinute { get; set; }
    public string? Tier { get; set; }
    public List<string>? Features { get; set; }
}
```

**Core philosophy: Everything configurable from code.**

```csharp
// Minimal - just works with defaults
services.AddStyloFlow();

// File-based license
services.AddStyloFlow(opts => opts.LicenseFilePath = "license.json");

// Inline license token (no file needed)
services.AddStyloFlow(opts =>
{
    opts.LicenseToken = "eyJsaWNlbnNlX2lkIjoiLi4uIn0=...";
    opts.VendorPublicKey = "ed25519:abc123...";
});

// Development/testing overrides
services.AddStyloFlow(opts =>
{
    opts.LicenseOverrides = new LicenseOverrides
    {
        MaxSlots = 100,
        Tier = "enterprise",
        Features = new() { "*" }  // All features
    };
});

// Multi-node mesh cluster
services.AddStyloFlow(opts =>
{
    opts.EnableMesh = true;
    opts.MeshPeers = new() { "node1:5200", "node2:5200" };
    opts.EnableLanDiscovery = true;
});

// Custom license validation (e.g., call your own license server)
services.AddStyloFlow(opts =>
{
    opts.CustomLicenseValidator = async (token, ct) =>
    {
        var result = await myLicenseService.ValidateAsync(token, ct);
        return new LicenseValidationResult { Valid = result.IsValid, Tier = result.Tier };
    };
});

// Event hooks
services.AddStyloFlow(opts =>
{
    opts.OnLicenseStateChanged = e =>
        logger.LogWarning("License state: {State}", e.NewState);

    opts.OnWorkUnitThreshold = e =>
        logger.LogWarning("Work units at {Percent}%", e.PercentUsed);
});
```

**Licensed atoms with `AddStyloFlowFree()`:**

If you use `AddStyloFlowFree()` but try to run licensed atoms:
- Atoms with `RequiresSystem = true` (default) will **defer forever** waiting for `styloflow.system.license.valid`
- Atoms with `RequiresSystem = false` will run without licensing checks

```csharp
// This atom works with AddStyloFlowFree()
[EphemeralJob("data.received")]
[StyloFlowLicensed(RequiresSystem = false)]  // Opt-out of system coordinator
public async Task ProcessFreeAsync(string data, CancellationToken ct) { ... }

// This atom requires AddStyloFlow() - won't start with AddStyloFlowFree()
[EphemeralJob("data.premium")]
[StyloFlowLicensed(Tier = "professional")]  // RequiresSystem = true by default
public async Task ProcessPremiumAsync(string data, CancellationToken ct) { ... }
```

### Molecule = Signed Atom

A StyloFlow molecule is essentially:

```csharp
// Conceptual - molecule extends atom with licensing
public interface IMolecule<TIn, TOut> : IAtom<TIn, TOut>
{
    // Inherited from IAtom
    // Task<TOut> ProcessAsync(TIn input, CancellationToken ct);
    // AtomManifest Manifest { get; }

    // Added by StyloFlow
    MoleculeManifest SignedManifest { get; }  // Includes vendor sig
    SlotLease? CurrentLease { get; }           // Runtime-injected
    ComponentAttestation Attestation { get; }  // Runtime-issued proof
}

// The manifest extends AtomManifest
public record MoleculeManifest : AtomManifest
{
    public LicensingInfo Licensing { get; init; }
    public string VendorSignature { get; init; }
}
```

### Existing Atoms as Molecules

Existing Ephemeral atoms can be wrapped as molecules:

```yaml
# Wrapping an existing atom
molecule_manifest:
  type: "detector.bot"

  # Reference to underlying atom
  atom:
    assembly: "Mostlylucid.Ephemeral.Detection"
    type: "BotDetectorAtom"

  # StyloFlow additions
  licensing:
    work_units:
      per_invocation: 1
    signal_permissions:
      - "styloflow.detection.bot.*"

  vendor_signature: "ed25519:..."
```

### Attribute-Based Definition

**Ephemeral.Attributes already provides:**
- `[EphemeralJob]` - Signal triggers, priority, concurrency, emit signals, retries
- `[EphemeralJobs]` - Class-level defaults and signal prefix
- `[EphemeralLane]` - Processing lane configuration
- `[KeySource]` - Operation keying

**StyloFlow adds licensing attributes:**

```csharp
// StyloFlow licensing attributes (extend Ephemeral)
namespace StyloFlow.Attributes;

/// <summary>
/// Marks a job as requiring StyloFlow licensing.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class StyloFlowLicensedAttribute : Attribute
{
    /// <summary>
    /// Work units consumed per invocation.
    /// </summary>
    public double WorkUnits { get; set; } = 1.0;

    /// <summary>
    /// Additional work units per KB of input.
    /// </summary>
    public double WorkUnitsPerKb { get; set; } = 0.0;

    /// <summary>
    /// Minimum license tier required (free, starter, professional, enterprise).
    /// </summary>
    public string Tier { get; set; } = "free";

    /// <summary>
    /// If true (default), requires system coordinator to be running.
    /// </summary>
    public bool RequiresSystem { get; set; } = true;
}

/// <summary>
/// Declares capabilities this component provides.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ProvidesAttribute : Attribute
{
    public string Capability { get; }
    public ProvidesAttribute(string capability) => Capability = capability;
}

/// <summary>
/// Declares capabilities this component requires.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequiresCapabilityAttribute : Attribute
{
    public string Capability { get; }
    public RequiresCapabilityAttribute(string capability) => Capability = capability;
}
```

**Example: Licensed sensor using both Ephemeral and StyloFlow attributes:**

```csharp
using Mostlylucid.Ephemeral.Attributes;
using StyloFlow.Attributes;

[EphemeralJobs(SignalPrefix = "sensor.file")]
[Provides("sensor.plaintext")]
[Provides("sensor.file")]
[StyloFlowLicensed(WorkUnits = 0.5, Tier = "free")]
public class PlaintextFileSensor
{
    private readonly SignalSink _signals;

    public PlaintextFileSensor(SignalSink signals) => _signals = signals;

    [EphemeralJob("file.created.*",
        EmitOnComplete = ["sensor.file.processed"],
        EmitOnFailure = ["sensor.file.error"],
        MaxConcurrency = 4,
        Lane = "io")]
    [StyloFlowLicensed(WorkUnitsPerKb = 0.1)]  // Override: also charge per KB
    public async Task ProcessFileAsync(string filePath, CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(filePath, ct);

        // Emit lines as signals
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            _signals.Raise("sensor.file.line", line.Trim());
        }
    }
}
```

**The runtime enforces:**
1. `RequiresSystem = true` → Waits for `styloflow.system.ready` signal
2. `Tier = "free"` → Checks `styloflow.system.license.tier.*` signal
3. `WorkUnits` → Metered via system coordinator

### Third-Party / Partner Signing (Future)

For ecosystem extensibility, you could later support delegated signing:

```yaml
# Partner manifest with delegated signature
molecule_manifest:
  type: "partner.acme.custom-detector"
  version: "1.0.0"

  # Partner signs the manifest
  partner_id: "acme-corp"
  partner_signature: "ed25519:partner_signature..."

  # Vendor co-signs to authorize partner
  partner_authorization:
    partner_id: "acme-corp"
    partner_pubkey: "ed25519:partner_public_key..."
    allowed_signal_prefixes:
      - "partner.acme.*"           # Partner's namespace only
    work_unit_multiplier: 1.0      # No discount
    authorized_until: "2026-01-01"
    vendor_signature: "ed25519:vendor_cosign..."
```

This allows partners to publish molecules while:
- Keeping them namespaced (`partner.acme.*`)
- Enforcing work unit accounting
- Maintaining vendor control over authorization

### Component Attestation

When the node runtime starts a molecule, it issues a **component attestation**:

```yaml
component_attestation:
  attestation_id: "att_xyz789"
  component_type: "molecule"
  molecule_type: "detector.bot"
  instance_id: "inst_abc123"

  # Bindings
  node_id: "sha256:node_pubkey_hash..."
  slot_lease_id: "slot_00042"           # Must hold valid lease

  # Validity
  issued_at: "2025-01-08T12:00:00Z"
  expires_at: "2025-01-08T12:05:00Z"    # Short-lived, tied to lease

  # Runtime signs with node key
  signature: "ed25519:node_signature..."
```

### What Gets Signed

| Artifact | Signed By | Verified By | Purpose |
|----------|-----------|-------------|---------|
| License token | **Vendor key** | All nodes | Authorizes cluster operation |
| Molecule manifest | **Vendor key** | Runtime at launch | Authorizes component + perms |
| Container image | **Vendor key** (digest in manifest) | Runtime at launch | Ensures untampered code |
| Heartbeat | Node key | All nodes | Mesh membership |
| Lease request | Node key | LA | Slot allocation |
| Lease grant | LA node key | Requesting node | Slot confirmation |
| Work report | Node key + attestation | LA | Consumption metering |
| Throttle advisory | LA node key | All nodes | Rate limit coordination |
| Component signals | Attestation (derived from vendor sig) | Local runtime | Signal permission enforcement |

### Signal Authentication Levels

Not all signals need the same trust level:

```yaml
signal_trust_levels:
  # Level 0: Unsigned (local debugging only)
  local_only:
    - debug.*
    - trace.*

  # Level 1: Node-signed (trusted within mesh)
  node_signed:
    - styloflow.mesh.*
    - styloflow.node.*

  # Level 2: Attestation-required (component must prove identity)
  attested:
    - styloflow.workunit.*        # Consumption reports
    - styloflow.molecule.*        # Molecule status

  # Level 3: LA-signed (only LA can emit)
  la_authority:
    - styloflow.leases.*
    - styloflow.slots.*
    - styloflow.capacity.*        # Cluster-wide state
```

### Runtime Enforcement

The node runtime acts as a **trusted execution boundary**:

```
┌─────────────────────────────────────────────────────────┐
│                    NODE RUNTIME                         │
│                  (trusted process)                      │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐     │
│  │  Molecule   │  │  Molecule   │  │  Molecule   │     │
│  │  Container  │  │  Container  │  │  Container  │     │
│  │             │  │             │  │             │     │
│  │ attestation │  │ attestation │  │ attestation │     │
│  │   token     │  │   token     │  │   token     │     │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘     │
│         │                │                │            │
│         └────────────────┼────────────────┘            │
│                          │                             │
│                          ▼                             │
│              ┌─────────────────────┐                   │
│              │   Signal Gateway    │                   │
│              │  (validates sigs,   │                   │
│              │   enforces levels)  │                   │
│              └──────────┬──────────┘                   │
│                         │                              │
└─────────────────────────┼──────────────────────────────┘
                          │
                          ▼ (to mesh / LA)
```

### Attestation Flow

```
1. Molecule requests start
2. Runtime checks: valid slot lease?
3. Runtime issues attestation (short TTL, bound to lease)
4. Molecule receives attestation token
5. All molecule signals must include attestation
6. Runtime validates attestation before forwarding
7. On lease expiry: attestation invalidated
```

### Preventing Replay and Forgery

| Attack | Mitigation |
|--------|------------|
| Replay old work reports | Timestamp + sliding window + nonce |
| Forge attestation | Only runtime has node private key |
| Steal attestation | Short TTL, bound to container ID |
| Emit fake LA signals | LA key verified against known LA node |
| Man-in-middle | All messages signed end-to-end |

### Container Isolation

Molecules run in containers without access to:
- Node private key (held by runtime only)
- Other molecules' attestations
- Raw network (signals go through runtime gateway)

```yaml
container_restrictions:
  network: "runtime-proxy-only"    # No direct mesh access
  mounts:
    - "/signals" # Write-only signal socket
    - "/config"  # Read-only config
  secrets:
    attestation_token: "injected at start"
    # NO node key, NO other secrets
```

---

## 5. Work Unit Metering

With the dual-limit model, we enforce **both** concurrent instances (slots) **and** throughput (work units).

### What is a Work Unit?

A work unit is a normalized measure of processing. Each molecule type declares its cost:

```yaml
# Molecule manifest declares work unit cost
molecule:
  type: detector.bot
  work_units:
    per_invocation: 1        # Cost per request processed
    per_kb_input: 0.1        # Optional: cost scales with input size
```

### Work Unit Accounting

LA tracks cluster-wide work units using a **sliding window**:

```
┌─────────────────────────────────────────────────────────┐
│           Sliding Window (60 seconds)                   │
├─────────────────────────────────────────────────────────┤
│ [bucket_0][bucket_1][bucket_2]...[bucket_59]            │
│   150 WU    220 WU    180 WU  ...   190 WU              │
│                                                         │
│ Current rate = sum(buckets) = 8,450 WU/min              │
│ Limit = 10,000 WU/min                                   │
│ Headroom = 1,550 WU/min                                 │
└─────────────────────────────────────────────────────────┘
```

### Reporting Flow

Nodes report work unit consumption to LA:

```yaml
# Work unit report (sent every 5-10 seconds)
work_report:
  node_id: "sha256:abc123..."
  window_start: "2025-01-08T12:00:00Z"
  window_end: "2025-01-08T12:00:10Z"
  work_units_consumed: 342
  by_molecule_type:
    detector.bot: 200
    analyzer.sentiment: 142
  signature: "ed25519:..."
```

### Dual Enforcement

Both limits apply simultaneously:

```
┌─────────────────────────────────────────┐
│         MOLECULE START REQUEST          │
└───────────────────┬─────────────────────┘
                    │
                    ▼
          ┌─────────────────┐
          │ Slot available? │ ← Instance cap check
          └────────┬────────┘
                   │
         ┌─────────┴─────────┐
         │                   │
        YES                  NO
         │                   │
         ▼                   ▼
┌─────────────────┐    ┌──────────┐
│ Rate headroom?  │    │  DENY    │
│ (WU/min check)  │    │ (queued) │
└────────┬────────┘    └──────────┘
         │
   ┌─────┴─────┐
   │           │
  YES          NO
   │           │
   ▼           ▼
┌──────┐  ┌─────────────┐
│ ALLOW│  │ THROTTLE    │
│      │  │ (backpressure)│
└──────┘  └─────────────┘
```

### Throttling vs Denial

| Limit Hit | Behavior | Signal |
|-----------|----------|--------|
| Slot cap | Queue or deny new instances | `slots_exhausted` |
| Rate cap | Backpressure (slow down, don't deny) | `rate_limited` |
| Both | Queue + backpressure | `capacity_exhausted` |

Rate limiting applies **backpressure** rather than hard denial - molecules can still run but requests are delayed. This is fairer for bursty workloads.

### Work Unit Signals

```yaml
signals:
  styloflow.workunit.rate_current: 8450      # WU/min
  styloflow.workunit.rate_limit: 10000       # WU/min
  styloflow.workunit.headroom_pct: 15.5      # % remaining
  styloflow.workunit.throttling: false       # Currently throttling?
```

---

## 6. Slot Enforcement Gates

### Molecule Start Gate

Every molecule start requires a valid slot lease:

```
molecule_start_request
    │
    ▼
┌─────────────────────────────┐
│ Do we have a local lease    │
│ for this molecule type?     │
└─────────────────────────────┘
    │
    ├─── NO ──▶ Request lease from LA
    │               │
    │               ├─── Granted ──▶ Continue
    │               │
    │               └─── Denied ──▶ Queue / Deny / Degrade
    │
    └─── YES ─▶ Is lease valid and unexpired?
                    │
                    ├─── YES ──▶ Start container
                    │
                    └─── NO ──▶ Renew or release
```

### Continuous Validation

Periodic "still valid?" check (e.g., every 30s):

```yaml
# Validation loop per running molecule
validation:
  check_interval: 30s
  on_lease_expired:
    action: renew_or_stop
    grace_period: 60s
  on_renewal_failed:
    action: graceful_shutdown  # or degrade
    timeout: 30s
```

### Scheduling Integration

Licensing becomes just another constraint:

```yaml
# Scheduling decision inputs
scheduling_constraints:
  - node_health: healthy
  - node_load: < 80%
  - node_capabilities: matches molecule requirements
  - license_lease: valid slot available    # ← License constraint
  - affinity_rules: satisfied
```

---

## 7. Dynamic Limit Adjustment

### Local License Updates

LA can reload license from local file:

```
license_update_file → LA parses → new max_slots →
    → issue fewer/more leases accordingly
```

### Vendor Lease Tokens (Optional)

For remote control without hard phone-home:

```yaml
# Short-lived vendor lease (daily refresh)
vendor_lease:
  license_id: "lic_abc123"
  valid_from: "2025-01-08T00:00:00Z"
  valid_until: "2025-01-09T00:00:00Z"

  # Can override/reduce base license limits
  effective_limits:
    max_molecule_slots: 100

  signature: "ed25519:vendor_daily_signature..."
```

Behavior:
- LA fetches vendor lease daily (best effort)
- If unavailable, fall back to base license after TTL
- If base license also missing → free-tier defaults

### Limit Change Flow

```
┌─────────────────┐
│ New limit = 50  │ (was 100)
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────┐
│ LA stops issuing new leases         │
│ until active count < 50             │
└─────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│ Existing leases expire naturally    │
│ (short TTL means quick convergence) │
└─────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────┐
│ Cluster converges to new limit      │
└─────────────────────────────────────┘
```

---

## 8. Node Capability Descriptors

YAML-based node descriptors feed into scheduling:

```yaml
# node-descriptor.yaml
node:
  roles:
    - worker        # Can run molecules
    - sensor        # Can ingest data
    - sink          # Can output results
    - escalator     # Can call external services (LLM, etc.)

  resources:
    gpu: true
    cpu_cores: 16
    ram_gb: 32
    storage_gb: 500

  constraints:
    allowed_molecules:
      - detector.*
      - analyzer.*
    max_concurrent: 10

  security:
    can_host_paid: true         # Node allowed for paid features
    network_zone: "internal"

  signals:
    # These are live, updated by runtime
    current_load: 0.45
    available_slots: 7
    health: healthy
```

### Capability-Based Routing

Scheduler uses capabilities + licensing together:

```
placement_decision(molecule_request):
    candidates = nodes.where(
        health == healthy AND
        load < threshold AND
        capabilities.matches(molecule.requirements) AND
        has_valid_lease(molecule.type)    # ← License check
    )
    return select_best(candidates, strategy)
```

---

## 9. Abuse Resistance

### Without Hardware Fingerprinting

Prevent "copy license to infinite nodes" via:

1. **Node registration with vendor** (optional for paid tiers):
   ```
   vendor.register(license_id, node_public_key) → approval
   ```

2. **Server-side clamping**:
   - Max active node identities per license
   - Max lease refreshes per time window
   - Rate limiting on vendor lease requests

3. **Tiered node limits**:
   ```yaml
   tiers:
     free:
       max_nodes: 3
       max_molecule_slots: 10
     starter:
       max_nodes: 10
       max_molecule_slots: 50
     professional:
       max_nodes: 25
       max_molecule_slots: 100
     enterprise:
       max_nodes: unlimited
       max_molecule_slots: custom
   ```

### Explainable Enforcement

All limits are visible and documented:
- "You're running 3/3 nodes on free tier"
- "Upgrade to add more nodes"
- No mysterious failures

---

## 10. Failure Modes and Safe Defaults

### Failure Mode Table

| Condition | Response | Rationale |
|-----------|----------|-----------|
| Mesh membership uncertain | Reduce capacity | Safe default |
| LA flapping | Short leases + grace period | Self-healing |
| License missing/invalid | Free-tier mode | Graceful degradation |
| Vendor lease expired | Degrade to base license | Offline-capable |
| Node loses connectivity | Existing leases honor TTL | Grace period |
| LA dies | Next lowest node_id becomes LA | Automatic failover |

### Signal Integration

All failure states become signals that routing can respond to:

```yaml
# License-related signals
signals:
  license.valid: true|false
  license.tier: "free"|"paid"|"enterprise"
  license.slots_available: 42
  license.expires_soon: false      # < 7 days

  la.healthy: true|false
  la.node_id: "sha256:..."

  lease.my_active_count: 5
  lease.renewal_pending: false
```

### Degradation Cascade

```
Full capacity
    │
    ▼ (license expires)
Base license limits
    │
    ▼ (base license invalid)
Free-tier limits
    │
    ▼ (free-tier violated)
Minimal safe mode (local only, no mesh)
```

---

## 11. Implementation Phases

### Phase 1: Slot-Based Licensing (MVP)

Ship early with instance cap only:

1. **Signed license token** in local file
2. **Offline verification** (vendor public key embedded)
3. **Gossip membership** with signed heartbeats
4. **Deterministic LA selection** (lowest node_id)
5. **Slot leases** signed by LA
6. **Molecule start gate** requiring valid lease
7. **TTL-based renewal** with graceful degradation

### Phase 2: Work Unit Metering

Add throughput limiting:

1. **Work unit cost** in molecule manifests
2. **Sliding window accounting** in LA
3. **Node work reports** (periodic consumption updates)
4. **Backpressure signals** when rate limit approached
5. **Dashboard widgets** for rate monitoring

### Phase 3: Remote Control

Add:

1. **Vendor-issued daily lease** for remote adjustment
2. **Node registration** for abuse resistance
3. **License management UI** in StyloFlow.Dashboard

### Phase 4: Advanced Quotas

Add:

1. **Per-molecule-type quotas** (e.g., 50 detectors, 30 analyzers)
2. **Per-molecule-type rate limits** (separate WU pools)
3. **Per-tenant splits** for multi-tenant deployments
4. **Usage reporting** (opt-in telemetry)
5. **Quota transfer/borrowing** between molecule types

---

## 12. Decisions and Open Questions

### Decided: Limit Model = (C) Both

**Decision**: Use dual limits - concurrent instance cap + work unit rate limit.

| Limit Type | Enforcement | Behavior on Breach |
|------------|-------------|-------------------|
| **Max concurrent instances** | Hard cap via slot leases | Queue/deny new instances |
| **Max work units/minute** | Soft cap via sliding window | Backpressure (slow down) |

This provides:
- **Hard floor**: Can't exceed instance count (prevents runaway scaling)
- **Fair metering**: High-throughput users pay proportionally
- **Burst tolerance**: Rate limit uses backpressure, not denial

### Q2: Lease TTL Duration

- Shorter TTL (1-5 min): Faster convergence, more gossip
- Longer TTL (15-60 min): Less overhead, slower reaction

**Recommendation**: 5 minute TTL with 60 second grace period.

### Q3: Free Tier Behavior

Options:
1. Full functionality, limited capacity
2. Limited molecule types
3. Local-only (no mesh)

**Recommendation**: Option 1 - full functionality at 3 nodes / 10 slots.

---

## Appendix A: Message Formats

### Heartbeat Message

```json
{
  "type": "heartbeat",
  "node_id": "sha256:abc123...",
  "endpoint": "192.168.1.10:5000",
  "capabilities": {
    "roles": ["worker", "sensor"],
    "resources": {"gpu": true, "cpu_cores": 16}
  },
  "timestamp": "2025-01-08T12:00:00Z",
  "signature": "ed25519:..."
}
```

### Lease Request

```json
{
  "type": "lease_request",
  "requestor_node_id": "sha256:abc123...",
  "molecule_type": "detector.bot",
  "requested_duration_seconds": 300,
  "timestamp": "2025-01-08T12:00:00Z",
  "signature": "ed25519:..."
}
```

### Lease Grant

```json
{
  "type": "lease_grant",
  "slot_id": "slot_00042",
  "molecule_type": "detector.bot",
  "holder_node_id": "sha256:abc123...",
  "issued_at": "2025-01-08T12:00:00Z",
  "expires_at": "2025-01-08T12:05:00Z",
  "la_node_id": "sha256:def456...",
  "signature": "ed25519:..."
}
```

### Work Unit Report

```json
{
  "type": "work_report",
  "node_id": "sha256:abc123...",
  "window_start": "2025-01-08T12:00:00Z",
  "window_end": "2025-01-08T12:00:10Z",
  "work_units_consumed": 342,
  "by_molecule_type": {
    "detector.bot": 200,
    "analyzer.sentiment": 142
  },
  "timestamp": "2025-01-08T12:00:10Z",
  "signature": "ed25519:..."
}
```

### Throttle Advisory

```json
{
  "type": "throttle_advisory",
  "la_node_id": "sha256:def456...",
  "current_rate": 9500,
  "rate_limit": 10000,
  "throttle_factor": 0.8,
  "advisory": "reduce_rate",
  "expires_at": "2025-01-08T12:00:30Z",
  "signature": "ed25519:..."
}
```

### Component Attestation

```json
{
  "type": "component_attestation",
  "attestation_id": "att_xyz789",
  "component_type": "molecule",
  "molecule_type": "detector.bot",
  "instance_id": "inst_abc123",
  "container_id": "docker:abc123def...",
  "node_id": "sha256:node_pubkey_hash...",
  "slot_lease_id": "slot_00042",
  "issued_at": "2025-01-08T12:00:00Z",
  "expires_at": "2025-01-08T12:05:00Z",
  "signature": "ed25519:node_signature..."
}
```

### Attested Signal (wrapper for component signals)

```json
{
  "type": "attested_signal",
  "attestation_id": "att_xyz789",
  "signal": {
    "name": "styloflow.workunit.consumed",
    "value": 42,
    "timestamp": "2025-01-08T12:00:05Z"
  },
  "nonce": "random_nonce_12345",
  "signature": "ed25519:attestation_derived_sig..."
}
```

---

## Appendix B: Signals Emitted

```yaml
# License subsystem signals
styloflow.license.valid: bool
styloflow.license.tier: string
styloflow.license.expires_at: timestamp
styloflow.license.expires_soon: bool          # < 7 days remaining

# Slot (instance cap) signals
styloflow.slots.total: int                    # Licensed max
styloflow.slots.active: int                   # Currently held
styloflow.slots.available: int                # Remaining
styloflow.slots.exhausted: bool               # At capacity?

# Work unit (rate limit) signals
styloflow.workunit.rate_limit: int            # Licensed WU/min
styloflow.workunit.rate_current: int          # Current WU/min
styloflow.workunit.rate_pct: float            # % of limit used
styloflow.workunit.throttling: bool           # Backpressure active?
styloflow.workunit.throttle_factor: float     # 0.0-1.0 (1.0 = no throttle)

# Mesh signals
styloflow.mesh.node_count: int
styloflow.mesh.la_node_id: string
styloflow.mesh.la_healthy: bool
styloflow.mesh.membership_stable: bool        # No churn in last T seconds

# Lease signals
styloflow.leases.active_count: int
styloflow.leases.pending_renewals: int
styloflow.leases.denied_requests: int         # Counter
styloflow.leases.expired_grace: int           # In grace period

# Composite signals
styloflow.capacity.healthy: bool              # slots OK AND rate OK
styloflow.capacity.degraded: bool             # Either limit stressed
styloflow.capacity.exhausted: bool            # Both limits hit
```

These signals integrate with existing StyloFlow routing and can trigger:
- Capacity scaling decisions
- Alert notifications
- Dashboard updates
- Automatic degradation workflows
- Backpressure propagation to upstream services
