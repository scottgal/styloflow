using Microsoft.Data.Sqlite;
using Mostlylucid.Ephemeral;

namespace StyloFlow.WorkflowBuilder.Runtime;

/// <summary>
/// Workflow storage using SQLite directly with signal-driven patterns.
/// Follows Ephemeral's data atom patterns - emits signals on save/load.
/// </summary>
public sealed class WorkflowStorage : IAsyncDisposable
{
    private readonly SignalSink _signals;
    private readonly SqliteConnection _runsConnection;
    private readonly SqliteConnection _signalLogConnection;
    private readonly SqliteConnection _sentimentCacheConnection;
    private bool _initialized;

    public WorkflowStorage(SignalSink signals, string dataPath = "./data")
    {
        _signals = signals;
        Directory.CreateDirectory(dataPath);

        _runsConnection = new SqliteConnection($"Data Source={dataPath}/workflow_runs.db");
        _signalLogConnection = new SqliteConnection($"Data Source={dataPath}/signal_log.db");
        _sentimentCacheConnection = new SqliteConnection($"Data Source={dataPath}/sentiment_cache.db");
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _runsConnection.OpenAsync();
        await _signalLogConnection.OpenAsync();
        await _sentimentCacheConnection.OpenAsync();

        // Create tables
        await ExecuteNonQueryAsync(_runsConnection, """
            CREATE TABLE IF NOT EXISTS workflow_runs (
                run_id TEXT PRIMARY KEY,
                workflow_id TEXT NOT NULL,
                workflow_name TEXT NOT NULL,
                input_json TEXT,
                output_json TEXT,
                started_at TEXT NOT NULL,
                completed_at TEXT,
                status TEXT NOT NULL
            )
        """);

        await ExecuteNonQueryAsync(_signalLogConnection, """
            CREATE TABLE IF NOT EXISTS signal_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                signal_key TEXT NOT NULL,
                value_json TEXT,
                source_node TEXT NOT NULL,
                confidence REAL NOT NULL,
                timestamp TEXT NOT NULL
            )
        """);

        await ExecuteNonQueryAsync(_sentimentCacheConnection, """
            CREATE TABLE IF NOT EXISTS sentiment_cache (
                text_hash TEXT PRIMARY KEY,
                score REAL NOT NULL,
                label TEXT NOT NULL,
                confidence REAL NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL
            )
        """);

        _initialized = true;
        _signals.Raise("storage.initialized", "workflow_storage");
    }

    /// <summary>
    /// Start a new workflow run.
    /// </summary>
    public async Task<string> StartRunAsync(string workflowId, string workflowName, string? inputJson)
    {
        var runId = $"run-{Guid.NewGuid():N}"[..16];

        using var cmd = _runsConnection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO workflow_runs (run_id, workflow_id, workflow_name, input_json, started_at, status)
            VALUES (@runId, @workflowId, @workflowName, @inputJson, @startedAt, 'running')
        """;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@workflowId", workflowId);
        cmd.Parameters.AddWithValue("@workflowName", workflowName);
        cmd.Parameters.AddWithValue("@inputJson", inputJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@startedAt", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        _signals.Raise("workflow.run.started", runId);
        return runId;
    }

    /// <summary>
    /// Complete a workflow run.
    /// </summary>
    public async Task CompleteRunAsync(string runId, string status, string? outputJson)
    {
        using var cmd = _runsConnection.CreateCommand();
        cmd.CommandText = """
            UPDATE workflow_runs
            SET status = @status, output_json = @outputJson, completed_at = @completedAt
            WHERE run_id = @runId
        """;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@outputJson", outputJson ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@completedAt", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        _signals.Raise($"workflow.run.{status}", runId);
    }

    /// <summary>
    /// Log a signal during execution.
    /// </summary>
    public async Task LogSignalAsync(string runId, string signalKey, object? value, string sourceNode, double confidence)
    {
        using var cmd = _signalLogConnection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO signal_log (run_id, signal_key, value_json, source_node, confidence, timestamp)
            VALUES (@runId, @signalKey, @valueJson, @sourceNode, @confidence, @timestamp)
        """;
        cmd.Parameters.AddWithValue("@runId", runId);
        cmd.Parameters.AddWithValue("@signalKey", signalKey);
        cmd.Parameters.AddWithValue("@valueJson", value != null ? System.Text.Json.JsonSerializer.Serialize(value) : DBNull.Value);
        cmd.Parameters.AddWithValue("@sourceNode", sourceNode);
        cmd.Parameters.AddWithValue("@confidence", confidence);
        cmd.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Get cached sentiment result.
    /// </summary>
    public async Task<SentimentResult?> GetCachedSentimentAsync(string textHash)
    {
        using var cmd = _sentimentCacheConnection.CreateCommand();
        cmd.CommandText = "SELECT score, label, confidence, expires_at FROM sentiment_cache WHERE text_hash = @hash";
        cmd.Parameters.AddWithValue("@hash", textHash);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var expiresAt = DateTimeOffset.Parse(reader.GetString(3));
        if (expiresAt < DateTimeOffset.UtcNow)
            return null;

        return new SentimentResult
        {
            Score = reader.GetDouble(0),
            Label = reader.GetString(1),
            Confidence = reader.GetDouble(2),
            FromCache = true
        };
    }

    /// <summary>
    /// Cache a sentiment result.
    /// </summary>
    public async Task CacheSentimentAsync(string textHash, SentimentResult result)
    {
        using var cmd = _sentimentCacheConnection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO sentiment_cache (text_hash, score, label, confidence, created_at, expires_at)
            VALUES (@hash, @score, @label, @confidence, @createdAt, @expiresAt)
        """;
        cmd.Parameters.AddWithValue("@hash", textHash);
        cmd.Parameters.AddWithValue("@score", result.Score);
        cmd.Parameters.AddWithValue("@label", result.Label);
        cmd.Parameters.AddWithValue("@confidence", result.Confidence);
        cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@expiresAt", DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        _signals.Raise("sentiment.cached", textHash);
    }

    /// <summary>
    /// Get recent workflow runs.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowRunData>> GetRecentRunsAsync(int limit = 20)
    {
        var results = new List<WorkflowRunData>();
        using var cmd = _runsConnection.CreateCommand();
        cmd.CommandText = "SELECT run_id, workflow_id, workflow_name, input_json, output_json, started_at, completed_at, status FROM workflow_runs ORDER BY started_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new WorkflowRunData
            {
                RunId = reader.GetString(0),
                WorkflowId = reader.GetString(1),
                WorkflowName = reader.GetString(2),
                InputJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                OutputJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                StartedAt = DateTimeOffset.Parse(reader.GetString(5)),
                CompletedAt = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6)),
                Status = reader.GetString(7)
            });
        }

        return results;
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _runsConnection.DisposeAsync();
        await _signalLogConnection.DisposeAsync();
        await _sentimentCacheConnection.DisposeAsync();
    }
}

public sealed record WorkflowRunData
{
    public required string RunId { get; init; }
    public required string WorkflowId { get; init; }
    public required string WorkflowName { get; init; }
    public string? InputJson { get; init; }
    public string? OutputJson { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required string Status { get; init; }
}
