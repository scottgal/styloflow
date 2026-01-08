using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mostlylucid.Ephemeral;
using StyloFlow.Licensing.Models;

namespace StyloFlow.Licensing.Services;

/// <summary>
/// The System Coordinator is the trust anchor for StyloFlow.
/// It manages licensing, emits system signals, and is required for licensed components to run.
/// Runs as a BackgroundService that starts automatically with the host.
/// </summary>
public sealed class SystemCoordinator : BackgroundService
{
    private readonly StyloFlowOptions _options;
    private readonly ILicenseManager _licenseManager;
    private readonly IWorkUnitMeter _workUnitMeter;
    private readonly SignalSink _signalSink;
    private readonly ILogger<SystemCoordinator> _logger;

    // Signal constants
    public static class Signals
    {
        // System lifecycle
        public const string Ready = "styloflow.system.ready";
        public const string Shutdown = "styloflow.system.shutdown";
        public const string Heartbeat = "styloflow.system.heartbeat";

        // License signals
        public const string LicenseValid = "styloflow.system.license.valid";
        public const string LicenseTier = "styloflow.system.license.tier";
        public const string LicenseExpiresSoon = "styloflow.system.license.expires_soon";
        public const string LicenseDegraded = "styloflow.system.license.degraded";
        public const string LicenseRevoked = "styloflow.system.license.revoked";
        public const string LicenseSlots = "styloflow.system.license.slots";
        public const string LicenseWorkUnitLimit = "styloflow.system.license.workunit_limit";

        // Capacity signals
        public const string SlotsAvailable = "styloflow.system.slots.available";
        public const string SlotsExhausted = "styloflow.system.slots.exhausted";
        public const string WorkUnitRate = "styloflow.system.workunit.rate";
        public const string WorkUnitThrottling = "styloflow.system.workunit.throttling";

        // Mesh signals (for future)
        public const string MeshModeStandalone = "styloflow.system.mesh.mode.standalone";
        public const string MeshModeCluster = "styloflow.system.mesh.mode.cluster";
        public const string MeshNodeCount = "styloflow.system.mesh.node_count";
        public const string LeaseAuthorityActive = "styloflow.system.la.active";
    }

    public SystemCoordinator(
        StyloFlowOptions options,
        ILicenseManager licenseManager,
        IWorkUnitMeter workUnitMeter,
        SignalSink signalSink,
        ILogger<SystemCoordinator> logger)
    {
        _options = options;
        _licenseManager = licenseManager;
        _workUnitMeter = workUnitMeter;
        _signalSink = signalSink;
        _logger = logger;

        // Subscribe to license state changes
        _licenseManager.LicenseStateChanged += OnLicenseStateChanged;

        // Subscribe to work unit threshold events
        _workUnitMeter.ThresholdCrossed += OnWorkUnitThresholdCrossed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StyloFlow System Coordinator starting...");

        try
        {
            // 1. Validate license
            await ValidateAndEmitLicenseSignalsAsync(stoppingToken);

            // 2. Emit mesh mode (standalone for now)
            EmitMeshSignals();

            // 3. System is ready - this is the key signal licensed components wait for
            _signalSink.Raise(Signals.Ready);
            _logger.LogInformation("StyloFlow System Coordinator ready");

            // 4. Heartbeat loop
            await HeartbeatLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System Coordinator error");
            throw;
        }
        finally
        {
            _logger.LogInformation("StyloFlow System Coordinator shutting down");
            _signalSink.Raise(Signals.Shutdown);
        }
    }

    private async Task ValidateAndEmitLicenseSignalsAsync(CancellationToken ct)
    {
        var result = await _licenseManager.ValidateLicenseAsync(ct);

        if (result.Valid && result.License != null)
        {
            _logger.LogInformation(
                "License valid: Tier={Tier}, Slots={Slots}, WorkUnits={WU}/min",
                result.Tier, _licenseManager.MaxSlots, _licenseManager.MaxWorkUnitsPerMinute);

            EmitLicenseSignals(result.License);
        }
        else
        {
            _logger.LogWarning("No valid license, running in free tier: {Error}", result.ErrorMessage);
            EmitFreeTierSignals();
        }
    }

