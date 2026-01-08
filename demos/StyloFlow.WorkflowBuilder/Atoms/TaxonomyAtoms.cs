using Mostlylucid.Ephemeral;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms;

/// <summary>
/// Base context for workflow atoms, providing access to signals and services.
/// </summary>
public sealed class WorkflowAtomContext
{
    public required string NodeId { get; init; }
    public required string RunId { get; init; }
    public required WorkflowSignals Signals { get; init; }
    public required OllamaService Ollama { get; init; }
    public required WorkflowStorage Storage { get; init; }
    public Dictionary<string, object> Config { get; init; } = [];

    public void Log(string message)
    {
        // Emit to SignalR coordinator via signal
        Signals.Emit($"signalr.all.log", $"{RunId}|{NodeId}|{message}", NodeId);
    }

    public void Emit(string signal, object? value, double confidence = 1.0)
    {
        Signals.Emit(signal, value, NodeId, confidence);
    }
}

// ============================================================================
// SENSOR ATOMS - Entry points that produce initial signals
// ============================================================================

/// <summary>
/// Timer trigger sensor - fires immediately for demo, would be scheduled in production.
/// Taxonomy: sensor, deterministic, ephemeral
/// </summary>
public sealed class TimerTriggerSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "timer-trigger",
        writes: ["timer.triggered", "timer.timestamp"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        ctx.Log("Timer fired!");
        ctx.Emit("timer.triggered", true);
        ctx.Emit("timer.timestamp", DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}

/// <summary>
/// HTTP receiver sensor - receives webhook data.
/// Taxonomy: sensor, deterministic, ephemeral
/// </summary>
public sealed class HttpReceiverSensor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "http-receiver",
        writes: ["http.received", "http.body", "http.method", "http.path"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var body = ctx.Config.TryGetValue("body", out var b) ? b?.ToString() : "Hello, this is a test message!";
        var method = ctx.Config.TryGetValue("method", out var m) ? m?.ToString() : "POST";
        var path = ctx.Config.TryGetValue("path", out var p) ? p?.ToString() : "/webhook";

        ctx.Log($"Received {method} request to {path}");
        ctx.Log($"Body: {body?[..Math.Min(body.Length, 100)]}...");

        ctx.Emit("http.received", true);
        ctx.Emit("http.body", body);
        ctx.Emit("http.method", method);
        ctx.Emit("http.path", path);

        return Task.CompletedTask;
    }
}

// ============================================================================
// EXTRACTOR ATOMS - Deterministic transformation of data
// ============================================================================

/// <summary>
/// Text analyzer extractor - extracts metrics from raw text.
/// Taxonomy: extractor, deterministic, persistable via escalation
/// </summary>
public sealed class TextAnalyzerExtractor
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Extractor,
        AtomDeterminism.Deterministic,
        AtomPersistence.PersistableViaEscalation,
        name: "text-analyzer",
        reads: ["http.body", "timer.triggered"],
        writes: ["text.analyzed", "text.word_count", "text.char_count", "text.content"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var text = ctx.Signals.Get<string>("http.body") ?? "";

        if (string.IsNullOrWhiteSpace(text))
        {
            ctx.Log("No text to analyze");
            return;
        }

        ctx.Log($"Analyzing text ({text.Length} chars)...");

        var result = await ctx.Ollama.AnalyzeTextAsync(text);

        ctx.Log($"Analysis complete: {result.WordCount} words, {result.SentenceCount} sentences");

        ctx.Emit("text.analyzed", result);
        ctx.Emit("text.word_count", result.WordCount);
        ctx.Emit("text.char_count", result.CharCount);
        ctx.Emit("text.content", result.Content);
    }
}

// ============================================================================
// PROPOSER ATOMS - Probabilistic decisions (use LLM)
// ============================================================================

/// <summary>
/// Sentiment detector proposer - analyzes sentiment using LLM.
/// Taxonomy: proposer, probabilistic, persistable via escalation
/// </summary>
public sealed class SentimentDetectorProposer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Proposer,
        AtomDeterminism.Probabilistic,
        AtomPersistence.PersistableViaEscalation,
        name: "sentiment-detector",
        reads: ["text.content", "text.analyzed"],
        writes: ["sentiment.score", "sentiment.label", "sentiment.confidence"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var text = ctx.Signals.Get<string>("text.content") ?? "";

        if (string.IsNullOrWhiteSpace(text))
        {
            ctx.Log("No text for sentiment analysis");
            return;
        }

        // Check cache first
        var textHash = ComputeHash(text);
        var cached = await ctx.Storage.GetCachedSentimentAsync(textHash);

        SentimentResult result;
        if (cached != null)
        {
            result = cached;
            ctx.Log($"Sentiment (CACHED): {result.Label} ({result.Score:F2})");
        }
        else
        {
            ctx.Log("Analyzing sentiment with TinyLlama...");
            result = await ctx.Ollama.AnalyzeSentimentAsync(text);

            // Cache the result
            await ctx.Storage.CacheSentimentAsync(textHash, result);
            ctx.Log($"Sentiment: {result.Label} ({result.Score:F2}, confidence: {result.Confidence:F2})");
        }

        ctx.Emit("sentiment.score", result.Score, result.Confidence);
        ctx.Emit("sentiment.label", result.Label, result.Confidence);
        ctx.Emit("sentiment.confidence", result.Confidence);

        // Conditional signals
        if (result.Score > 0.3)
            ctx.Emit("sentiment.is_positive", true, result.Confidence);
        if (result.Score < -0.3)
            ctx.Emit("sentiment.is_negative", true, result.Confidence);
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16];
    }
}

