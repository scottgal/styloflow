# Changelog

All notable changes to StyloFlow are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.8.1] - 2026-07-05

### Fixed

- **StyloFlow.Core**: annotated `AddOnInitSignal<TCoordinator>` and its
  internal `InitSignalBootstrap<TCoordinator>` generic parameters with
  `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]`
  so the AOT / trimming analyzers see that the coordinator type is
  eventually passed to `TryAddSingleton<T>` and `GetRequiredService<T>`.
  Non-blocking IL2091 warnings emitted by the 2.8.0 build gone.

## [2.8.0] - 2026-07-05

### Added

- **StyloFlow.Core: `IInitSignalBus` primitive** for lazy-boot coordinator
  registration. Producers write to sinks unconditionally; coordinators wake
  up on first raise of a named init signal.
  - `IInitSignalBus` + `InitSignalBus` in `StyloFlow.Orchestration` — once-per-signal
    semantics, `Subscribe` after fire runs handler immediately, handler
    exceptions swallowed so one bad factory cannot poison others.
  - DI helpers: `services.AddInitSignalBus()` + `services.AddOnInitSignal<TCoordinator>(initSignal)`.
    Coordinator is registered as an ordinary singleton whose construction is
    deferred; an internal `InitSignalBootstrap<T>` hosted service subscribes
    at boot and resolves the coordinator via
    `sp.GetRequiredService<T>()` on first raise.
  - Introduces a `Microsoft.Extensions.Hosting.Abstractions` package
    reference (~35KB, no runtime cost when the host has no hosted services).

Tests: 688/688 pass. 10 new `InitSignalBusTests` cover idempotent raise,
subscribe-before + subscribe-after semantics, multiple-handler fan-out,
dispose-before-fire, exception isolation, singleton registration, deferred
construction, race-safe when producer beats bootstrap.

Consumers (e.g. stylobot's learning fabric, session store, LLM
classification) migrate coordinator lifecycles to this primitive by
registering with `AddOnInitSignal<T>` and hooking their shared sink to
raise the init signal on first write. See stylobot's
`SINK_COORDINATOR_ARCHITECTURE.md` for the target end-to-end shape.

## [2.6.1] - 2026-05-14

### Changed

- **StyloFlow.Core**: marked `IsAotCompatible=true` and annotated reflection-using
  entry points with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`.
  Consumers (e.g. stylobot's NativeAOT publish) no longer get the rollup
  `IL3053 Assembly 'StyloFlow.Core' produced AOT analysis warnings`.
  - YamlDotNet reflection-based deserializer paths annotated:
    `EmbeddedManifestLoader`, `FileSystemManifestLoader`, `EntityTypeLoader`,
    `AddStyloFlow*` extensions.
  - `IConfigProvider.GetParameter<T>` (and concrete impl + `ConfiguredComponentBase.GetParam<T>`)
    annotated; primitive call sites (`IsFeatureEnabled`, `IsOptional`,
    `TriggerTimeout`) carry `[UnconditionalSuppressMessage]` because binding
    bool / int doesn't need reflection.
  - `AddStyloFlowModulesFromAssemblies` (Assembly.GetTypes + Activator.CreateInstance)
    annotated; doc points operators at the build-time `AddStyloFlowModule`
    registration pattern for AOT scenarios.

Tests: 675/675 pass. Internal AOT analyzer: 0 warnings.

## [2.4.0] - 2026-02-02

### Added

- Bot detection endpoints and enhanced similarity search integration
- Pipeline architecture documentation covering core concepts, molecule processing, evidence storage, query decomposition, and GraphRAG integration

## [2.3.0] - 2026-01-09

### Added

- **StyloFlow.Ingestion** - `IngestionJob`, `IngestionJobRunner`, and foundational components for job execution, signal emission, error handling, and incremental sync
- **StyloFlow.Converters.OpenXml** - OpenXml document conversion support
- **StyloFlow.Converters.PdfPig** - PDF document conversion with refactored table extraction and fallback strategies
- **StyloFlow.Retrieval.Core** - `EntityBuilder`, `RetrievalEntity`, `WaveCoordinator`, and `WaveManifest` for cross-modal retrieval and wave orchestration
- Support for pipeline, wave, and coordinator manifest deserialization
- `AnalysisState` class for orchestrating immutable analysis snapshots
- `ITriggerableComponent` for trigger-based execution
- `ConfiguredComponentBase` enhancements: signals, triggers, and execution timeouts

### Changed

- Centralized package version management via `Directory.Packages.props`
- CI/CD refactoring: simplified build steps, excluded `StyloFlow.Complete` from Release builds
- Replaced `Mostlylucid.Ephemeral.Complete` dependency with individual `Mostlylucid.Ephemeral` packages

## [2.0.0] - 2026-01-08

### Added

- **StyloFlow.WorkflowBuilder** - in-memory workflow storage, runtime models, workflow manifests, and core components for atom execution
  - New atoms: HTTP fetcher, deduplicator, TF-IDF scorer, JSON API fetcher, signal router, keyed coordinator
  - Shapers, sensors, and configuration components
  - Sample workflow loader and automatic atom discovery
  - Comprehensive documentation and examples
- **StyloFlow.Licensing** - licensing tiers, work unit metering, mesh coordination
- **StyloFlow.Dashboard.Core** - monitoring dashboard components
- `BurstDetectorAtom` for per-identity burst detection with YAML manifest
- Document retrieval utilities: `MmrReranker`, `DocumentChunker`, `VectorMath`
- Input/output contracts for manifest validation
- Comprehensive unit tests for manifest loaders and configuration providers

### Changed

- Removed outdated analysis, retrieval, and audio modules (`AnalysisContext`, `AnomalyScoring`, `AudioFingerprint`, `Bm25Scorer`)
- Updated NuGet publishing workflow with OIDC trusted publishing, improved version handling, and artifact uploads
- Switched to embedded debug symbols (removed separate symbol packages)

## [1.0.0] - 2025-12-15

### Added

- Initial release of StyloFlow
- Declarative orchestration with YAML-driven component manifests
- `ComponentManifest` for YAML-based component definition
- `ConfiguredComponentBase` with configuration shortcuts and `GetParam<T>()`
- `IManifestLoader` with FileSystem and Embedded resource implementations
- `IConfigProvider` for 3-tier configuration resolution (appsettings.json > YAML > code)
- Entity type system with built-in types (http, image, video, audio, document, behavioral, network, detection, embedded, persistence, data)
- Signal-based orchestration with triggers, emitted signals, and escalation
- Taxonomy classification and execution lanes
- Budget constraints for resource management
- YAML schema validation with JSON Schema definitions
- Package signing and supply chain security via `sfsign` CLI tool (Ed25519)