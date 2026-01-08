using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mostlylucid.Ephemeral;
using StyloFlow.Licensing.Models;
using StyloFlow.Licensing.Services;

namespace StyloFlow.Licensing;

/// <summary>
/// Extension methods for registering StyloFlow services.
/// </summary>
public static class StyloFlowServiceExtensions
{
    /// <summary>
    /// Adds StyloFlow services with full licensing support.
    /// The System Coordinator starts automatically and manages licensing, signals, and mesh.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStyloFlow(
        this IServiceCollection services,
        Action<StyloFlowOptions>? configure = null)
    {
        var options = new StyloFlowOptions();
        configure?.Invoke(options);

        // Register options as singleton
        services.AddSingleton(options);

        // Register SignalSink if not already registered (shared with Ephemeral)
        services.TryAddSingleton<SignalSink>();

        // Register core services
        services.TryAddSingleton<ILicenseManager, LicenseManager>();
        services.TryAddSingleton<IWorkUnitMeter, WorkUnitMeter>();

        // Register the System Coordinator as a hosted service (auto-starts)
        services.AddHostedService<SystemCoordinator>();

        return services;
    }

    /// <summary>
    /// Adds StyloFlow services without licensing.
    /// Use this for development, testing, or free-tier deployments.
    /// No System Coordinator is started - components run without license validation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStyloFlowFree(this IServiceCollection services)
    {
        var options = new StyloFlowOptions
        {
            // Free tier defaults are already set, but be explicit
            FreeTierMaxSlots = 5,
            FreeTierMaxWorkUnitsPerMinute = 100,
            FreeTierMaxNodes = 1,
            EnableMesh = false
        };

        services.AddSingleton(options);

        // Register SignalSink (shared with Ephemeral if already registered)
        services.TryAddSingleton<SignalSink>();

        // Register no-op implementations for free tier
        services.TryAddSingleton<ILicenseManager, FreeTierLicenseManager>();
        services.TryAddSingleton<IWorkUnitMeter, NoOpWorkUnitMeter>();

        // No SystemCoordinator - components run freely

        return services;
    }

    /// <summary>
    /// Adds StyloFlow with a license token directly.
    /// Convenience method for embedded license scenarios.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="licenseToken">The license token JSON.</param>
    /// <param name="configure">Optional additional configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStyloFlow(
        this IServiceCollection services,
        string licenseToken,
        Action<StyloFlowOptions>? configure = null)
    {
        return services.AddStyloFlow(options =>
        {
            options.LicenseToken = licenseToken;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Adds StyloFlow with mesh enabled.
    /// Convenience method for clustered deployments.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="meshPeers">Initial mesh peer addresses.</param>
    /// <param name="configure">Optional additional configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStyloFlowMesh(
        this IServiceCollection services,
        IEnumerable<string> meshPeers,
        Action<StyloFlowOptions>? configure = null)
    {
        return services.AddStyloFlow(options =>
        {
            options.EnableMesh = true;
            options.MeshPeers = meshPeers.ToList();
            configure?.Invoke(options);
        });
    }
}

/// <summary>
/// No-op license manager for free tier - always reports free tier limits.
/// </summary>
internal sealed class FreeTierLicenseManager : ILicenseManager
{
    private readonly StyloFlowOptions _options;

    public FreeTierLicenseManager(StyloFlowOptions options)
    {
        _options = options;
    }

    public LicenseState CurrentState => LicenseState.FreeTier;
    public string CurrentTier => "free";
    public int MaxSlots => _options.FreeTierMaxSlots;
    public int MaxWorkUnitsPerMinute => _options.FreeTierMaxWorkUnitsPerMinute;
    public int? MaxNodes => _options.FreeTierMaxNodes;
    public bool IsExpiringSoon => false;
    public TimeSpan TimeUntilExpiry => TimeSpan.Zero;
    public LicenseToken? CurrentLicense => null;
    public IReadOnlyList<string> EnabledFeatures => Array.Empty<string>();

    public event EventHandler<LicenseStateChangedEvent>? LicenseStateChanged;

    public Task<LicenseValidationResult> ValidateLicenseAsync(CancellationToken ct = default)
    {
        return Task.FromResult(LicenseValidationResult.Failure("Free tier - no license required"));
    }

    public bool HasFeature(string feature) => false;
    public bool MeetsTierRequirement(string requiredTier) => requiredTier == "free";
}

/// <summary>
/// No-op work unit meter for free tier - doesn't track anything.
/// </summary>
internal sealed class NoOpWorkUnitMeter : IWorkUnitMeter
{
    public double CurrentWorkUnits => 0;
    public double MaxWorkUnits => double.MaxValue;
    public double PercentUsed => 0;
    public bool IsThrottling => false;
    public double ThrottleFactor => 1.0;
    public double HeadroomRemaining => double.MaxValue;

    public event EventHandler<WorkUnitThresholdEvent>? ThresholdCrossed;

    public void Record(double workUnits, string? moleculeType = null) { }
    public bool CanConsume(double workUnits) => true;

    public WorkUnitSnapshot GetSnapshot() => new()
    {
        CurrentWorkUnits = 0,
        MaxWorkUnits = double.MaxValue,
        PercentUsed = 0,
        IsThrottling = false,
        ThrottleFactor = 1.0,
        WindowStart = DateTimeOffset.UtcNow,
        WindowEnd = DateTimeOffset.UtcNow,
        ByMoleculeType = new Dictionary<string, double>()
    };
}
