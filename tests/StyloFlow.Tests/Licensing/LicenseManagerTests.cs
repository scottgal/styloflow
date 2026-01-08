
using Microsoft.Extensions.Logging.Abstractions;
using StyloFlow.Licensing;
using StyloFlow.Licensing.Models;
using StyloFlow.Licensing.Services;
using Xunit;

namespace StyloFlow.Tests.Licensing;

public class LicenseManagerTests
{
    #region Tier Requirement Tests

    [Theory]
    [InlineData("free", "free", true)]
    [InlineData("starter", "free", true)]
    [InlineData("professional", "free", true)]
    [InlineData("enterprise", "free", true)]
    [InlineData("free", "starter", false)]
    [InlineData("starter", "starter", true)]
    [InlineData("professional", "starter", true)]
    [InlineData("enterprise", "starter", true)]
    [InlineData("free", "professional", false)]
    [InlineData("starter", "professional", false)]
    [InlineData("professional", "professional", true)]
    [InlineData("enterprise", "professional", true)]
    [InlineData("free", "enterprise", false)]
    [InlineData("starter", "enterprise", false)]
    [InlineData("professional", "enterprise", false)]
    [InlineData("enterprise", "enterprise", true)]
    public async Task MeetsTierRequirement_ReturnsCorrectResult(string currentTier, string requiredTier, bool expected)
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson(currentTier)
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act
        var result = manager.MeetsTierRequirement(requiredTier);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("free", true)]
    public async Task MeetsTierRequirement_EmptyOrFreeTier_AlwaysTrue(string? requiredTier, bool expected)
    {
        // Arrange
        var options = new StyloFlowOptions();
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = manager.MeetsTierRequirement(requiredTier!);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task MeetsTierRequirement_UnknownTier_TreatsAsLowest()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("unknown-tier")
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act & Assert
        Assert.True(manager.MeetsTierRequirement("free"));
        Assert.False(manager.MeetsTierRequirement("starter"));
    }

    #endregion

    #region Feature Matching Tests

    [Theory]
    [InlineData("documents.parse", new[] { "documents.parse" }, true)]
    [InlineData("documents.parse", new[] { "documents.*" }, true)]
    [InlineData("documents.parse.pdf", new[] { "documents.*" }, true)]
    [InlineData("images.analyze", new[] { "documents.*" }, false)]
    [InlineData("anything", new[] { "*" }, true)]
    [InlineData("", new[] { "documents.*" }, true)]
    [InlineData("premium.feature", new[] { "basic.feature", "premium.feature" }, true)]
    [InlineData("premium.other", new[] { "basic.feature", "premium.feature" }, false)]
    public async Task HasFeature_MatchesCorrectly(string feature, string[] enabledFeatures, bool expected)
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional", enabledFeatures)
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act
        var result = manager.HasFeature(feature);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task HasFeature_EmptyFeature_ReturnsTrue()
    {
        // Arrange
        var options = new StyloFlowOptions();
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = manager.HasFeature("");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasFeature_NoLicense_ReturnsFalse()
    {
        // Arrange
        var options = new StyloFlowOptions();
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act
        var result = manager.HasFeature("any.feature");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region License Validation Tests

    [Fact]
    public async Task ValidateLicense_NoLicense_ReturnsFreeTier()
    {
        // Arrange
        var options = new StyloFlowOptions();
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = await manager.ValidateLicenseAsync();

        // Assert
        Assert.False(result.Valid);
        Assert.Equal(LicenseState.FreeTier, manager.CurrentState);
        Assert.Equal("free", manager.CurrentTier);
    }

    [Fact]
    public async Task ValidateLicense_ValidLicense_ReturnsValid()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional")
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = await manager.ValidateLicenseAsync();

        // Assert
        Assert.True(result.Valid);
        Assert.Equal(LicenseState.Valid, manager.CurrentState);
        Assert.Equal("professional", manager.CurrentTier);
    }

    [Fact]
    public async Task ValidateLicense_ExpiredLicense_ReturnsExpired()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional", expiry: DateTimeOffset.UtcNow.AddDays(-1))
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = await manager.ValidateLicenseAsync();

        // Assert
        Assert.False(result.Valid);
        Assert.Equal(LicenseState.Expired, manager.CurrentState);
    }

    [Fact]
    public async Task ValidateLicense_ExpiringSoon_ReturnsExpiringSoon()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional", expiry: DateTimeOffset.UtcNow.AddMinutes(2)),
            LicenseGracePeriod = TimeSpan.FromMinutes(5)
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = await manager.ValidateLicenseAsync();

        // Assert
        Assert.True(result.Valid);
        Assert.Equal(LicenseState.ExpiringSoon, manager.CurrentState);
        Assert.True(manager.IsExpiringSoon);
    }

    [Fact]
    public async Task ValidateLicense_InvalidJson_ReturnsInvalid()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = "not valid json"
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = await manager.ValidateLicenseAsync();

        // Assert
        Assert.False(result.Valid);
        Assert.Equal(LicenseState.Invalid, manager.CurrentState);
    }

    [Fact]
    public async Task ValidateLicense_CustomValidator_IsUsed()
    {
        // Arrange
        var customCalled = false;
        var options = new StyloFlowOptions
        {
            LicenseToken = "custom-token",
            CustomLicenseValidator = (token, ct) =>
            {
                customCalled = true;
                Assert.Equal("custom-token", token);
                return Task.FromResult(LicenseValidationResult.Failure("Custom validation failed"));
            }
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        var result = await manager.ValidateLicenseAsync();

        // Assert
        Assert.True(customCalled);
        Assert.False(result.Valid);
        Assert.Equal("Custom validation failed", result.ErrorMessage);
    }

    #endregion

    #region State Change Event Tests

    [Fact]
    public async Task LicenseStateChanged_EventFired_OnStateChange()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional")
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        LicenseStateChangedEvent? receivedEvent = null;
        manager.LicenseStateChanged += (sender, evt) => receivedEvent = evt;

        // Act
        await manager.ValidateLicenseAsync();

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal(LicenseState.Unknown, receivedEvent.PreviousState);
        Assert.Equal(LicenseState.Valid, receivedEvent.NewState);
    }

    [Fact]
    public async Task LicenseStateChanged_CallbackInvoked()
    {
        // Arrange
        LicenseStateChangedEvent? receivedEvent = null;
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional"),
            OnLicenseStateChanged = evt => receivedEvent = evt
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act
        await manager.ValidateLicenseAsync();

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal(LicenseState.Valid, receivedEvent.NewState);
    }

    #endregion

    #region License Limits Tests

    [Fact]
    public async Task MaxSlots_ReturnsLicenseValue()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional", maxSlots: 100)
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act & Assert
        Assert.Equal(100, manager.MaxSlots);
    }

    [Fact]
    public async Task MaxSlots_Override_TakesPrecedence()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional", maxSlots: 100),
            LicenseOverrides = new LicenseOverrides { MaxSlots = 50 }
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act & Assert
        Assert.Equal(50, manager.MaxSlots);
    }

    [Fact]
    public async Task MaxWorkUnitsPerMinute_ReturnsLicenseValue()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional", maxWorkUnits: 5000)
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act & Assert
        Assert.Equal(5000, manager.MaxWorkUnitsPerMinute);
    }

    [Fact]
    public async Task MaxSlots_NoLicense_ReturnsFreeTierDefault()
    {
        // Arrange
        var options = new StyloFlowOptions
        {
            FreeTierMaxSlots = 25
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act & Assert
        Assert.Equal(25, manager.MaxSlots);
    }

    [Fact]
    public async Task TimeUntilExpiry_ReturnsCorrectValue()
    {
        // Arrange
        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        var options = new StyloFlowOptions
        {
            LicenseToken = CreateLicenseJson("professional", expiry: expiry)
        };
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);
        await manager.ValidateLicenseAsync();

        // Act
        var timeRemaining = manager.TimeUntilExpiry;

        // Assert
        Assert.True(timeRemaining.TotalDays > 29 && timeRemaining.TotalDays <= 30);
    }

    [Fact]
    public async Task TimeUntilExpiry_NoLicense_ReturnsZero()
    {
        // Arrange
        var options = new StyloFlowOptions();
        var manager = new LicenseManager(options, NullLogger<LicenseManager>.Instance);

        // Act & Assert
        Assert.Equal(TimeSpan.Zero, manager.TimeUntilExpiry);
    }

    #endregion

    #region Helper Methods

    private static string CreateLicenseJson(
        string tier,
        string[]? features = null,
        DateTimeOffset? expiry = null,
        int maxSlots = 100,
        int maxWorkUnits = 1000)
    {
        var featuresJson = features != null
            ? $"[{string.Join(",", features.Select(f => $"\"{f}\""))}]"
            : "[]";

        var expiryValue = expiry ?? DateTimeOffset.UtcNow.AddDays(30);

        return $$"""
        {
            "licenseId": "test-{{Guid.NewGuid():N}}",
            "issuedTo": "test@example.com",
            "issuedAt": "{{DateTimeOffset.UtcNow:O}}",
            "expiry": "{{expiryValue:O}}",
            "tier": "{{tier}}",
            "features": {{featuresJson}},
            "limits": {
                "maxMoleculeSlots": {{maxSlots}},
                "maxWorkUnitsPerMinute": {{maxWorkUnits}},
                "maxNodes": 10
            }
        }
        """;
    }

    #endregion
}