// ============================================================================
// CONSTRAINER ATOMS - Validate or gate proposals
// ============================================================================

/// <summary>
/// Threshold filter constrainer - gates signals based on threshold.
/// Taxonomy: constrainer, deterministic, ephemeral
/// </summary>
public sealed class ThresholdFilterConstrainer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Constrainer,
        AtomDeterminism.Deterministic,
        AtomPersistence.EphemeralOnly,
        name: "threshold-filter",
        reads: ["sentiment.score"],
        writes: ["filter.passed", "filter.exceeded", "filter.value"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var threshold = ctx.Config.TryGetValue("threshold", out var t) && t is double d ? d : 0.5;
        var signalKey = ctx.Config.TryGetValue("signal_key", out var sk) ? sk?.ToString() : "sentiment.score";

        var value = ctx.Signals.Get<double>(signalKey ?? "sentiment.score");

        var passed = value >= threshold;
        var exceeded = value > threshold;

        ctx.Log($"Filter check: {signalKey}={value:F2} vs threshold={threshold:F2} -> {(passed ? "PASSED" : "BLOCKED")}");

        ctx.Emit("filter.passed", passed);
        ctx.Emit("filter.exceeded", exceeded);
        ctx.Emit("filter.value", value);

        if (exceeded)
        {
            ctx.Emit("filter.action_required", true);
        }

        return Task.CompletedTask;
    }
}

// ============================================================================
// RENDERER ATOMS - Produce output artifacts
// ============================================================================

/// <summary>
/// Email sender renderer - sends email notifications.
/// Taxonomy: renderer, deterministic, direct write allowed
/// </summary>
public sealed class EmailSenderRenderer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Renderer,
        AtomDeterminism.Deterministic,
        AtomPersistence.DirectWriteAllowed,
        name: "email-sender",
        reads: ["filter.passed", "sentiment.label", "text.content"],
        writes: ["email.sent", "email.message_id"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var filterPassed = ctx.Signals.Get<bool>("filter.passed");
        if (!filterPassed)
        {
            ctx.Log("Filter did not pass - skipping email");
            return Task.CompletedTask;
        }

        var to = ctx.Config.TryGetValue("to", out var t) ? t?.ToString() : "admin@example.com";
        var subjectTemplate = ctx.Config.TryGetValue("subject_template", out var st) ? st?.ToString() : "Alert: {{sentiment.label}}";
        var bodyTemplate = ctx.Config.TryGetValue("body_template", out var bt) ? bt?.ToString() : "Sentiment detected: {{sentiment.label}}";

        var subject = InterpolateTemplate(subjectTemplate!, ctx);
        var body = InterpolateTemplate(bodyTemplate!, ctx);

        var messageId = $"msg-{Guid.NewGuid():N}"[..16];

        ctx.Log($"Sending email to {to}");
        ctx.Log($"  Subject: {subject}");
        ctx.Log($"  Body: {body[..Math.Min(body.Length, 100)]}...");
        ctx.Log($"  Message ID: {messageId}");

        ctx.Emit("email.sent", true);
        ctx.Emit("email.message_id", messageId);

        return Task.CompletedTask;
    }

    private static string InterpolateTemplate(string template, WorkflowAtomContext ctx)
    {
        var result = template;
        foreach (var signal in ctx.Signals.GetAll())
        {
            var placeholder = $"{{{{{signal.Signal}}}}}";
            result = result.Replace(placeholder, signal.Key ?? "");
        }
        return result;
    }
}

/// <summary>
/// Log writer renderer - writes workflow events to storage.
/// Taxonomy: renderer, deterministic, direct write allowed
/// </summary>
public sealed class LogWriterRenderer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Renderer,
        AtomDeterminism.Deterministic,
        AtomPersistence.DirectWriteAllowed,
        name: "log-writer",
        reads: ["*"], // Reads any signal
        writes: ["log.written", "log.entry_id"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var logLevel = ctx.Config.TryGetValue("log_level", out var ll) ? ll?.ToString() : "info";
        var allSignals = ctx.Signals.GetAll();
        var entryId = $"log-{Guid.NewGuid():N}"[..12];

        ctx.Log($"Writing log entry {entryId}");
        ctx.Log($"  Level: {logLevel}");
        ctx.Log($"  Signals captured: {allSignals.Count}");

        // Log key signals
        var sentimentLabel = ctx.Signals.Get<string>("sentiment.label");
        if (sentimentLabel != null)
            ctx.Log($"  sentiment.label = {sentimentLabel}");

        var sentimentScore = ctx.Signals.Get<double>("sentiment.score");
        if (sentimentScore != 0)
            ctx.Log($"  sentiment.score = {sentimentScore:F2}");

        var wordCount = ctx.Signals.Get<int>("text.word_count");
        if (wordCount != 0)
            ctx.Log($"  text.word_count = {wordCount}");

        // Store signals via storage atom
        foreach (var signal in allSignals)
        {
            await ctx.Storage.LogSignalAsync(
                ctx.RunId,
                signal.Signal,
                signal.Key,
                ctx.NodeId,
                1.0);
        }

        ctx.Emit("log.written", true);
        ctx.Emit("log.entry_id", entryId);
    }
}
