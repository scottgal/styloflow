# StyloFlow Workflow Builder Documentation

Visual workflow builder for creating signal-driven data pipelines.

## Tutorials

1. **[Getting Started](01-getting-started.md)** - Quick start guide and core concepts
2. **[Understanding Signals](02-signals.md)** - Deep dive into signal-based communication
3. **[Creating Custom Atoms](03-creating-atoms.md)** - Build your own atoms
4. **[MapReduce Patterns](04-mapreduce.md)** - Document scoring, ranking, and RAG pipelines
5. **[Signal Shapers](05-shapers.md)** - Transform signals like a modular synth

## Quick Reference

### Atom Kinds
| Kind | Purpose |
|------|---------|
| sensor | Entry points (timers, webhooks) |
| analyzer | Process/transform data |
| extractor | Extract features |
| proposer | Make decisions |
| constrainer | Validation/filtering |
| renderer | Output actions |
| shaper | Signal transformation |
| coordinator | Orchestration |

### Common Signals
```
timer.triggered    - Timer fired
http.body         - Received HTTP body
text.analyzed     - Processed text
sentiment.score   - Sentiment analysis result
filter.passed     - Passed threshold check
```

### Running the Builder
```bash
cd demos/StyloFlow.WorkflowBuilder
dotnet run
# Open http://localhost:5000/workflow-builder/
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│            Frontend (Drawflow + Alpine.js)                  │
│   Canvas → Atom Palette → Properties Panel                  │
└─────────────────────┬───────────────────────────────────────┘
                      │ SignalR + REST API
┌─────────────────────▼───────────────────────────────────────┐
│           WorkflowBuilderMiddleware                         │
│   /workflow-builder/api/manifests, workflows, execute       │
└─────────────────────┬───────────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────────┐
│   WorkflowOrchestrator + AtomExecutorRegistry               │
│   Auto-discovers atoms via reflection                       │
└─────────────────────┬───────────────────────────────────────┘
                      │
┌─────────────────────▼───────────────────────────────────────┐
│   Ephemeral SignalSink + EphemeralWorkCoordinator           │
│   Signal-driven execution with backpressure                 │
└─────────────────────────────────────────────────────────────┘
```

## Available Atoms (40+)

### Sensors
- `timer-trigger` - Scheduled execution
- `http-receiver` - Webhook endpoint
- `http-fetch` - Fetch web content
- `json-api` - REST API fetcher

### Analyzers
- `text-analyzer` - Text processing
- `sentiment-detector` - Sentiment analysis

### MapReduce
- `accumulator` - Collect items
- `reducer` - Aggregate values
- `bm25-scorer` - BM25 text scoring
- `tfidf-scorer` - TF-IDF extraction
- `rrf-scorer` - Rank fusion
- `mmr-scorer` - Diversity selection
- `topk-selector` - Top-K selection
- `deduplicator` - Remove duplicates
- `iterative-reducer` - Progressive reduction

### Shapers
- `signal-clamp` - Range limiting
- `signal-filter` - Conditional gating
- `signal-mixer` - Combine signals
- `signal-quantizer` - Discretize values
- `signal-attenuverter` - Scale/invert
- `signal-switch` - Route selection
- `signal-delay` - Hold/delay
- `signal-slew` - Smooth transitions
- `signal-comparator` - Value comparison

### Windows
- `window-collector` - Sliding window collection
- `window-sampler` - Random sampling
- `window-pattern-detector` - Behavioral analysis
- `window-stats` - Window statistics
- `burst-detector` - Rate limiting/burst detection

### Config
- `config-env` - Environment variables
- `config-file` - JSON/YAML files
- `config-db` - Database config
- `config-vault` - Secret management

### Output
- `email-sender` - Send emails
- `log-writer` - Logging

## Contributing

To add a new atom:

1. Create C# class in `Atoms/` with `ExecuteAsync` method
2. Add `Contract` property with metadata
3. Create YAML manifest in `Manifests/Demo/`
4. Atom is auto-discovered on restart!

See [Creating Custom Atoms](03-creating-atoms.md) for details.
