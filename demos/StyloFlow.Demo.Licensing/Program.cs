using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mostlylucid.Ephemeral;
using StyloFlow.Licensing;
using StyloFlow.Licensing.Components;
using StyloFlow.Licensing.Models;
using StyloFlow.Licensing.Services;
using System.Collections.Concurrent;
using System.Diagnostics;

Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       StyloFlow Licensing - Integration Test Demo             ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝\n");

var testResults = new ConcurrentBag<TestResult>();
var signalsReceived = new ConcurrentBag<string>();

// Parse command line args
var mode = args.Contains("--licensed") ? "licensed" :
           args.Contains("--expiring") ? "expiring" :
           args.Contains("--throttle") ? "throttle" :
           "free";

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Usage: StyloFlow.Demo.Licensing [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --licensed    Run with full licensing (professional tier)");
    Console.WriteLine("  --expiring    Run with license expiring soon");
    Console.WriteLine("  --throttle    Run with low limits to test throttling");
    Console.WriteLine("  --free        Run in free tier mode (default)");
    Console.WriteLine("  --help, -h    Show this help");
    return 0;
}

Console.WriteLine($"Test Mode: {mode.ToUpperInvariant()}\n");

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddConsole();

// Configure based on mode
switch (mode)
{
    case "licensed":
        ConfigureLicensedMode(builder.Services, testResults);
        break;
    case "expiring":
        ConfigureExpiringMode(builder.Services, testResults);
        break;
    case "throttle":
        ConfigureThrottleMode(builder.Services, testResults);
        break;
    default:
        ConfigureFreeMode(builder.Services, testResults);
        break;
}

// Register test components
builder.Services.AddSingleton<DocumentProcessor>();
builder.Services.AddSingleton<ImageAnalyzer>();
builder.Services.AddSingleton<DataValidator>();
builder.Services.AddSingleton<PremiumFeature>();

var host = builder.Build();

// Get services
var licenseManager = host.Services.GetRequiredService<ILicenseManager>();
var workUnitMeter = host.Services.GetRequiredService<IWorkUnitMeter>();
var signalSink = host.Services.GetRequiredService<SignalSink>();

// Subscribe to all signals for testing
signalSink.Subscribe(signal =>
{
    signalsReceived.Add(signal.Signal);
});

// Start host if in licensed mode
CancellationTokenSource? cts = null;
Task? hostTask = null;

if (mode != "free")
{
    cts = new CancellationTokenSource();
    hostTask = host.StartAsync(cts.Token);
    await Task.Delay(2500); // Wait for coordinator to start and emit heartbeat
}

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("                    TEST 1: License Status");
Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

await TestLicenseStatus(licenseManager, workUnitMeter, mode, testResults);

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("                    TEST 2: Feature Access");
Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

TestFeatureAccess(licenseManager, mode, testResults);

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("                    TEST 3: Tier Requirements");
Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

TestTierRequirements(licenseManager, mode, testResults);

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("                    TEST 4: Work Unit Metering");
Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

await TestWorkUnitMetering(workUnitMeter, mode, testResults);

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("                    TEST 5: Licensed Components");
Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

var docProcessor = host.Services.GetRequiredService<DocumentProcessor>();
var imageAnalyzer = host.Services.GetRequiredService<ImageAnalyzer>();
var dataValidator = host.Services.GetRequiredService<DataValidator>();
var premiumFeature = host.Services.GetRequiredService<PremiumFeature>();

TestLicensedComponents(docProcessor, imageAnalyzer, dataValidator, premiumFeature, mode, testResults);

Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
Console.WriteLine("                    TEST 6: Component Operations");
Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

await TestComponentOperations(docProcessor, imageAnalyzer, dataValidator, workUnitMeter, mode, testResults);

if (mode != "free")
{
    Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
    Console.WriteLine("                    TEST 7: Signal Emission");
    Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

    TestSignalEmission(signalsReceived, mode, testResults);
}

