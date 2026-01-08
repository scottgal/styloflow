using System.Text.Json;
using Microsoft.AspNetCore.Http;
using StyloFlow.Dashboard.Configuration;
using StyloFlow.Dashboard.Models;
using StyloFlow.Dashboard.Services;

namespace StyloFlow.Dashboard.Middleware;

/// <summary>
/// Middleware for handling dashboard routes and API endpoints.
/// </summary>
public class DashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DashboardOptions _options;
    private readonly IDashboardEventStore _eventStore;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DashboardMiddleware(
        RequestDelegate next,
        DashboardOptions options,
        IDashboardEventStore eventStore)
    {
        _next = next;
        _options = options;
        _eventStore = eventStore;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        if (!path.StartsWith(_options.BasePath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Authorization check
        if (_options.AuthorizationFilter != null)
        {
            var authorized = await _options.AuthorizationFilter(context);
            if (!authorized)
            {
                context.Response.StatusCode = 401;
                return;
            }
        }

        var relativePath = path[_options.BasePath.Length..].TrimStart('/');

        var handled = relativePath switch
        {
            "" or "index.html" => await HandleDashboardPage(context),
            "api/events" => await HandleEventsApi(context),
            "api/summary" => await HandleSummaryApi(context),
            "api/timeseries" => await HandleTimeSeriesApi(context),
            "api/export" => await HandleExportApi(context),
            _ => false
        };

        if (!handled)
        {
            await _next(context);
        }
    }

    private async Task<bool> HandleDashboardPage(HttpContext context)
    {
        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(GenerateDashboardHtml());
        return true;
    }

    private async Task<bool> HandleEventsApi(HttpContext context)
    {
        var filter = ParseFilter(context.Request.Query);
        var events = await _eventStore.GetEventsAsync(filter);

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, events, JsonOptions);
        return true;
    }

    private async Task<bool> HandleSummaryApi(HttpContext context)
    {
        var timeWindow = 300;
        if (context.Request.Query.TryGetValue("window", out var windowStr) &&
            int.TryParse(windowStr, out var w))
        {
            timeWindow = w;
        }

        var summary = await _eventStore.GetSummaryAsync(timeWindow);

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, summary, JsonOptions);
        return true;
    }

    private async Task<bool> HandleTimeSeriesApi(HttpContext context)
    {
        var query = context.Request.Query;

        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-1);
        var bucketSize = TimeSpan.FromMinutes(1);

        if (query.TryGetValue("start", out var startStr) &&
            DateTime.TryParse(startStr, out var s))
            startTime = s;

        if (query.TryGetValue("end", out var endStr) &&
            DateTime.TryParse(endStr, out var e))
            endTime = e;

        if (query.TryGetValue("bucket", out var bucketStr) &&
            int.TryParse(bucketStr, out var b))
            bucketSize = TimeSpan.FromSeconds(b);

        var series = await _eventStore.GetTimeSeriesAsync(startTime, endTime, bucketSize);

        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, series, JsonOptions);
        return true;
    }

    private async Task<bool> HandleExportApi(HttpContext context)
    {
        var format = context.Request.Query["format"].FirstOrDefault() ?? "json";
        var filter = ParseFilter(context.Request.Query);
        filter = filter with { Limit = 10000 }; // Allow larger exports

        var events = await _eventStore.GetEventsAsync(filter);

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/csv";
            context.Response.Headers.Append("Content-Disposition", "attachment; filename=events.csv");
            await WriteCsvAsync(context.Response.Body, events);
        }
        else
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.Append("Content-Disposition", "attachment; filename=events.json");
            await JsonSerializer.SerializeAsync(context.Response.Body, events, JsonOptions);
        }

        return true;
    }

    private static DashboardFilter ParseFilter(IQueryCollection query)
    {
        DateTime? startTime = null;
        DateTime? endTime = null;

        if (query.TryGetValue("start", out var startStr) &&
            DateTime.TryParse(startStr, out var s))
            startTime = s;

        if (query.TryGetValue("end", out var endStr) &&
            DateTime.TryParse(endStr, out var e))
            endTime = e;

        var limit = 100;
        if (query.TryGetValue("limit", out var limitStr) &&
            int.TryParse(limitStr, out var l))
            limit = Math.Min(l, 1000);

        var offset = 0;
        if (query.TryGetValue("offset", out var offsetStr) &&
            int.TryParse(offsetStr, out var o))
            offset = o;

        return new DashboardFilter
        {
            StartTime = startTime,
            EndTime = endTime,
            EventTypes = query.TryGetValue("types", out var types)
                ? types.ToString().Split(',').ToList()
                : null,
            Severities = query.TryGetValue("severities", out var sev)
                ? sev.ToString().Split(',').ToList()
                : null,
            Sources = query.TryGetValue("sources", out var sources)
                ? sources.ToString().Split(',').ToList()
                : null,
            MessageContains = query.TryGetValue("search", out var search)
                ? search.ToString()
                : null,
            Limit = limit,
            Offset = offset
        };
    }

    private static async Task WriteCsvAsync(Stream stream, List<DashboardEvent> events)
    {
        using var writer = new StreamWriter(stream, leaveOpen: true);

        await writer.WriteLineAsync("EventId,Timestamp,EventType,Severity,Source,Message,ProcessingTimeMs");

        foreach (var e in events)
        {
            var message = e.Message?.Replace("\"", "\"\"") ?? "";
            await writer.WriteLineAsync(
                $"{e.EventId},{e.Timestamp:O},{e.EventType},{e.Severity},{e.Source ?? ""},\"{message}\",{e.ProcessingTimeMs ?? 0}");
        }
    }

    private string GenerateDashboardHtml()
    {
        return $@"<!DOCTYPE html>
<html lang=""en"" data-theme=""{_options.Theme}"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{_options.Title}</title>
    <link href=""https://cdn.jsdelivr.net/npm/daisyui@4.12.14/dist/full.min.css"" rel=""stylesheet"" />
    <script src=""https://cdn.tailwindcss.com""></script>
    <script src=""https://cdn.jsdelivr.net/npm/alpinejs@3.x.x/dist/cdn.min.js"" defer></script>
    <script src=""https://cdn.jsdelivr.net/npm/echarts@5/dist/echarts.min.js""></script>
    <script src=""https://cdn.jsdelivr.net/npm/tabulator-tables@6/dist/js/tabulator.min.js""></script>
    <link href=""https://cdn.jsdelivr.net/npm/tabulator-tables@6/dist/css/tabulator_midnight.min.css"" rel=""stylesheet"">
    <script src=""https://cdn.jsdelivr.net/npm/@microsoft/signalr@8/dist/browser/signalr.min.js""></script>
</head>
<body class=""min-h-screen bg-base-200"">
    <div x-data=""dashboardState()"" x-init=""init()"" class=""container mx-auto p-4"">
        <!-- Header -->
        <div class=""navbar bg-base-100 rounded-box mb-4"">
            <div class=""flex-1"">
                <span class=""text-xl font-bold"">{_options.Title}</span>
            </div>
            <div class=""flex-none gap-2"">
                <span x-show=""connected"" class=""badge badge-success"">Connected</span>
                <span x-show=""!connected"" class=""badge badge-error"">Disconnected</span>
            </div>
        </div>

        <!-- Summary Cards -->
        <div class=""grid grid-cols-1 md:grid-cols-4 gap-4 mb-4"">
            <div class=""stat bg-base-100 rounded-box"">
                <div class=""stat-title"">Total Events</div>
                <div class=""stat-value"" x-text=""summary.totalEvents"">0</div>
                <div class=""stat-desc"" x-text=""summary.eventsPerSecond.toFixed(2) + '/sec'"">0/sec</div>
            </div>
            <div class=""stat bg-base-100 rounded-box"">
                <div class=""stat-title"">Event Types</div>
                <div class=""stat-value"" x-text=""Object.keys(summary.eventsByType).length"">0</div>
            </div>
            <div class=""stat bg-base-100 rounded-box"">
                <div class=""stat-title"">Avg Processing</div>
                <div class=""stat-value"" x-text=""summary.averageProcessingTimeMs.toFixed(1) + 'ms'"">0ms</div>
            </div>
            <div class=""stat bg-base-100 rounded-box"">
                <div class=""stat-title"">Sources</div>
                <div class=""stat-value"" x-text=""Object.keys(summary.eventsBySource).length"">0</div>
            </div>
        </div>

        <!-- Charts Row -->
        <div class=""grid grid-cols-1 lg:grid-cols-2 gap-4 mb-4"">
            <div class=""card bg-base-100"">
                <div class=""card-body"">
                    <h2 class=""card-title"">Events Timeline</h2>
                    <div id=""timelineChart"" style=""height: 300px;""></div>
                </div>
            </div>
            <div class=""card bg-base-100"">
                <div class=""card-body"">
                    <h2 class=""card-title"">Events by Type</h2>
                    <div id=""typeChart"" style=""height: 300px;""></div>
                </div>
            </div>
        </div>

        <!-- Filters -->
        <div class=""card bg-base-100 mb-4"">
            <div class=""card-body"">
                <div class=""flex flex-wrap gap-4 items-end"">
                    <div class=""form-control"">
                        <label class=""label""><span class=""label-text"">Time Range</span></label>
                        <select class=""select select-bordered"" x-model=""filters.timeRange"" @change=""loadEvents()"">
                            <option value=""5m"">Last 5 minutes</option>
                            <option value=""1h"">Last hour</option>
                            <option value=""24h"">Last 24 hours</option>
                            <option value=""all"">All</option>
                        </select>
                    </div>
                    <div class=""form-control"">
                        <label class=""label""><span class=""label-text"">Severity</span></label>
                        <select class=""select select-bordered"" x-model=""filters.severity"" @change=""loadEvents()"">
                            <option value="""">All</option>
                            <option value=""Info"">Info</option>
                            <option value=""Warning"">Warning</option>
                            <option value=""Error"">Error</option>
                            <option value=""Critical"">Critical</option>
                        </select>
                    </div>
                    <div class=""form-control"">
                        <label class=""label""><span class=""label-text"">Search</span></label>
                        <input type=""text"" class=""input input-bordered"" x-model=""filters.search"" @input.debounce.500ms=""loadEvents()"" placeholder=""Search messages..."">
                    </div>
                    <button class=""btn btn-primary"" @click=""exportData('json')"">Export JSON</button>
                    <button class=""btn btn-secondary"" @click=""exportData('csv')"">Export CSV</button>
                </div>
            </div>
        </div>

        <!-- Events Table -->
        <div class=""card bg-base-100"">
            <div class=""card-body"">
                <h2 class=""card-title"">Events</h2>
                <div id=""eventsTable""></div>
            </div>
        </div>
    </div>

    <script>
        const basePath = '{_options.BasePath}';
        const hubPath = '{_options.HubPath}';

        function dashboardState() {{
            return {{
                connection: null,
                connected: false,
                summary: {{
                    totalEvents: 0,
                    eventsByType: {{}},
                    eventsBySeverity: {{}},
                    eventsBySource: {{}},
                    averageProcessingTimeMs: 0,
                    eventsPerSecond: 0
                }},
                events: [],
                filters: {{
                    timeRange: '1h',
                    severity: '',
                    search: ''
                }},
                timelineChart: null,
                typeChart: null,
                table: null,

                init() {{
                    this.initSignalR();
                    this.initCharts();
                    this.initTable();
                    this.loadEvents();
                }},

                initSignalR() {{
                    this.connection = new signalR.HubConnectionBuilder()
                        .withUrl(hubPath)
                        .withAutomaticReconnect()
                        .build();

                    this.connection.on('BroadcastEvent', (evt) => {{
                        this.events.unshift(evt);
                        if (this.events.length > 100) this.events.pop();
                        this.table?.setData(this.events);
                    }});

                    this.connection.on('BroadcastSummary', (summary) => {{
                        this.summary = summary;
                        this.updateCharts();
                    }});

                    this.connection.onclose(() => this.connected = false);
                    this.connection.onreconnected(() => this.connected = true);

                    this.connection.start()
                        .then(() => this.connected = true)
                        .catch(err => console.error('SignalR error:', err));
                }},

                initCharts() {{
                    this.timelineChart = echarts.init(document.getElementById('timelineChart'));
                    this.typeChart = echarts.init(document.getElementById('typeChart'));

                    this.timelineChart.setOption({{
                        xAxis: {{ type: 'time' }},
                        yAxis: {{ type: 'value' }},
                        series: [{{ name: 'Events', type: 'line', data: [] }}],
                        tooltip: {{ trigger: 'axis' }}
                    }});

                    this.typeChart.setOption({{
                        series: [{{
                            type: 'pie',
                            radius: ['40%', '70%'],
                            data: []
                        }}],
                        tooltip: {{ trigger: 'item' }}
                    }});
                }},

                initTable() {{
                    this.table = new Tabulator('#eventsTable', {{
                        data: this.events,
                        layout: 'fitColumns',
                        pagination: true,
                        paginationSize: 20,
                        columns: [
                            {{ title: 'Time', field: 'timestamp', formatter: 'datetime', formatterParams: {{ outputFormat: 'HH:mm:ss' }} }},
                            {{ title: 'Type', field: 'eventType' }},
                            {{ title: 'Severity', field: 'severity', formatter: (cell) => {{
                                const val = cell.getValue();
                                const colors = {{ Info: 'badge-info', Warning: 'badge-warning', Error: 'badge-error', Critical: 'badge-error' }};
                                return `<span class=""badge ${{colors[val] || ''}}"">$${{val}}</span>`;
                            }} }},
                            {{ title: 'Source', field: 'source' }},
                            {{ title: 'Message', field: 'message', widthGrow: 2 }},
                            {{ title: 'Time (ms)', field: 'processingTimeMs', formatter: (cell) => (cell.getValue() || 0).toFixed(2) }}
                        ]
                    }});
                }},

                updateCharts() {{
                    const typeData = Object.entries(this.summary.eventsByType)
                        .map(([name, value]) => ({{ name, value }}));
                    this.typeChart?.setOption({{ series: [{{ data: typeData }}] }});
                }},

                async loadEvents() {{
                    const params = new URLSearchParams();
                    if (this.filters.timeRange !== 'all') {{
                        const mins = {{ '5m': 5, '1h': 60, '24h': 1440 }}[this.filters.timeRange] || 60;
                        params.set('start', new Date(Date.now() - mins * 60000).toISOString());
                    }}
                    if (this.filters.severity) params.set('severities', this.filters.severity);
                    if (this.filters.search) params.set('search', this.filters.search);

                    const res = await fetch(`${{basePath}}/api/events?${{params}}`);
                    this.events = await res.json();
                    this.table?.setData(this.events);
                }},

                exportData(format) {{
                    const params = new URLSearchParams({{ format }});
                    window.open(`${{basePath}}/api/export?${{params}}`, '_blank');
                }}
            }};
        }}
    </script>
</body>
</html>";
    }
}