    private void EmitLicenseSignals(LicenseToken license)
    {
        _signalSink.Raise(Signals.LicenseValid);
        _signalSink.Raise($"{Signals.LicenseTier}.{_licenseManager.CurrentTier}");
        _signalSink.Raise(Signals.LicenseSlots, _licenseManager.MaxSlots.ToString());
        _signalSink.Raise(Signals.LicenseWorkUnitLimit, _licenseManager.MaxWorkUnitsPerMinute.ToString());
        _signalSink.Raise(Signals.SlotsAvailable, _licenseManager.MaxSlots.ToString());

        if (_licenseManager.IsExpiringSoon)
        {
            _signalSink.Raise(Signals.LicenseExpiresSoon);
        }
    }

    private void EmitFreeTierSignals()
    {
        _signalSink.Raise($"{Signals.LicenseTier}.free");
        _signalSink.Raise(Signals.LicenseDegraded);
        _signalSink.Raise(Signals.LicenseSlots, _options.FreeTierMaxSlots.ToString());
        _signalSink.Raise(Signals.LicenseWorkUnitLimit, _options.FreeTierMaxWorkUnitsPerMinute.ToString());
        _signalSink.Raise(Signals.SlotsAvailable, _options.FreeTierMaxSlots.ToString());
    }

    private void EmitMeshSignals()
    {
        if (_options.EnableMesh)
        {
            _signalSink.Raise(Signals.MeshModeCluster);
            // TODO: Actual mesh node count
            _signalSink.Raise(Signals.MeshNodeCount, "1");
        }
        else
        {
            _signalSink.Raise(Signals.MeshModeStandalone);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.HeartbeatInterval, ct);

            // Emit heartbeat
            _signalSink.Raise(Signals.Heartbeat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

            // Refresh license expiry warning
            if (_licenseManager.IsExpiringSoon)
            {
                _signalSink.Raise(Signals.LicenseExpiresSoon);
            }

            // Emit work unit rate
            var snapshot = _workUnitMeter.GetSnapshot();
            _signalSink.Raise(Signals.WorkUnitRate, snapshot.CurrentWorkUnits.ToString("F1"));

            if (snapshot.IsThrottling)
            {
                _signalSink.Raise(Signals.WorkUnitThrottling, snapshot.ThrottleFactor.ToString("F2"));
            }

            _logger.LogDebug(
                "Heartbeat: WorkUnits={WU:F1}/{Max}, Throttle={Throttle:F2}",
                snapshot.CurrentWorkUnits, snapshot.MaxWorkUnits, snapshot.ThrottleFactor);
        }
    }

    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEvent e)
    {
        _logger.LogInformation(
            "License state changed: {Previous} -> {New} ({Reason})",
            e.PreviousState, e.NewState, e.Reason);

        switch (e.NewState)
        {
            case LicenseState.Valid:
                _signalSink.Raise(Signals.LicenseValid);
                break;

            case LicenseState.ExpiringSoon:
                _signalSink.Raise(Signals.LicenseExpiresSoon);
                break;

            case LicenseState.Expired:
            case LicenseState.Invalid:
                _signalSink.Raise(Signals.LicenseRevoked);
                EmitFreeTierSignals();
                break;

            case LicenseState.FreeTier:
                EmitFreeTierSignals();
                break;
        }
    }

    private void OnWorkUnitThresholdCrossed(object? sender, WorkUnitThresholdEvent e)
    {
        if (e.ThresholdPercent >= 100)
        {
            _signalSink.Raise(Signals.SlotsExhausted);
            _signalSink.Raise(Signals.WorkUnitThrottling, "0.00");
        }
        else
        {
            _signalSink.Raise(Signals.WorkUnitThrottling, _workUnitMeter.ThrottleFactor.ToString("F2"));
        }
    }

    public override void Dispose()
    {
        _licenseManager.LicenseStateChanged -= OnLicenseStateChanged;
        _workUnitMeter.ThresholdCrossed -= OnWorkUnitThresholdCrossed;
        base.Dispose();
    }
}