if (mode == "throttle")
{
    Console.WriteLine("\n═══════════════════════════════════════════════════════════════");
    Console.WriteLine("                    TEST 8: Throttling Behavior");
    Console.WriteLine("═══════════════════════════════════════════════════════════════\n");

    await TestThrottling(docProcessor, workUnitMeter, testResults);
}

// Shutdown
if (cts != null)
{
    cts.Cancel();
    try { await hostTask!; } catch (OperationCanceledException) { }
}

// Print results
Console.WriteLine("\n╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║                      TEST RESULTS                             ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝\n");

var passed = testResults.Count(r => r.Passed);
var failed = testResults.Count(r => !r.Passed);

foreach (var result in testResults.OrderBy(r => r.Name))
{
    var status = result.Passed ? "PASS" : "FAIL";
    var color = result.Passed ? ConsoleColor.Green : ConsoleColor.Red;
    Console.ForegroundColor = color;
    Console.Write($"  [{status}] ");
    Console.ResetColor();
    Console.WriteLine($"{result.Name}");
    if (!result.Passed && !string.IsNullOrEmpty(result.Message))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"         {result.Message}");
        Console.ResetColor();
    }
}

Console.WriteLine();
Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
Console.WriteLine($"  Total: {passed} passed, {failed} failed");
Console.ResetColor();

return failed == 0 ? 0 : 1;

// ============================================================================
// Configuration Methods
// ============================================================================

void ConfigureLicensedMode(IServiceCollection services, ConcurrentBag<TestResult> results)
{
    services.AddStyloFlow(options =>
    {
        options.LicenseToken = CreateLicenseJson("professional", 100, 1000, 30);
        options.HeartbeatInterval = TimeSpan.FromSeconds(2);
        options.WorkUnitThresholds = [50, 80, 90, 100];

        options.OnLicenseStateChanged = evt =>
        {
            Console.WriteLine($"  [EVENT] License: {evt.PreviousState} -> {evt.NewState}");
        };

        options.OnWorkUnitThreshold = evt =>
        {
            Console.WriteLine($"  [EVENT] Threshold {evt.ThresholdPercent}%: {evt.CurrentWorkUnits:F1}/{evt.MaxWorkUnits}");
        };
    });
}

void ConfigureExpiringMode(IServiceCollection services, ConcurrentBag<TestResult> results)
{
    services.AddStyloFlow(options =>
    {
        // Expiring in 5 days (within default 7-day grace period)
        options.LicenseToken = CreateLicenseJson("starter", 50, 500, 5);
        options.HeartbeatInterval = TimeSpan.FromSeconds(2);
        options.LicenseGracePeriod = TimeSpan.FromDays(7);

        options.OnLicenseStateChanged = evt =>
        {
            Console.WriteLine($"  [EVENT] License: {evt.PreviousState} -> {evt.NewState}");
        };
    });
}

void ConfigureThrottleMode(IServiceCollection services, ConcurrentBag<TestResult> results)
{
    services.AddStyloFlow(options =>
    {
        // Very low limits to trigger throttling
        options.LicenseToken = CreateLicenseJson("starter", 10, 20, 30);
        options.HeartbeatInterval = TimeSpan.FromSeconds(1);
        options.WorkUnitThresholds = [50, 80, 90, 100];

        options.OnWorkUnitThreshold = evt =>
        {
            Console.WriteLine($"  [THROTTLE] {evt.ThresholdPercent}% - {evt.CurrentWorkUnits:F1}/{evt.MaxWorkUnits} WU");
        };
    });
}

void ConfigureFreeMode(IServiceCollection services, ConcurrentBag<TestResult> results)
{
    services.AddStyloFlowFree();
}

string CreateLicenseJson(string tier, int slots, int workUnits, int expiryDays)
{
    var now = DateTimeOffset.UtcNow;
    var expiry = now.AddDays(expiryDays);

    return $$"""
    {
        "licenseId": "test-{{tier}}-001",
        "issuedTo": "integration-test@styloflow.dev",
        "issuedAt": "{{now:O}}",
        "expiry": "{{expiry:O}}",
        "tier": "{{tier}}",
        "features": ["documents.*", "images.*", "data.*", "premium.analytics"],
        "limits": {
            "maxMoleculeSlots": {{slots}},
            "maxWorkUnitsPerMinute": {{workUnits}},
            "maxNodes": 5
        }
    }
    """;
}

