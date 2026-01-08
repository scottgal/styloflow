using System.Text;
using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.Fetchers;

/// <summary>
/// JSON API Fetcher - Fetches JSON from an API and extracts fields.
/// Supports JSONPath-like field extraction and pagination.
/// </summary>
public sealed class JsonApiFetcherAtom
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Probabilistic,
        AtomPersistence.EphemeralOnly,
        name: "json-api",
        reads: ["*"],
        writes: ["api.data", "api.items", "api.count", "api.success", "api.error"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var url = ctx.Config.TryGetValue("url", out var u) ? u?.ToString() : null;
        var method = ctx.Config.TryGetValue("method", out var m) ? m?.ToString()?.ToUpper() ?? "GET" : "GET";
        var dataPath = ctx.Config.TryGetValue("data_path", out var dp) ? dp?.ToString() : null;
        var authHeader = ctx.Config.TryGetValue("auth_header", out var ah) ? ah?.ToString() : null;
        var timeout = GetIntConfig(ctx.Config, "timeout_seconds", 30);

        // Support signal-based URL
        if (string.IsNullOrWhiteSpace(url))
        {
            url = ctx.Signals.Get<string>("url");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            ctx.Log("JSON API: no URL provided");
            ctx.Emit("api.success", false);
            ctx.Emit("api.error", "No URL provided");
            return;
        }

        ctx.Log($"JSON API: {method} {url}");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var request = new HttpRequestMessage(new HttpMethod(method), url);

            // Add standard JSON headers
            request.Headers.Accept.ParseAdd("application/json");

            // Add auth header if provided
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            }

            // Add custom headers
            if (ctx.Config.TryGetValue("headers", out var headersObj))
            {
                AddHeaders(request, headersObj);
            }

            // Add body for POST/PUT
            if (ctx.Config.TryGetValue("body", out var body) && body != null && method is "POST" or "PUT" or "PATCH")
            {
                var bodyStr = body is string s ? s : JsonSerializer.Serialize(body);
                request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
            }

            var response = await SharedClient.SendAsync(request, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                ctx.Log($"JSON API: HTTP {(int)response.StatusCode}");
                ctx.Emit("api.success", false);
                ctx.Emit("api.error", $"HTTP {(int)response.StatusCode}: {responseBody[..Math.Min(200, responseBody.Length)]}");
                return;
            }

            // Parse JSON
            var json = JsonSerializer.Deserialize<JsonElement>(responseBody);

            // Extract data at path if specified
            var data = !string.IsNullOrWhiteSpace(dataPath)
                ? ExtractPath(json, dataPath)
                : json;

            // Count items if array
            var count = data.ValueKind == JsonValueKind.Array ? data.GetArrayLength() : 1;

            ctx.Log($"JSON API: success, {count} items");

            ctx.Emit("api.success", true);
            ctx.Emit("api.data", data);
            ctx.Emit("api.count", count);

            // If array, also emit as items list
            if (data.ValueKind == JsonValueKind.Array)
            {
                ctx.Emit("api.items", data.EnumerateArray().Select(e => (object)e).ToList());
            }
        }
        catch (JsonException ex)
        {
            ctx.Log($"JSON API: parse error - {ex.Message}");
            ctx.Emit("api.success", false);
            ctx.Emit("api.error", $"JSON parse error: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            ctx.Log($"JSON API: timeout after {timeout}s");
            ctx.Emit("api.success", false);
            ctx.Emit("api.error", "Request timeout");
        }
        catch (Exception ex)
        {
            ctx.Log($"JSON API error: {ex.Message}");
            ctx.Emit("api.success", false);
            ctx.Emit("api.error", ex.Message);
        }
    }

    private static JsonElement ExtractPath(JsonElement root, string path)
    {
        var current = root;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
            {
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index))
            {
                var arr = current.EnumerateArray().ToList();
                if (index >= 0 && index < arr.Count)
                {
                    current = arr[index];
                }
            }
            else
            {
                return current; // Path not found, return what we have
            }
        }

        return current;
    }

    private static void AddHeaders(HttpRequestMessage request, object? headersObj)
    {
        if (headersObj is IDictionary<string, object> dict)
        {
            foreach (var (key, value) in dict)
            {
                request.Headers.TryAddWithoutValidation(key, value?.ToString());
            }
        }
        else if (headersObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in je.EnumerateObject())
            {
                request.Headers.TryAddWithoutValidation(prop.Name, prop.Value.ToString());
            }
        }
    }

    private static int GetIntConfig(Dictionary<string, object> config, string key, int defaultValue)
    {
        if (!config.TryGetValue(key, out var val)) return defaultValue;
        return val switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var p) => p,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
            _ => defaultValue
        };
    }
}
