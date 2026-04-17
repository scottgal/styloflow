# Changelog

All notable changes to StyloFlow are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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