// ============================================================================
// Test Methods
// ============================================================================

async Task TestLicenseStatus(ILicenseManager lm, IWorkUnitMeter wum, string mode, ConcurrentBag<TestResult> results)
{
    Console.WriteLine($"  License State:    {lm.CurrentState}");
    Console.WriteLine($"  License Tier:     {lm.CurrentTier}");
    Console.WriteLine($"  Max Slots:        {lm.MaxSlots}");
    Console.WriteLine($"  Max WU/min:       {lm.MaxWorkUnitsPerMinute}");
    Console.WriteLine($"  Expiring Soon:    {lm.IsExpiringSoon}");
    Console.WriteLine($"  Time Until Expiry: {lm.TimeUntilExpiry}");
    Console.WriteLine($"  Features:         [{string.Join(", ", lm.EnabledFeatures)}]");

    // Validate license async
    var result = await lm.ValidateLicenseAsync();
    Console.WriteLine($"  Validation:       {(result.Valid ? "Valid" : "Invalid")} - {result.ErrorMessage ?? "OK"}");

    switch (mode)
    {
        case "licensed":
            results.Add(new("License.State.Valid", lm.CurrentState == LicenseState.Valid));
            results.Add(new("License.Tier.Professional", lm.CurrentTier == "professional"));
            results.Add(new("License.MaxSlots.100", lm.MaxSlots == 100));
            results.Add(new("License.MaxWU.1000", lm.MaxWorkUnitsPerMinute == 1000));
            results.Add(new("License.NotExpiring", !lm.IsExpiringSoon));
            break;

        case "expiring":
            results.Add(new("License.State.ExpiringSoon",
                lm.CurrentState == LicenseState.ExpiringSoon || lm.CurrentState == LicenseState.Valid));
            results.Add(new("License.IsExpiringSoon", lm.IsExpiringSoon));
            break;

        case "throttle":
            results.Add(new("License.MaxWU.Low", lm.MaxWorkUnitsPerMinute == 20));
            break;

        case "free":
            results.Add(new("License.State.FreeTier", lm.CurrentState == LicenseState.FreeTier));
            results.Add(new("License.Tier.Free", lm.CurrentTier == "free"));
            break;
    }
}

void TestFeatureAccess(ILicenseManager lm, string mode, ConcurrentBag<TestResult> results)
{
    var features = new[] { "documents.parse", "images.analyze", "data.validate", "premium.analytics", "enterprise.mesh" };

    foreach (var feature in features)
    {
        var hasFeature = lm.HasFeature(feature);
        Console.WriteLine($"  {feature,-25} {(hasFeature ? "YES" : "NO")}");
    }

    if (mode == "licensed" || mode == "expiring" || mode == "throttle")
    {
        results.Add(new("Feature.Documents", lm.HasFeature("documents.parse")));
        results.Add(new("Feature.Images", lm.HasFeature("images.analyze")));
        results.Add(new("Feature.Data", lm.HasFeature("data.validate")));
        results.Add(new("Feature.Premium", lm.HasFeature("premium.analytics")));
        results.Add(new("Feature.Enterprise.Denied", !lm.HasFeature("enterprise.mesh")));
    }
    else
    {
        results.Add(new("Feature.FreeTier.NoFeatures", !lm.HasFeature("documents.parse")));
    }
}

