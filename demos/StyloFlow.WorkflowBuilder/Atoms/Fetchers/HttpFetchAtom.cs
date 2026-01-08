using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Mostlylucid.Ephemeral.Atoms.Taxonomy;
using StyloFlow.WorkflowBuilder.Runtime;

namespace StyloFlow.WorkflowBuilder.Atoms.Fetchers;

/// <summary>
/// HTTP Fetch - Fetches content from a URL.
/// Supports GET/POST/PUT/DELETE with configurable headers and body.
/// </summary>
public sealed class HttpFetchAtom
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Sensor,
        AtomDeterminism.Probabilistic,
        AtomPersistence.EphemeralOnly,
        name: "http-fetch",
        reads: ["*"],
        writes: ["fetch.response", "fetch.status", "fetch.headers", "fetch.content_type", "fetch.success"]);

    public static async Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var url = ctx.Config.TryGetValue("url", out var u) ? u?.ToString() : null;
        var method = ctx.Config.TryGetValue("method", out var m) ? m?.ToString()?.ToUpper() ?? "GET" : "GET";
        var body = ctx.Config.TryGetValue("body", out var b) ? b?.ToString() : null;
        var contentType = ctx.Config.TryGetValue("content_type", out var ct) ? ct?.ToString() ?? "application/json" : "application/json";
        var timeout = GetIntConfig(ctx.Config, "timeout_seconds", 30);

        // Support signal-based URL
        if (string.IsNullOrWhiteSpace(url))
        {
            url = ctx.Signals.Get<string>("url");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            ctx.Log("HTTP Fetch: no URL provided");
            ctx.Emit("fetch.success", false);
            ctx.Emit("fetch.status", 0);
            ctx.Emit("fetch.response", "No URL provided");
            return;
        }

        ctx.Log($"HTTP Fetch: {method} {url}");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var request = new HttpRequestMessage(new HttpMethod(method), url);

            // Add headers from config
            if (ctx.Config.TryGetValue("headers", out var headersObj))
            {
                AddHeaders(request, headersObj);
            }

            // Add body for POST/PUT
            if (!string.IsNullOrWhiteSpace(body) && method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            var response = await SharedClient.SendAsync(request, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

            ctx.Log($"HTTP Fetch: {(int)response.StatusCode} {response.StatusCode} ({responseBody.Length} bytes)");

            ctx.Emit("fetch.success", response.IsSuccessStatusCode);
            ctx.Emit("fetch.status", (int)response.StatusCode);
            ctx.Emit("fetch.response", responseBody);
            ctx.Emit("fetch.content_type", response.Content.Headers.ContentType?.MediaType ?? "unknown");
            ctx.Emit("fetch.headers", response.Headers
                .ToDictionary(h => h.Key, h => string.Join(", ", h.Value)));
        }
        catch (TaskCanceledException)
        {
            ctx.Log($"HTTP Fetch: timeout after {timeout}s");
            ctx.Emit("fetch.success", false);
            ctx.Emit("fetch.status", 408);
            ctx.Emit("fetch.response", "Request timeout");
        }
        catch (Exception ex)
        {
            ctx.Log($"HTTP Fetch error: {ex.Message}");
            ctx.Emit("fetch.success", false);
            ctx.Emit("fetch.status", 0);
            ctx.Emit("fetch.response", ex.Message);
        }
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
