using System.Text.Json;
using StyloFlow.Licensing.Models;
using Xunit;

namespace StyloFlow.Tests.Licensing;

public sealed class LicenseModelsTests
{
    [Fact]
    public void LicenseToken_DefaultsDomainsToEmpty_AndOrgIdToNull()
    {
        var token = new LicenseToken
        {
            LicenseId = "lic-1",
            IssuedTo = "acme",
            IssuedAt = DateTimeOffset.UtcNow,
            Expiry = DateTimeOffset.UtcNow.AddDays(30),
            Limits = new LicenseLimits { MaxMoleculeSlots = 1, MaxWorkUnitsPerMinute = 1 },
            Tier = "starter"
        };

        Assert.Empty(token.Domains);
        Assert.Null(token.OrgId);
    }

    [Fact]
    public void LicenseToken_RoundTripsDomainsAndOrgIdThroughJson()
    {
        var original = new LicenseToken
        {
            LicenseId = "lic-2",
            IssuedTo = "acme",
            IssuedAt = DateTimeOffset.UtcNow,
            Expiry = DateTimeOffset.UtcNow.AddDays(30),
            Limits = new LicenseLimits { MaxMoleculeSlots = 1, MaxWorkUnitsPerMinute = 1 },
            Tier = "starter",
            Domains = new[] { "acme.com", "acme.co.uk" },
            OrgId = "org-42"
        };

        var json = JsonSerializer.Serialize(original);
        var round = JsonSerializer.Deserialize<LicenseToken>(json);

        Assert.NotNull(round);
        Assert.Equal(new[] { "acme.com", "acme.co.uk" }, round!.Domains);
        Assert.Equal("org-42", round.OrgId);
    }

    [Fact]
    public void LicenseToken_DeserializesLegacyJson_DefaultsDomainsToEmptyAndOrgIdToNull()
    {
        const string legacyJson = """
            {"LicenseId":"lic-0","IssuedTo":"acme","IssuedAt":"2025-01-01T00:00:00+00:00",
             "Expiry":"2026-01-01T00:00:00+00:00","Limits":{"MaxMoleculeSlots":1,"MaxWorkUnitsPerMinute":1},
             "Tier":"starter"}
            """;

        var token = JsonSerializer.Deserialize<LicenseToken>(legacyJson);

        Assert.NotNull(token);
        Assert.Empty(token!.Domains);
        Assert.Null(token.OrgId);
    }
}