void TestTierRequirements(ILicenseManager lm, string mode, ConcurrentBag<TestResult> results)
{
    var tiers = new[] { "free", "starter", "professional", "enterprise" };

    foreach (var tier in tiers)
    {
        var meets = lm.MeetsTierRequirement(tier);
        Console.WriteLine($"  Meets '{tier,-12}' requirement: {(meets ? "YES" : "NO")}");
    }

    switch (mode)
    {
        case "licensed":
            results.Add(new("Tier.MeetsFree", lm.MeetsTierRequirement("free")));
            results.Add(new("Tier.MeetsStarter", lm.MeetsTierRequirement("starter")));
            results.Add(new("Tier.MeetsProfessional", lm.MeetsTierRequirement("professional")));
            results.Add(new("Tier.NotEnterprise", !lm.MeetsTierRequirement("enterprise")));
            break;

        case "expiring":
        case "throttle":
            results.Add(new("Tier.MeetsStarter", lm.MeetsTierRequirement("starter")));
            results.Add(new("Tier.NotProfessional", !lm.MeetsTierRequirement("professional")));
            break;

        case "free":
            results.Add(new("Tier.MeetsFree", lm.MeetsTierRequirement("free")));
            results.Add(new("Tier.NotStarter", !lm.MeetsTierRequirement("starter")));
            break;
    }
}

async Task TestWorkUnitMetering(IWorkUnitMeter wum, string mode, ConcurrentBag<TestResult> results)
{
    var initialWU = wum.CurrentWorkUnits;
    Console.WriteLine($"  Initial Work Units: {initialWU:F1}");
    Console.WriteLine($"  Max Work Units:     {wum.MaxWorkUnits:F1}");
    Console.WriteLine($"  Percent Used:       {wum.PercentUsed:F1}%");
    Console.WriteLine($"  Is Throttling:      {wum.IsThrottling}");
    Console.WriteLine($"  Throttle Factor:    {wum.ThrottleFactor:F2}");
    Console.WriteLine($"  Headroom:           {wum.HeadroomRemaining:F1}");

    // Record some work units
    Console.WriteLine("\n  Recording 5 work units...");
    wum.Record(5.0, "test");
    await Task.Delay(100);

    var afterWU = wum.CurrentWorkUnits;
    Console.WriteLine($"  After Recording:    {afterWU:F1}");

    var snapshot = wum.GetSnapshot();
    Console.WriteLine($"  Snapshot WU:        {snapshot.CurrentWorkUnits:F1}");
    Console.WriteLine($"  By Type:            {string.Join(", ", snapshot.ByMoleculeType.Select(kv => $"{kv.Key}={kv.Value:F1}"))}");

    if (mode != "free")
    {
        results.Add(new("WorkUnit.Recorded", afterWU >= initialWU + 4.9)); // Allow small variance
        results.Add(new("WorkUnit.SnapshotMatches", Math.Abs(snapshot.CurrentWorkUnits - afterWU) < 0.1));
        results.Add(new("WorkUnit.ByType.Tracked", snapshot.ByMoleculeType.ContainsKey("test")));
    }
    else
    {
        // Free tier uses no-op meter
        results.Add(new("WorkUnit.FreeTier.NoTracking", wum.CurrentWorkUnits == 0));
        results.Add(new("WorkUnit.FreeTier.MaxIsMax", wum.MaxWorkUnits == double.MaxValue));
    }
}

void TestLicensedComponents(DocumentProcessor doc, ImageAnalyzer img, DataValidator data, PremiumFeature prem,
    string mode, ConcurrentBag<TestResult> results)
{
    Console.WriteLine($"  DocumentProcessor:");
    Console.WriteLine($"    ID:          {doc.ComponentId}");
    Console.WriteLine($"    IsLicensed:  {doc.IsLicensed}");
    Console.WriteLine($"    RequiredTier: {doc.Requirements.MinimumTier}");
    Console.WriteLine($"    WorkUnits:   {doc.Requirements.WorkUnits}");

    Console.WriteLine($"\n  ImageAnalyzer:");
    Console.WriteLine($"    ID:          {img.ComponentId}");
    Console.WriteLine($"    IsLicensed:  {img.IsLicensed}");
    Console.WriteLine($"    RequiredTier: {img.Requirements.MinimumTier}");

    Console.WriteLine($"\n  DataValidator:");
    Console.WriteLine($"    ID:          {data.ComponentId}");
    Console.WriteLine($"    IsLicensed:  {data.IsLicensed}");

    Console.WriteLine($"\n  PremiumFeature:");
    Console.WriteLine($"    ID:          {prem.ComponentId}");
    Console.WriteLine($"    IsLicensed:  {prem.IsLicensed}");
    Console.WriteLine($"    RequiredTier: {prem.Requirements.MinimumTier}");

    switch (mode)
    {
        case "licensed":
            results.Add(new("Component.Doc.Licensed", doc.IsLicensed));
            results.Add(new("Component.Img.Licensed", img.IsLicensed));
            results.Add(new("Component.Data.Licensed", data.IsLicensed));
            results.Add(new("Component.Premium.Licensed", prem.IsLicensed));
            break;

        case "expiring":
        case "throttle":
            results.Add(new("Component.Doc.Licensed", doc.IsLicensed));
            results.Add(new("Component.Premium.NotLicensed", !prem.IsLicensed)); // Requires professional
            break;

        case "free":
            results.Add(new("Component.Doc.NotLicensed", !doc.IsLicensed));
            results.Add(new("Component.Premium.NotLicensed", !prem.IsLicensed));
            break;
    }
}

