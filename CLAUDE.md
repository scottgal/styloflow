# StyloFlow - AI Assistant Guide

## Project Overview

StyloFlow is a declarative orchestration infrastructure library for .NET with YAML-driven configuration, blackboard pattern, and signal-based coordination.

**Package**: `Mostlylucid.StyloFlow`
**Target Frameworks**: net8.0, net9.0, net10.0
**License**: Unlicense (public domain)

## Quick Reference

### Build & Test
```bash
dotnet build                    # Build all projects
dotnet test                     # Run all 531 tests
dotnet test --filter "Name~Bm25" # Run specific tests
```

### Key Directories
```
src/StyloFlow/
├── Configuration/       # ConfigProvider, IConfigProvider
├── Manifests/          # ManifestModels, loaders
├── Entities/           # EntityTypeRegistry, EntityTypeLoader
├── Orchestration/      # ConfiguredComponentBase
├── Retrieval/          # Scoring, similarity, vector math
│   ├── Documents/      # DocumentChunker, MmrReranker
│   ├── Images/         # PerceptualHash, ColorAnalysis
│   ├── Audio/          # AudioFingerprint
│   ├── Video/          # VideoFingerprint, SceneDetector
│   ├── Data/           # PiiDetection, PatternDetection
│   └── Analysis/       # Signal, AnalysisContext
└── Schemas/            # JSON schemas for YAML validation

tests/StyloFlow.Tests/  # Comprehensive unit tests
```

## Core Concepts

### Configuration Hierarchy (3-tier)
1. **appsettings.json** (highest) - Runtime overrides
2. **YAML manifest** - Default values per component
3. **Code defaults** - Fallback values

### Component Manifest (YAML)
```yaml
name: MyComponent
priority: 50
enabled: true
taxonomy:
  kind: sensor|analyzer|proposer|gatekeeper|aggregator|emitter
  determinism: deterministic|probabilistic
  persistence: ephemeral|cached|persisted
triggers:
  requires: [{signal: "signal.name"}]
  skip_when: ["condition"]
emits:
  on_start: ["signal.started"]
  on_complete: [{key: "signal.result", type: double}]
defaults:
  weights: {base: 1.0}
  confidence: {neutral: 0.0}
  timing: {timeout_ms: 100}
  parameters: {custom_key: value}
```

### ConfiguredComponentBase
Base class with shortcuts:
```csharp
// Weights
WeightBase, WeightBotSignal, WeightHumanSignal

// Confidence
ConfidenceNeutral, ConfidenceBotDetected, ConfidenceHighThreshold

// Features/Timing
DetailedLogging, CacheEnabled, TimeoutMs

// Parameters
GetParam<T>("name", default)
GetStringListParam("name")
```

## Retrieval Subsystem

### Text Scoring
- **Bm25Scorer**: BM25 ranking with term frequency and document length normalization
- **TfIdfScorer**: TF-IDF with multiple variants (standard, sublinear, augmented)
- **ReciprocalRankFusion**: Hybrid search combining multiple ranking methods

### Vector Operations (SIMD-optimized)
```csharp
VectorMath.CosineSimilarity(float[] a, float[] b)
VectorMath.EuclideanDistance(float[] a, float[] b)
VectorMath.DotProduct(float[] a, float[] b)
VectorMath.L2Norm(float[] v)
VectorMath.NormalizeInPlace(float[] v)
```

### String Similarity
```csharp
StringSimilarity.JaroWinkler(a, b)
StringSimilarity.Levenshtein(a, b)
StringSimilarity.Jaccard(a, b)
StringSimilarity.NGramSimilarity(a, b, n)
```

### Document Processing
```csharp
// Chunking strategies
DocumentChunker.SlidingWindow(text, windowSize, overlap)
DocumentChunker.BySentence(text, minSize, maxSize)
DocumentChunker.ByParagraph(text, minSize, maxSize)
DocumentChunker.ByMarkdownSection(markdown, maxLevel, maxSize)
DocumentChunker.Recursive(text, targetSize, minSize, maxSize)

// Reranking
MmrReranker.Rerank(query, chunks, lambda, topK)
```

### Image Analysis
```csharp
PerceptualHash.ComputePdqHash(pixels, w, h)
PerceptualHash.ComputeBlockHash(pixels, w, h, blockSize)
PerceptualHash.HammingDistance(hash1, hash2)

ColorAnalysis.ExtractDominantColors(pixels, k)
ColorAnalysis.CalculateColorDiversity(pixels)
```

### Data Analysis
```csharp
// PII Detection
PiiDetection.ScanValues(columnName, values)
PiiDetection.DetectFromColumnName(columnName)
PiiDetection.RedactValue(value, piiType)

// Pattern Detection
PatternDetection.DetectTextPatterns(values)
PatternDetection.ClassifyDistribution(skewness, kurtosis)
PatternDetection.DetectTrend(values)
PatternDetection.DetectPeriodicity(values)

// Anomaly Scoring
AnomalyScoring.ComputeScore(profile)
```

### Analysis Framework
```csharp
// Signal-based analysis
var signal = new Signal {
    Key = "domain.category.metric",
    Source = "AnalyzerName",
    Value = result,
    Confidence = 0.9,
    Tags = [SignalTags.Quality]
};

// Analysis context
var ctx = new AnalysisContext { SelectedRoute = "fast" };
ctx.AddSignal(signal);
ctx.Aggregate("key", AggregationStrategy.HighestConfidence);
```

## Performance Optimizations

### Source-Generated Regex
All regex patterns use `[GeneratedRegex]` for compile-time code generation:
```csharp
[GeneratedRegex(@"pattern")]
private static partial Regex MyRegex();
```

### SIMD Optimization
Vector operations use `System.Numerics.Vector<T>` with automatic fallback:
```csharp
if (Vector.IsHardwareAccelerated && length >= Vector<float>.Count)
    // SIMD path
else
    // Scalar fallback
```

## Entity Type System

### Built-in Entity Types
- `http.request`, `http.response` - HTTP data
- `image.*`, `video.*`, `audio.*` - Media types
- `embedded.vector`, `embedded.multivector` - Embeddings
- `data.json`, `text.plain` - Generic data

### Entity Definition (YAML)
```yaml
entity_types:
  - type: myapp.document
    category: myapp
    persistence: database
    schema:
      format: inline
      inline:
        type: object
        properties:
          id: {type: string}
```

## Common Tasks

### Adding a New Component
1. Create YAML manifest in `manifests/` directory
2. Extend `ConfiguredComponentBase`
3. Use `GetParam<T>()` for configuration
4. Reference triggers/emits in manifest

### Adding Unit Tests
```csharp
[Fact]
public void Method_Scenario_ExpectedResult()
{
    // Arrange
    // Act
    // Assert
}
```

### Running Specific Tests
```bash
dotnet test --filter "FullyQualifiedName~VectorMath"
dotnet test --filter "FullyQualifiedName~PatternDetection"
```

## Dependencies
- `Microsoft.Extensions.Configuration.Abstractions` (9.0.0)
- `Microsoft.Extensions.DependencyInjection.Abstractions` (9.0.0)
- `Microsoft.Extensions.Logging.Abstractions` (9.0.0)
- `YamlDotNet` (16.3.0)

## Test Coverage
- 531 passing unit tests
- Full coverage for Retrieval subsystem
- Test files in `tests/StyloFlow.Tests/`
