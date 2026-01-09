# Creating Custom Atoms

This guide shows how to create custom atoms for the StyloFlow Workflow Builder.

## Atom Structure

Every atom needs two files:
1. **C# Implementation** - The execution logic
2. **YAML Manifest** - The metadata and UI configuration

## Step 1: Create the C# Class

```csharp
// Atoms/MyCategory/MyCustomAtom.cs
using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.MyCategory;

public sealed class MyCustomAtom
{
    // Contract defines the atom's metadata
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,                    // What kind of atom
        AtomDeterminism.Deterministic,         // Same input = same output?
        AtomPersistence.EphemeralOnly,         // How outputs are stored
        name: "my-custom",                     // Must match manifest name
        reads: ["input.signal", "*"],          // What signals it reads
        writes: ["output.result", "output.count"]);  // What signals it emits

    // Main execution method - called when atom runs
    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        // 1. Read input signals
        var input = ctx.Signals.Get<string>("input.signal");

        // 2. Read configuration
        var threshold = GetDoubleConfig(ctx.Config, "threshold", 0.5);
        var mode = ctx.Config.TryGetValue("mode", out var m) ? m?.ToString() : "default";

        // 3. Process
        var result = ProcessData(input, threshold, mode);

        // 4. Log progress (appears in UI)
        ctx.Log($"Processed with threshold={threshold}, mode={mode}");

        // 5. Emit output signals
        ctx.Emit("output.result", result);
        ctx.Emit("output.count", 1);

        return Task.CompletedTask;
    }

    private static string ProcessData(string? input, double threshold, string? mode)
    {
        // Your custom logic here
        return $"Processed: {input}";
    }

    // Helper for reading config values
    private static double GetDoubleConfig(Dictionary<string, object> config, string key, double defaultValue)
    {
        if (!config.TryGetValue(key, out var val)) return defaultValue;

        return val switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s when double.TryParse(s, out var parsed) => parsed,
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetDouble(),
            _ => defaultValue
        };
    }
}
```

## Step 2: Create the YAML Manifest

```yaml
# Manifests/Demo/my-custom.atom.yaml
name: my-custom
kind: extractor
version: 1.0.0
description: |
  Brief description of what this atom does.
  Can be multi-line for longer explanations.

taxonomy:
  kind: extractor
  category: processing

signals:
  triggers:
    - signal: input.signal
      required: true
      description: The input data to process

  emits:
    - key: output.result
      type: string
      description: The processed output

    - key: output.count
      type: integer
      description: Number of items processed

config:
  threshold:
    type: number
    default: 0.5
    description: Processing threshold (0.0-1.0)

  mode:
    type: string
    default: "default"
    options:
      - default
      - strict
      - lenient
    description: Processing mode

examples:
  - name: Basic usage
    config:
      threshold: 0.7
      mode: strict
    description: Process with strict mode and 0.7 threshold

  - name: Lenient processing
    config:
      threshold: 0.3
      mode: lenient
    description: More permissive processing
```

## Step 3: Build and Run

The atom is **automatically discovered** - no manual registration needed!

```bash
dotnet build
dotnet run
```

The atom will appear in the palette under its category.

## Atom Kinds Reference

| Kind | Purpose | When to Use |
|------|---------|-------------|
| `Sensor` | Entry points | Triggers, webhooks, timers |
| `Extractor` | Extract/transform | Parsing, scoring, filtering |
| `Analyzer` | Analysis | Text analysis, ML inference |
| `Proposer` | Decisions | Thresholds, routing |
| `Constrainer` | Validation | Filters, gates |
| `Renderer` | Output | Email, logging, API calls |
| `Coordinator` | Orchestration | Child workflows, keyed execution |

## Determinism

```csharp
AtomDeterminism.Deterministic     // Same input = same output (caching OK)
AtomDeterminism.Probabilistic     // May vary (time-based, random, ML)
```

## Persistence

```csharp
AtomPersistence.EphemeralOnly          // Never persisted
AtomPersistence.PersistableViaEscalation  // Can be promoted
AtomPersistence.DirectWriteAllowed     // Can write directly
```

## Using LLM in Atoms

Access Ollama for LLM processing:

```csharp
public static async Task ExecuteAsync(WorkflowAtomContext ctx)
{
    var text = ctx.Signals.Get<string>("input.text");

    // Call LLM
    var prompt = $"Summarize this text:\n{text}";
    var summary = await ctx.Ollama.GenerateAsync(prompt);

    ctx.Emit("output.summary", summary);
}
```

## Using Sliding Windows

For behavioral analysis:

```csharp
public static Task ExecuteAsync(WorkflowAtomContext ctx)
{
    // Add to window
    var key = ctx.Signals.Get<string>("entity.id") ?? Guid.NewGuid().ToString();
    ctx.Signals.WindowAdd("my-window", key, new { Data = "value" });

    // Sample from window
    var samples = ctx.Signals.WindowSample("my-window", 10);

    // Detect patterns
    var patterns = ctx.Signals.DetectPatterns("my-window", PatternType.Burst);

    return Task.CompletedTask;
}
```

## Testing Your Atom

Create a unit test:

```csharp
[Fact]
public async Task MyCustomAtom_ProcessesInput()
{
    // Arrange
    var signals = new WorkflowSignals("test-run");
    signals.Emit("input.signal", "test data", "test");

    var ctx = new WorkflowAtomContext
    {
        NodeId = "test-node",
        RunId = "test-run",
        Signals = signals,
        Config = new Dictionary<string, object>
        {
            ["threshold"] = 0.7,
            ["mode"] = "strict"
        }
    };

    // Act
    await MyCustomAtom.ExecuteAsync(ctx);

    // Assert
    var result = signals.Get<string>("output.result");
    Assert.NotNull(result);
}
```

## Best Practices

1. **Keep atoms focused** - One responsibility per atom
2. **Validate inputs** - Check for null/invalid signals
3. **Log progress** - Use `ctx.Log()` for visibility
4. **Handle errors gracefully** - Don't crash the workflow
5. **Document signals** - Clear descriptions in YAML
6. **Use typed config helpers** - Avoid casting errors