async Task TestComponentOperations(DocumentProcessor doc, ImageAnalyzer img, DataValidator data,
    IWorkUnitMeter wum, string mode, ConcurrentBag<TestResult> results)
{
    var initialWU = wum.CurrentWorkUnits;

    // Test document processing
    Console.WriteLine("  Testing DocumentProcessor.Process(4096 bytes)...");
    var docResult = doc.Process(4096);
    Console.WriteLine($"    Result: {(docResult ? "Success" : "Failed/Throttled")}");

    // Test image analysis
    Console.WriteLine("\n  Testing ImageAnalyzer.Analyze(2048 bytes)...");
    var imgResult = img.Analyze(2048);
    Console.WriteLine($"    Result: {(imgResult ? "Success" : "Failed/Throttled")}");

    // Test data validation
    Console.WriteLine("\n  Testing DataValidator.Validate(1024 bytes)...");
    var dataResult = data.Validate(1024);
    Console.WriteLine($"    Result: {(dataResult ? "Success" : "Failed/Throttled")}");

    await Task.Delay(100);
    var afterWU = wum.CurrentWorkUnits;
    Console.WriteLine($"\n  Work Units Used: {afterWU - initialWU:F1}");

    if (mode != "free")
    {
        results.Add(new("Operation.Doc.Success", docResult));
        results.Add(new("Operation.Img.Success", imgResult));
        results.Add(new("Operation.Data.Success", dataResult));
        results.Add(new("Operation.WorkUnits.Increased", afterWU > initialWU));
    }
}

void TestSignalEmission(ConcurrentBag<string> signals, string mode, ConcurrentBag<TestResult> results)
{
    Console.WriteLine($"  Signals received: {signals.Count}");
    var uniqueSignals = signals.Distinct().OrderBy(s => s).ToList();

    foreach (var sig in uniqueSignals.Take(15))
    {
        Console.WriteLine($"    - {sig}");
    }
    if (uniqueSignals.Count > 15)
    {
        Console.WriteLine($"    ... and {uniqueSignals.Count - 15} more");
    }

    results.Add(new("Signal.SystemReady", signals.Contains("styloflow.system.ready")));
    results.Add(new("Signal.LicenseTier", signals.Any(s => s.StartsWith("styloflow.system.license.tier"))));
    results.Add(new("Signal.MeshMode", signals.Any(s => s.Contains("mesh.mode"))));
    results.Add(new("Signal.Heartbeat", signals.Contains("styloflow.system.heartbeat")));
}

