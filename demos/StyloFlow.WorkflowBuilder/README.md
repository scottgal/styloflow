# StyloFlow Workflow Builder

A visual, n8n-style workflow builder for composing StyloFlow atoms into executable workflows. Drag and drop atoms onto a canvas, connect them via signals, and export the workflow definition.

## Quick Start

```bash
dotnet run --project demos/StyloFlow.WorkflowBuilder
```

Open http://localhost:5000/workflow-builder/ in your browser.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│     Frontend (Drawflow + Alpine.js + Tailwind + DaisyUI)    │
│   - Drawflow canvas (nodes, edges, pan/zoom)                │
│   - Palette sidebar (atoms from YAML manifests)             │
│   - Properties panel (node config, signals)                 │
└──────────────────────┬──────────────────────────────────────┘
                       │ REST API + SignalR
┌──────────────────────▼──────────────────────────────────────┐
│              WorkflowBuilderMiddleware                       │
│   Routes: /workflow-builder/api/*                           │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│   Services: ManifestService, WorkflowStore, Validator       │
└─────────────────────────────────────────────────────────────┘
```

## Features

- **Visual Node Editor**: Powered by Drawflow - drag, drop, connect, pan, zoom
- **YAML-Driven Atoms**: Each atom type defined by a YAML manifest
- **Signal-Based Connections**: Connect nodes via typed signals (e.g., `http.body`, `sentiment.score`)
- **Validation**: Real-time validation of signal compatibility between connected nodes
- **Save/Load**: Persist workflows to in-memory store (extensible to databases)
- **Export**: Export workflow definitions as JSON

## Demo Atoms

The builder includes 7 demo atoms organized by taxonomy kind:

### Sensors (Entry Points)
| Atom | Emits | Description |
|------|-------|-------------|
| `timer-trigger` | `timer.triggered`, `timer.timestamp` | Fires at scheduled intervals |
| `http-receiver` | `http.received`, `http.body`, `http.method`, `http.path` | Receives HTTP webhooks |

### Analyzers
| Atom | Requires | Emits | Description |
|------|----------|-------|-------------|
| `text-analyzer` | `http.body` OR `timer.triggered` | `text.analyzed`, `text.word_count`, `text.char_count`, `text.content` | Analyzes text content |
| `sentiment-detector` | `text.analyzed`, `text.content` | `sentiment.score`, `sentiment.label`, `sentiment.confidence` | Detects sentiment |

### Proposers
| Atom | Requires | Emits | Description |
|------|----------|-------|-------------|
| `threshold-filter` | `sentiment.score` | `filter.passed`, `filter.exceeded`, `filter.value` | Gates signals based on threshold |

### Emitters
| Atom | Requires | Emits | Description |
|------|----------|-------|-------------|
| `email-sender` | `filter.passed` | `email.sent`, `email.message_id` | Sends email notifications |
| `log-writer` | Any signal | `log.written`, `log.entry_id` | Logs events for debugging/auditing |

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/workflow-builder/` | Serve the UI |
| GET | `/workflow-builder/api/manifests` | List all available atom manifests |
| GET | `/workflow-builder/api/workflows` | List saved workflows |
| POST | `/workflow-builder/api/workflows` | Save a workflow |
| GET | `/workflow-builder/api/workflows/{id}` | Get a specific workflow |
| DELETE | `/workflow-builder/api/workflows/{id}` | Delete a workflow |
| POST | `/workflow-builder/api/workflows/{id}/validate` | Validate a workflow's connections |

## Data Models

### WorkflowDefinition

```csharp
public record WorkflowDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public List<WorkflowNode> Nodes { get; init; } = [];
    public List<WorkflowEdge> Edges { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
```

### WorkflowNode

```csharp
public record WorkflowNode
{
    public required string Id { get; init; }
    public required string ManifestName { get; init; }  // References atom YAML
    public double X { get; init; }
    public double Y { get; init; }
    public Dictionary<string, object> Config { get; init; } = [];
}
```

### WorkflowEdge

```csharp
public record WorkflowEdge
{
    public required string Id { get; init; }
    public required string SourceNodeId { get; init; }
    public required string SignalKey { get; init; }  // e.g., "sentiment.score"
    public required string TargetNodeId { get; init; }
}
```

## Atom Manifest Structure

Each atom is defined by a YAML manifest following the `ComponentManifest` schema:

```yaml
name: sentiment-detector
description: Detects sentiment from analyzed text
enabled: true
priority: 40

taxonomy:
  kind: analyzer          # sensor | analyzer | proposer | emitter
  determinism: probabilistic
  persistence: ephemeral

input:
  accepts:
    - type: text.analyzed
  requiredSignals:
    - text.analyzed

output:
  produces:
    - type: sentiment.result
  signals:
    - key: sentiment.score
      entityType: number
      salience: 0.9

triggers:
  requires:
    - signal: text.analyzed
    - signal: text.content

emits:
  onComplete:
    - key: sentiment.score
      type: number
      confidenceRange: [-1.0, 1.0]
    - key: sentiment.label
      type: string
  conditional:
    - key: sentiment.is_positive
      type: bool
      when: sentiment.score > 0.3

defaults:
  parameters:
    model: basic
    language: en

tags:
  - sentiment
  - analysis
```

## Validation Rules

The `WorkflowValidator` checks:

1. **Node validity**: Each node references an existing manifest
2. **Edge validity**:
   - Source node must emit the signal
   - Target node must accept or listen to the signal
3. **Required inputs**: Warns if required signals aren't connected

## Extending

### Custom Atoms

Add new YAML files to `Manifests/Demo/`:

```yaml
name: my-custom-atom
description: Does something custom
taxonomy:
  kind: analyzer
# ... rest of manifest
```

Manifests are loaded as embedded resources at startup.

### Custom Storage

Implement `IWorkflowStore` for persistent storage:

```csharp
public class SqlWorkflowStore : IWorkflowStore
{
    public Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync() { ... }
    public Task<WorkflowDefinition?> GetByIdAsync(string id) { ... }
    public Task<WorkflowDefinition> SaveAsync(WorkflowDefinition workflow) { ... }
    public Task<bool> DeleteAsync(string id) { ... }
}
```

Register in DI:
```csharp
services.AddSingleton<IWorkflowStore, SqlWorkflowStore>();
```

## Example Workflow

A typical sentiment analysis workflow:

```
[timer-trigger] ──timer.triggered──► [text-analyzer] ──text.analyzed──► [sentiment-detector]
                                                                              │
                                          sentiment.score                     │
                                              ┌────────────────────────────────┘
                                              ▼
                                     [threshold-filter] ──filter.passed──► [email-sender]
                                              │
                                              └──────any signal────────► [log-writer]
```

## Relationship to sfpkg

The Workflow Builder is a design tool for composing atoms that are distributed via **sfpkg** bundles. See [docs/sfpkg.md](../../docs/sfpkg.md) for the full package format specification.

### How It Fits Together

```
┌─────────────────────────────────────────────────────────────────┐
│                        sfpkg Bundle                             │
│  ┌─────────────────┐  ┌──────────────┐  ┌──────────────────┐   │
│  │  .nupkg         │  │  /js/dist    │  │  /dashboard      │   │
│  │  (atoms,        │  │  (snippets,  │  │  (widgets,       │   │
│  │   molecules)    │  │   collectors)│  │   config UIs)    │   │
│  └─────────────────┘  └──────────────┘  └──────────────────┘   │
│                                                                 │
│  /manifests/*.yaml   ◄── Atom YAML definitions                  │
│  sfpkg.json          ◄── Package manifest + licensing           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
              ┌───────────────────────────────┐
              │      Workflow Builder         │
              │  - Load atoms from sfpkg      │
              │  - Compose into workflows     │
              │  - Export for execution       │
              └───────────────────────────────┘
                              │
                              ▼
              ┌───────────────────────────────┐
              │     Workflow Definition       │
              │  { nodes: [...], edges: [...]}│
              └───────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
    ┌─────────────────┐            ┌─────────────────┐
    │  Source Gen     │            │  Runtime        │
    │  (compile-time) │            │  (interpret)    │
    └─────────────────┘            └─────────────────┘
```

### Workflow Definitions in sfpkg

Composed workflows can themselves become atoms via sfpkg:

```
my-sentiment-pipeline.sfpkg
  /sfpkg.json
  /manifests/
    sentiment-pipeline.molecule.yaml    ◄── The composed workflow as a molecule
  /workflows/
    sentiment-pipeline.workflow.json    ◄── The visual workflow definition
  /dotnet/
    MyCompany.SentimentPipeline.nupkg   ◄── Source-generated runtime code
```

### IStyloflowModule Integration

The workflow builder outputs can be packaged with the standard module interface:

```csharp
public class SentimentPipelineModule : IStyloflowModule
{
    public string Id => "my-company.sentiment-pipeline";
    public Version Version => new(1, 0, 0);

    public void ConfigureServices(IServiceCollection services, IStyloflowModuleContext ctx)
    {
        // Register the composed workflow
        services.AddTransient<SentimentPipelineWorkflow>();

        // Register individual atoms if source-generated
        services.AddTransient<TextAnalyzerAtom>();
        services.AddTransient<SentimentDetectorAtom>();
        // ...
    }
}
```

## Future Directions

- **sfpkg Integration**: Load atoms directly from installed sfpkg bundles
- **Molecule Composition**: Combine atoms into reusable molecules (sub-workflows)
- **Source Generation**: Generate executable code from workflow definitions
- **Prompt-Based Design**: Use LLMs to compose workflows from natural language
- **Self-Assembly**: Workflows that can evolve and optimize their own structure
- **Store Integration**: Browse and install atoms from a package store

## Tech Stack

- **Backend**: ASP.NET Core, SignalR
- **Frontend**: Drawflow (node editor), Alpine.js, Tailwind CSS, DaisyUI
- **Manifests**: YamlDotNet for YAML parsing
- **Storage**: In-memory (demo), extensible to any database
