# Mostlylucid.StyloFlow

Declarative orchestration infrastructure with YAML-driven configuration, blackboard pattern, and signal-based coordination. Built on [mostlylucid.ephemeral](https://github.com/scottgal/mostlylucid.atoms).

## Installation

```bash
dotnet add package mostlylucid.styloflow
```

## Quick Start

### 1. Create a YAML Manifest

```yaml
# manifests/detectors/mydetector.detector.yaml
name: MyDetector
priority: 50
enabled: true
description: Detects specific patterns

taxonomy:
  kind: sensor
  determinism: deterministic
  persistence: ephemeral

triggers:
  requires:
    - signal: request.headers.present
    - signal: request.ip.present
  skip_when:
    - verified.human

emits:
  on_start:
    - detector.mydetector.started
  on_complete:
    - key: detector.mydetector.confidence
      type: double
      confidence_range: [0.0, 1.0]
  on_failure:
    - detector.mydetector.failed

defaults:
  weights:
    base: 1.0
    bot_signal: 1.2
    human_signal: 0.9
  confidence:
    neutral: 0.0
    bot_detected: 0.3
    high_threshold: 0.7
  timing:
    timeout_ms: 100
  features:
    detailed_logging: false
    enable_cache: true
  parameters:
    custom_threshold: 0.5
    patterns:
      - "pattern1"
      - "pattern2"
```

### 2. Register Services

```csharp
// File system manifests
services.AddStyloFlow(
    manifestDirectories: new[] { "manifests" },
    configSectionPath: "Components");

// Or embedded resource manifests
services.AddStyloFlowFromAssemblies(
    sourceAssemblies: new[] { typeof(MyDetector).Assembly },
    manifestPattern: ".yaml",
    configSectionPath: "Components");
```

### 3. Create a Configured Component

```csharp
public class MyDetector : ConfiguredComponentBase
{
    public MyDetector(IConfigProvider configProvider)
        : base(configProvider) { }

    public async Task<double> DetectAsync(RequestContext context)
    {
        // Use configured values - no magic numbers!
        var threshold = GetParam("custom_threshold", 0.5);
        var patterns = GetStringListParam("patterns");

        // Use weight shortcuts
        var weight = WeightBase;

        // Use confidence shortcuts
        if (someCondition)
            return ConfidenceBotDetected * weight;

        return ConfidenceNeutral;
    }
}
```

## Configuration Hierarchy

StyloFlow resolves configuration with a three-tier hierarchy:

1. **appsettings.json** (highest priority) - Runtime overrides
2. **YAML manifest** - Default values per component
3. **Code defaults** - Fallback values

```json
// appsettings.json - overrides YAML defaults
{
  "Components": {
    "MyDetector": {
      "Weights": {
        "Base": 1.5
      },
      "Parameters": {
        "custom_threshold": 0.7
      }
    }
  }
}
```

## Core Concepts

### ComponentManifest

The YAML manifest defines everything about a component:

| Section | Purpose |
|---------|---------|
| `name` | Component identifier |
| `priority` | Execution order (lower = earlier) |
| `taxonomy` | Classification (kind, determinism, persistence) |
| `triggers` | When to run (required signals, skip conditions) |
| `emits` | Signals emitted on start/complete/failure |
| `listens` | Signals this component consumes |
| `defaults` | Weights, confidence, timing, features, parameters |
| `escalation` | When to forward to expensive analysis |
| `budget` | Resource constraints (duration, tokens, cost) |

### ConfiguredComponentBase

Base class providing shortcuts to manifest configuration:

```csharp
// Weight shortcuts
protected double WeightBase;
protected double WeightBotSignal;
protected double WeightHumanSignal;
protected double WeightVerified;
protected double WeightEarlyExit;

// Confidence shortcuts
protected double ConfidenceNeutral;
protected double ConfidenceBotDetected;
protected double ConfidenceHumanIndicated;
protected double ConfidenceStrongSignal;
protected double ConfidenceHighThreshold;
protected double ConfidenceLowThreshold;

// Timing shortcuts
protected int TimeoutMs;
protected int CacheRefreshSec;

// Feature shortcuts
protected bool DetailedLogging;
protected bool CacheEnabled;
protected bool CanEarlyExit;
protected bool CanEscalate;

// Parameter access
protected T GetParam<T>(string name, T defaultValue);
protected IReadOnlyList<string> GetStringListParam(string name);
protected bool IsFeatureEnabled(string featureName);
```

### IManifestLoader

Load manifests from different sources:

- **FileSystemManifestLoader** - Watch directories for YAML files
- **EmbeddedManifestLoader** - Load from assembly embedded resources

### IConfigProvider

Resolve configuration with hierarchy:

```csharp
public interface IConfigProvider
{
    ComponentManifest? GetManifest(string componentName);
    ComponentDefaults GetDefaults(string componentName);
    T GetParameter<T>(string componentName, string parameterName, T defaultValue);
    IReadOnlyDictionary<string, ComponentManifest> GetAllManifests();
}
```

## Entity Types

Entity types define the input/output contracts for atoms - what data they consume and produce. This enables type-safe orchestration, validation, and documentation.

### Built-in Entity Types

StyloFlow includes built-in entity types for common scenarios:

| Category | Types | Description |
|----------|-------|-------------|
| `http` | `http.request`, `http.response` | HTTP request/response with headers, body, metadata |
| `image` | `image.*`, `image.png`, `image.jpeg` | Image files with size/dimension constraints |
| `video` | `video.*`, `video.mp4` | Video files with duration limits |
| `audio` | `audio.*` | Audio files |
| `document` | `document.pdf` | Document files |
| `behavioral` | `behavioral.signature`, `behavioral.session` | User behavioral data |
| `network` | `network.ip`, `network.tls` | Network-level signals |
| `detection` | `detection.contribution`, `detection.ledger` | Detection pipeline entities |
| `embedded` | `embedded.vector`, `embedded.multivector` | Vector embeddings for similarity search |
| `persistence` | `persistence.record`, `persistence.cached` | Storage-aware entities |
| `data` | `data.json`, `text.plain` | Generic data formats |

### Defining Entity Types in YAML

Create domain-specific entity types in `*.entity.yaml` files:

```yaml
# manifests/entities/myapp.entity.yaml
entity_types:
  - type: myapp.document
    category: myapp
    description: Application document with metadata
    persistence: database
    storage_hint: documents_table
    signal_patterns:
      - document.id
      - document.content
      - document.metadata.*
    schema:
      format: inline
      inline:
        type: object
        required: [id, content]
        properties:
          id:
            type: string
          content:
            type: string
          metadata:
            type: object

  - type: myapp.embedding
    category: myapp
    description: Document embedding for search
    persistence: embedded
    vector_dimension: 1536
    signal_patterns:
      - embedding.vector
      - embedding.document_id
```

### Entity Persistence Hints

Entity types can specify how they're typically stored:

| Persistence | Description | Use Case |
|-------------|-------------|----------|
| `ephemeral` | In-memory only | Processing intermediates |
| `json` | JSON serializable | API responses, logs |
| `database` | Persistent storage | Learning records, audit |
| `embedded` | Vector store | Similarity search |
| `file` | File/blob storage | Documents, media |
| `cached` | Short-term cache | Expensive computations |

### Multi-Vector Embeddings

For ColBERT-style late interaction or multi-aspect embeddings:

```yaml
- type: myapp.multivector_embedding
  category: myapp
  description: Multi-vector embedding with named components
  persistence: embedded
  signal_patterns:
    - embedding.vectors
    - embedding.fingerprint
  schema:
    format: inline
    inline:
      type: object
      required: [vectors, fingerprint]
      properties:
        vectors:
          type: array
          items:
            type: object
            required: [name, vector]
            properties:
              name:
                type: string
                description: Vector identifier (e.g., "title", "content")
              vector:
                type: array
                items:
                  type: number
              description:
                type: string
              weight:
                type: number
                default: 1.0
        fingerprint:
          type: string
        aggregation:
          type: string
          enum: [maxsim, avgpool, concat]
          default: maxsim
```

### Input/Output Contracts in Manifests

Reference entity types in component manifests:

```yaml
# Input contract - what this component consumes
input:
  accepts:
    - type: http.request
      required: true
      description: HTTP request for analysis
      signal_pattern: request.headers.*
    - type: behavioral.signature
      required: false
      description: Optional behavioral data
      signal_pattern: behavioral.*

  required_signals:
    - request.headers.user-agent
    - request.ip

  optional_signals:
    - request.headers.accept-language

# Output contract - what this component produces
output:
  produces:
    - type: detection.contribution
      description: Analysis contribution
    - type: myapp.embedding
      description: Generated embedding

  signals:
    - key: analysis.confidence
      entity_type: number
      salience: 0.8
      description: Analysis confidence score
```

### EntityTypeRegistry

Register and query entity types programmatically:

```csharp
var registry = new EntityTypeRegistry();

// Register custom types
registry.Register(new EntityTypeDefinition
{
    Type = "myapp.custom",
    Category = "myapp",
    Description = "Custom entity type",
    Persistence = EntityPersistence.Json,
    SignalPatterns = ["custom.*"]
});

// Query types
var httpTypes = registry.GetByPattern("http.*");
var isValid = registry.Validate("myapp.custom", myObject);
```

### EntityTypeLoader

Load entity types from YAML files or embedded resources:

```csharp
var loader = new EntityTypeLoader(logger);

// From directory
var types = loader.LoadFromDirectory("manifests/entities", "*.entity.yaml");

// From embedded resources
var types = loader.LoadFromAssembly(typeof(MyApp).Assembly, ".entity.yaml");

foreach (var type in types)
{
    registry.Register(type);
}
```

## YAML Schemas and Validation

StyloFlow provides JSON Schema definitions for validating YAML manifest files. These schemas enable IDE autocompletion, validation, and documentation.

### Available Schemas

| Schema | Location | Purpose |
|--------|----------|---------|
| `component-manifest.schema.json` | StyloFlow/Schemas/ | Base component manifest format |
| `entity-types.schema.json` | StyloFlow/Schemas/ | Entity type definition format |
| `detector-manifest.schema.json` | BotDetection/schemas/ | Detector-specific manifest format |
| `policy.schema.json` | BotDetection/schemas/ | Action policy definition format |

### Using Schemas with VS Code

Add a `.vscode/settings.json` to your project:

```json
{
  "yaml.schemas": {
    "./node_modules/styloflow/schemas/component-manifest.schema.json": [
      "**/*.detector.yaml",
      "**/*.sensor.yaml",
      "**/*.contributor.yaml"
    ],
    "./node_modules/styloflow/schemas/entity-types.schema.json": [
      "**/*.entity.yaml"
    ]
  }
}
```

Or reference directly in the YAML file:

```yaml
# yaml-language-server: $schema=https://styloflow.dev/schemas/component-manifest.schema.json
name: MyDetector
priority: 50
# ... rest of manifest
```

### Manifest File Naming Conventions

| Pattern | Purpose | Example |
|---------|---------|---------|
| `{name}.detector.yaml` | Detection components | `useragent.detector.yaml` |
| `{name}.sensor.yaml` | Low-level signal extraction | `http2.sensor.yaml` |
| `{name}.contributor.yaml` | Higher-level analysis | `heuristic.contributor.yaml` |
| `{name}.gatekeeper.yaml` | Flow control (early exit) | `fastpath.gatekeeper.yaml` |
| `{name}.pipeline.yaml` | Pipeline definitions | `detection.pipeline.yaml` |
| `{name}.entity.yaml` | Entity type definitions | `botdetection.entity.yaml` |
| `{name}.policies.yaml` | Policy definitions | `block.policies.yaml` |

### Schema Structure Overview

#### Component Manifest Schema

```yaml
# Core identification
name: string               # Required: Component identifier
priority: integer          # Execution order (lower = earlier)
enabled: boolean          # Whether component is active
description: string       # Human-readable description

# Classification
scope:
  sink: string            # Signal namespace (e.g., 'botdetection')
  coordinator: string     # Coordinator name
  atom: string           # Atom identifier

taxonomy:
  kind: enum             # sensor | analyzer | proposer | gatekeeper | aggregator | emitter
  determinism: enum      # deterministic | probabilistic
  persistence: enum      # ephemeral | direct_read | direct_write | cached

# Input/Output contracts
input:
  accepts: []            # Entity types consumed
  required_signals: []   # Signals that must be present
  optional_signals: []   # Signals that may enhance processing

output:
  produces: []           # Entity types produced
  signals: []            # Signals emitted (key, entity_type, salience, description)

# Execution control
triggers:
  requires: []           # Conditions to run
  skip_when: []         # Conditions to skip
  when: []              # Additional expressions

emits:
  on_start: []          # Signals on start
  on_complete: []       # Signals on completion
  on_failure: []        # Signals on failure
  conditional: []       # Conditional signals

listens:
  required: []          # Required upstream signals
  optional: []          # Optional upstream signals

# Resource management
lane:
  name: enum            # fast | normal | slow | llm | io
  max_concurrency: int  # Maximum parallel executions
  priority: int         # Priority within lane

budget:
  max_duration: string  # TimeSpan format (00:00:30)
  max_tokens: int       # For LLM components
  max_cost: number      # Cost limit in dollars

# Configuration
config:
  bindings: []          # Configuration key bindings

defaults:
  weights: {}           # Weight values
  confidence: {}        # Confidence thresholds
  timing: {}            # Timing parameters
  features: {}          # Feature flags
  parameters: {}        # Custom parameters

tags: []                # Categorization tags
```

#### Entity Types Schema

```yaml
namespace: string        # Prefix for all types (e.g., 'botdetection')
version: string         # Schema version
description: string     # Collection description

entity_types:
  type_name:
    base_type: enum     # string | number | boolean | object | array | embedded_vector | multivector
    description: string
    persistence: enum   # ephemeral | json | database | embedded | file | cached
    nullable: boolean
    default_value: any

    # For validation
    validation:
      min: number
      max: number
      min_length: int
      max_length: int
      pattern: string   # Regex
      enum: []          # Allowed values
      format: string    # email | uri | ipv4 | etc.

    # For objects
    properties: {}

    # For embeddings
    embedding:
      dimensions: int
      model: string
      distance_metric: enum  # cosine | euclidean | dot_product
      normalize: boolean

    # For multi-vector
    vectors:
      - name: string
        dimensions: int
        description: string
        weight: number
        model: string
        source_field: string

    signal_pattern: string   # For composing from signals
    storage_hint: string     # sqlite | redis | qdrant | etc.
    tags: []
```

### ManifestEntityValidator

Validate entity type references across manifests:

```csharp
var validator = new ManifestEntityValidator(registry, loader, logger);

// Validate a single manifest
var result = validator.Validate(manifest);
if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error: {error}");
    }
}

// Validate all manifests
var results = validator.ValidateAll(manifestLoader);
foreach (var r in results.Where(r => !r.IsValid))
{
    Console.WriteLine($"{r.ManifestName}: {r.Errors.Count} errors");
}
```

## Signal-Based Orchestration

### Trigger Conditions

```yaml
triggers:
  requires:
    - signal: request.headers.present
      condition: ">"
      value: 0
    - signal: ip.analyzed
  skip_when:
    - verified.bot
    - verified.human
  when:
    - confidence < 0.5
```

### Emitted Signals

```yaml
emits:
  on_start:
    - detector.mydetector.started
  on_complete:
    - key: detector.mydetector.result
      type: enum
      description: Detection result
  conditional:
    - key: detector.mydetector.bot_detected
      type: bool
      when: confidence > 0.7
  on_failure:
    - detector.mydetector.failed
```

### Escalation

```yaml
escalation:
  targets:
    llm_analysis:
      when:
        - signal: confidence
          condition: ">"
          value: 0.4
        - signal: confidence
          condition: "<"
          value: 0.7
      skip_when:
        - signal: budget.exhausted
      description: Forward ambiguous cases to LLM
```

## Taxonomy Classification

```yaml
taxonomy:
  kind: sensor          # sensor, aggregator, escalator, enricher
  determinism: deterministic  # deterministic, probabilistic, learning
  persistence: ephemeral      # ephemeral, cached, persisted
```

## Execution Lanes

```yaml
lane:
  name: fast           # fast, standard, expensive
  max_concurrency: 4
  priority: 50
```

## Budget Constraints

```yaml
budget:
  max_duration: "00:00:30"
  max_tokens: 1000
  max_cost: 0.01
```

## Architecture

```
                    ┌─────────────────┐
                    │  appsettings    │
                    │   overrides     │
                    └────────┬────────┘
                             │
                             ▼
┌──────────────┐    ┌─────────────────┐    ┌──────────────┐
│    YAML      │───▶│  ConfigProvider │◀───│     Code     │
│  Manifests   │    │   (resolves)    │    │   Defaults   │
└──────────────┘    └────────┬────────┘    └──────────────┘
                             │
                             ▼
                    ┌─────────────────┐
                    │  Configured     │
                    │  ComponentBase  │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
              ▼              ▼              ▼
        ┌──────────┐  ┌──────────┐  ┌──────────┐
        │Detector 1│  │Detector 2│  │Detector N│
        └──────────┘  └──────────┘  └──────────┘
```

## Licensing Module

StyloFlow includes a comprehensive licensing system for commercial deployments with tiered features, work unit metering, and mesh coordination.

### Quick Start

```csharp
// Full licensing with System Coordinator
services.AddStyloFlow(options =>
{
    options.LicenseToken = myLicenseJson;
    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
});

// Free tier (development/testing)
services.AddStyloFlowFree();

// Mesh mode for distributed deployments
services.AddStyloFlowMesh(new[] { "node1:5200", "node2:5200" });
```

### License Tiers

| Tier | Slots | Work Units/min | Features |
|------|-------|----------------|----------|
| `free` | 5 | 100 | Basic functionality |
| `starter` | 50 | 500 | Document/Image processing |
| `professional` | 100 | 1000 | Full retrieval + analytics |
| `enterprise` | Unlimited | Unlimited | Mesh + priority support |

### Licensed Components

Create license-aware components that automatically validate tier requirements:

```csharp
public class DocumentProcessor : LicensedComponentBase
{
    public DocumentProcessor(
        ILicenseManager licenseManager,
        IWorkUnitMeter workUnitMeter,
        SignalSink signalSink)
        : base(licenseManager, workUnitMeter, signalSink, new LicenseRequirements
        {
            MinimumTier = "starter",
            WorkUnits = 2.0,
            WorkUnitsPerKb = 0.5,
            RequiredFeatures = new[] { "documents.*" },
            AllowFreeTierDegradation = true
        })
    { }

    public override string ComponentId => "styloflow.documents.processor";

    public async Task<Result> ProcessAsync(byte[] document)
    {
        // Check if we can perform the operation
        if (!CanPerformOperation(document.Length))
            return Result.Throttled();

        // Do the work...
        var result = await DoProcessingAsync(document);

        // Record work units
        RecordWorkUnits(document.Length);

        // Emit completion signal
        EmitSignal("processed", document.Length.ToString());

        return result;
    }
}
```

### Work Unit Metering

Track and throttle resource consumption with sliding window metering:

```csharp
var meter = provider.GetRequiredService<IWorkUnitMeter>();

// Record work
meter.Record(10.0, "documents");

// Check capacity
if (meter.CanConsume(50.0))
{
    // Proceed with operation
}

// Get throttle factor (1.0 = full speed, 0.0 = throttled)
var factor = meter.ThrottleFactor;

// Subscribe to threshold events
meter.ThresholdCrossed += (_, evt) =>
{
    Console.WriteLine($"Threshold {evt.ThresholdPercent}% crossed");
};
```

### System Signals

The SystemCoordinator emits these signals for orchestration:

| Signal | Description |
|--------|-------------|
| `styloflow.system.ready` | System initialized and ready |
| `styloflow.system.heartbeat` | Periodic health check |
| `styloflow.system.license.valid` | License validated successfully |
| `styloflow.system.license.expires_soon` | License expiring within grace period |
| `styloflow.system.license.tier.{tier}` | Current license tier |
| `styloflow.system.slots.available` | Available molecule slots |
| `styloflow.system.workunit.rate` | Current work unit consumption rate |
| `styloflow.system.workunit.throttling` | Throttling is active |

### YAML License Configuration

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
  licensed:
    - component.ready
```

For detailed licensing documentation, see [src/StyloFlow.Licensing/README.md](src/StyloFlow.Licensing/README.md).

## Package Signing & Supply Chain Security

StyloFlow includes `sfsign`, a package signing tool that provides Ed25519-based cryptographic signatures for supply chain integrity.

### Installation

```bash
dotnet tool install -g Mostlylucid.StyloFlow.Sign
```

### Quick Start

```bash
# Generate a signing key
sfsign key generate --id "myvendor" --name "My Vendor Key" --keyring keyring.json

# Sign a package
sfsign sign --package mypackage.sfpkg --key "myvendor" --keyring keyring.json

# Verify a signed package
sfsign verify --package mypackage.sfpkg
```

### Key Management

```bash
# Generate a new Ed25519 key pair
sfsign key generate --id "vendor.example" --name "Example Vendor" --keyring keyring.json

# List keys in a keyring
sfsign key list --keyring keyring.json

# Export public key for distribution (safe to share)
sfsign key export-public --id "vendor.example" --keyring keyring.json --output public.json
```

### Signing Packages

```bash
# Sign with author type (package creator)
sfsign sign --package mypackage.sfpkg --key "myvendor" --type author --keyring keyring.json

# Add vendor signature (distribution)
sfsign sign --package mypackage.sfpkg --key "distributor" --type vendor --keyring keyring.json

# Add audit signature (third-party verification)
sfsign sign --package mypackage.sfpkg --key "auditor" --type audit --keyring keyring.json
```

### Verification

```bash
# Basic verification (checks hash and signatures)
sfsign verify --package mypackage.sfpkg

# Verify with trust configuration
sfsign verify --package mypackage.sfpkg --trust trust.json

# Require specific signers
sfsign verify --package mypackage.sfpkg --require "myvendor" --require "auditor"
```

### Trust Configuration

```bash
# Add a key to trusted roots
sfsign trust add --key "myvendor" --keyring keyring.json --config trust.json

# List trusted keys and cross-signings
sfsign trust list --config trust.json
```

### Cross-Signing for Trust Chains

Cross-signing allows a trusted vendor to vouch for another vendor's key, creating trust chains without centralized authorities.

```bash
# Create cross-signing certificate (myvendor vouches for partner)
sfsign cross-sign \
  --issuer "myvendor" \
  --subject "partner.vendor" \
  --keyring keyring.json \
  --output trust.json

# Cross-sign with expiration
sfsign cross-sign \
  --issuer "myvendor" \
  --subject "partner.vendor" \
  --valid-to "2027-01-01" \
  --keyring keyring.json
```

### Signature Types

| Type | Purpose | Use Case |
|------|---------|----------|
| `author` | Package creator | Original developer signs their work |
| `vendor` | Distribution | Distributor vouches for package authenticity |
| `audit` | Third-party | Security auditor confirms code review |

### Manifest Format

Signatures are stored in `.sig.json` files alongside packages:

```json
{
  "version": "1.0",
  "packageId": "mypackage",
  "packageHash": "GSP4PaEWyjVhQ+071f+l...",
  "packageSize": 12345,
  "timestamp": "2026-01-08T18:30:00Z",
  "signatures": [
    {
      "signerId": "myvendor",
      "signerName": "My Vendor",
      "publicKey": "wE47MgQxtBez5t0z+Jgzs...",
      "signature": "jkvkKxSYGT9gNLdLIxUp...",
      "signedAt": "2026-01-08T18:30:00Z",
      "signatureType": "author"
    }
  ]
}
```

### Security Model

- **Ed25519**: Fast, secure, deterministic signatures with 128-bit security
- **SHA-256**: Package integrity verification
- **Multi-signature**: Support for multiple independent signatures
- **Cross-signing**: Decentralized trust without certificate authorities
- **No key escrow**: Private keys never leave your control

## Package Structure

| Package | Description |
|---------|-------------|
| `Mostlylucid.StyloFlow.Core` | Core orchestration, manifests, configuration |
| `Mostlylucid.StyloFlow.Licensing` | Licensing, work unit metering, mesh coordination |
| `Mostlylucid.StyloFlow.Sign` | Package signing CLI tool (`sfsign`) |
| `Mostlylucid.StyloFlow.Retrieval.Core` | Base retrieval interfaces and scoring |
| `Mostlylucid.StyloFlow.Retrieval.Documents` | Document chunking, MMR reranking |
| `Mostlylucid.StyloFlow.Retrieval.Images` | Perceptual hashing, image similarity |
| `Mostlylucid.StyloFlow.Retrieval.Audio` | Audio fingerprinting |
| `Mostlylucid.StyloFlow.Retrieval.Video` | Video fingerprinting |
| `Mostlylucid.StyloFlow.Retrieval.Data` | PII detection, anomaly scoring |
| `Mostlylucid.StyloFlow.Dashboard.Core` | Monitoring dashboard components |
| `Mostlylucid.StyloFlow.Complete` | Metapackage with all components |

## License

This is free and unencumbered software released into the public domain - see [UNLICENSE](UNLICENSE) for details.

---

*Documentation is actively being expanded. Check individual project README files for detailed API documentation.*
