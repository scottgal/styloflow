# Getting Started with StyloFlow Workflow Builder

A visual workflow builder for creating signal-driven data pipelines using draggable atoms connected by signals.

## Quick Start

```bash
# Run the workflow builder
dotnet run --project demos/StyloFlow.WorkflowBuilder

# Open in browser
http://localhost:5000/workflow-builder/
```

## Core Concepts

### Signals
Signals are the communication layer between atoms. They carry typed data through the workflow.

```
[Timer Trigger] --timer.triggered--> [Text Analyzer] --text.analyzed--> [Sentiment Detector]
```

Every atom:
- **Triggers on** specific signals (inputs)
- **Emits** new signals (outputs)

### Atoms
Atoms are the building blocks of workflows. Each atom has a YAML manifest defining its interface:

```yaml
name: text-analyzer
kind: analyzer
signals:
  triggers:
    - signal: http.body
    - signal: timer.triggered
  emits:
    - key: text.analyzed
    - key: text.word_count
```

### Atom Kinds

| Kind | Purpose | Example |
|------|---------|---------|
| **sensor** | Entry points, triggers | timer-trigger, http-receiver |
| **analyzer** | Process/transform data | text-analyzer, sentiment-detector |
| **extractor** | Extract features | topk-selector, deduplicator |
| **proposer** | Make decisions | threshold-filter |
| **renderer** | Output/actions | email-sender, log-writer |
| **shaper** | Transform signals | signal-clamp, signal-mixer |
| **coordinator** | Orchestrate child workflows | coordinator-keyed |

## Building Your First Workflow

### 1. Drag atoms from the palette
Click and drag atoms from the left sidebar onto the canvas.

### 2. Connect with signals
- Drag from an output port (right side) to an input port (left side)
- Connections validate automatically - only compatible signals can connect

### 3. Configure atoms
- Click an atom to select it
- Edit configuration in the properties panel

### 4. Run the workflow
- Click "Run" to execute
- Watch signals flow in real-time
- View logs and output

## Example: Sentiment Analysis Pipeline

```
[Timer Trigger] --> [HTTP Fetch] --> [Text Analyzer] --> [Sentiment Detector] --> [Threshold Filter] --> [Log Writer]
     |                                      |                    |
     +-- timer.triggered                    +-- text.analyzed    +-- sentiment.score
```

Configuration:
- **Timer Trigger**: `interval_seconds: 60`
- **HTTP Fetch**: `url: "https://api.example.com/content"`
- **Threshold Filter**: `threshold: 0.7, operator: "gte"`

## Signal Types

| Type | Description | Example |
|------|-------------|---------|
| boolean | true/false | `burst.detected` |
| number | Numeric value | `sentiment.score` |
| string | Text content | `text.analyzed` |
| array | List of items | `topk.selected` |
| object | Complex data | `api.data` |

## Next Steps

- [Understanding Signals](02-signals.md) - Deep dive into signal-based communication
- [Creating Custom Atoms](03-creating-atoms.md) - Build your own atoms
- [MapReduce Patterns](04-mapreduce.md) - Document scoring and ranking workflows