async Task TestThrottling(DocumentProcessor doc, IWorkUnitMeter wum, ConcurrentBag<TestResult> results)
{
    Console.WriteLine("  Consuming work units to trigger throttling...\n");

    var throttleFactors = new List<double>();
    var successCount = 0;
    var throttledCount = 0;

    for (int i = 0; i < 30; i++)
    {
        var factor = wum.ThrottleFactor;
        throttleFactors.Add(factor);

        var result = doc.Process(512);
        if (result) successCount++;
        else throttledCount++;

        var snapshot = wum.GetSnapshot();
        Console.WriteLine($"  [{i + 1:D2}] WU: {snapshot.CurrentWorkUnits,5:F1}/{snapshot.MaxWorkUnits} ({snapshot.PercentUsed,5:F1}%) Factor: {factor:F2} -> {(result ? "OK" : "THROTTLED")}");

        await Task.Delay(50);
    }

    Console.WriteLine($"\n  Success: {successCount}, Throttled: {throttledCount}");
    Console.WriteLine($"  Throttle factors seen: {throttleFactors.Min():F2} - {throttleFactors.Max():F2}");

    results.Add(new("Throttle.FactorDecreased", throttleFactors.Min() < 1.0));
    results.Add(new("Throttle.SomeThrottled", throttledCount > 0 || wum.PercentUsed > 80));
}

// ============================================================================
// Test Components
// ============================================================================

public class DocumentProcessor : LicensedComponentBase
{
    public DocumentProcessor(ILicenseManager lm, IWorkUnitMeter wum, SignalSink sink)
        : base(lm, wum, sink, new LicenseRequirements
        {
            MinimumTier = "starter",
            WorkUnits = 2.0,
            WorkUnitsPerKb = 0.5,
            RequiredFeatures = ["documents.*"],
            RequiresSystemCoordinator = true,
            AllowFreeTierDegradation = true
        })
    {
        AddFreeTierSignal("documents.degraded");
        AddLicensedSignal("documents.ready");
    }

    public override string ComponentId => "styloflow.documents.processor";

    public bool Process(long sizeBytes)
    {
        try
        {
            ValidateLicense();
            if (!CanPerformOperation(sizeBytes))
            {
                return false;
            }
            RecordWorkUnits(sizeBytes);
            EmitSignal("processed", sizeBytes.ToString());
            return true;
        }
        catch (LicenseRequiredException)
        {
            return false;
        }
    }
}

public class ImageAnalyzer : LicensedComponentBase
{
    public ImageAnalyzer(ILicenseManager lm, IWorkUnitMeter wum, SignalSink sink)
        : base(lm, wum, sink, new LicenseRequirements
        {
            MinimumTier = "starter",
            WorkUnits = 3.0,
            WorkUnitsPerKb = 0.1,
            RequiredFeatures = ["images.*"],
            AllowFreeTierDegradation = true
        })
    { }

    public override string ComponentId => "styloflow.images.analyzer";

    public bool Analyze(long sizeBytes)
    {
        try
        {
            ValidateLicense();
            if (!CanPerformOperation(sizeBytes)) return false;
            RecordWorkUnits(sizeBytes);
            EmitSignal("analyzed");
            return true;
        }
        catch { return false; }
    }
}

public class DataValidator : LicensedComponentBase
{
    public DataValidator(ILicenseManager lm, IWorkUnitMeter wum, SignalSink sink)
        : base(lm, wum, sink, new LicenseRequirements
        {
            MinimumTier = "starter",
            WorkUnits = 1.0,
            RequiredFeatures = ["data.*"],
            AllowFreeTierDegradation = true
        })
    { }

    public override string ComponentId => "styloflow.data.validator";

    public bool Validate(long sizeBytes)
    {
        try
        {
            ValidateLicense();
            if (!CanPerformOperation(sizeBytes)) return false;
            RecordWorkUnits(sizeBytes);
            return true;
        }
        catch { return false; }
    }
}

public class PremiumFeature : LicensedComponentBase
{
    public PremiumFeature(ILicenseManager lm, IWorkUnitMeter wum, SignalSink sink)
        : base(lm, wum, sink, new LicenseRequirements
        {
            MinimumTier = "professional",
            WorkUnits = 5.0,
            RequiredFeatures = ["premium.analytics"],
            AllowFreeTierDegradation = false // This one REQUIRES license
        })
    { }

    public override string ComponentId => "styloflow.premium.analytics";

    public bool Execute()
    {
        try
        {
            ValidateLicense(); // Will throw if not professional tier
            RecordWorkUnits();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public record TestResult(string Name, bool Passed, string? Message = null);
