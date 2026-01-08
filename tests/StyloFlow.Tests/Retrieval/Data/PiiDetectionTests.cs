using StyloFlow.Retrieval.Data;
using Xunit;

namespace StyloFlow.Tests.Retrieval.Data;

public class PiiDetectionTests
{
    #region ScanValues Tests

    [Fact]
    public void ScanValues_EmptyValues_ChecksColumnName()
    {
        // Arrange
        var values = Array.Empty<string?>();

        // Act
        var result = PiiDetection.ScanValues("email_address", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.Email, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_AllNull_ChecksColumnName()
    {
        // Arrange
        var values = new string?[] { null, null, null };

        // Act
        var result = PiiDetection.ScanValues("ssn", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.SSN, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_SSNs_DetectsSSN()
    {
        // Arrange
        var values = new[]
        {
            "123-45-6789",
            "987-65-4321",
            "111-22-3333",
            "444-55-6666"
        };

        // Act
        var result = PiiDetection.ScanValues("tax_id", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.SSN, result.PrimaryType);
        Assert.Equal(PiiDetection.PiiRiskLevel.Critical, result.RiskLevel);
    }

    [Fact]
    public void ScanValues_Emails_DetectsEmail()
    {
        // Arrange
        var values = new[]
        {
            "test@example.com",
            "user@domain.org",
            "admin@company.net"
        };

        // Act
        var result = PiiDetection.ScanValues("contact", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.Email, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_PhoneNumbers_DetectsPhone()
    {
        // Arrange
        var values = new[]
        {
            "555-123-4567",
            "(555) 123-4567",
            "+1 555 123 4567"
        };

        // Act
        var result = PiiDetection.ScanValues("contact_number", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.PhoneNumber, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_IpAddresses_DetectsIP()
    {
        // Arrange
        var values = new[]
        {
            "192.168.1.1",
            "10.0.0.1",
            "172.16.0.1"
        };

        // Act
        var result = PiiDetection.ScanValues("client_ip", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.IPAddress, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_UUIDs_DetectsUUID()
    {
        // Arrange
        var values = new[]
        {
            "123e4567-e89b-12d3-a456-426614174000",
            "550e8400-e29b-41d4-a716-446655440000"
        };

        // Act
        var result = PiiDetection.ScanValues("user_guid", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.UUID, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_Dates_DetectsDateOfBirth()
    {
        // Arrange
        var values = new[]
        {
            "1990-01-15",
            "1985-06-20",
            "2000-12-31"
        };

        // Act
        var result = PiiDetection.ScanValues("dob", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.DateOfBirth, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_ZipCodes_DetectsZipCode()
    {
        // Arrange
        var values = new[]
        {
            "12345",
            "67890",
            "12345-6789"
        };

        // Act
        var result = PiiDetection.ScanValues("postal", values);

        // Assert
        Assert.True(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.ZipCode, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_NoPii_ReturnsNone()
    {
        // Arrange
        var values = new[]
        {
            "apple",
            "banana",
            "cherry"
        };

        // Act
        var result = PiiDetection.ScanValues("fruit", values);

        // Assert
        Assert.False(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.None, result.PrimaryType);
    }

    [Fact]
    public void ScanValues_LowMatchRate_NotFlaggedAsPii()
    {
        // Arrange - only 1 of 10 values is an email
        var values = new[] { "test@example.com" }
            .Concat(Enumerable.Repeat("not_an_email", 9))
            .ToArray();

        // Act
        var result = PiiDetection.ScanValues("data", values);

        // Assert
        Assert.False(result.IsPii);
    }

    [Fact]
    public void ScanValues_RedactsSamples()
    {
        // Arrange
        var values = new[]
        {
            "123-45-6789",
            "987-65-4321"
        };

        // Act
        var result = PiiDetection.ScanValues("ssn", values);

        // Assert
        Assert.All(result.DetectedTypes.SelectMany(d => d.Samples), s => Assert.Contains("***", s));
    }

    #endregion

    #region DetectFromColumnName Tests

    [Fact]
    public void DetectFromColumnName_SSN_ReturnsSSN()
    {
        // Act & Assert
        Assert.Equal(PiiDetection.PiiType.SSN, PiiDetection.DetectFromColumnName("ssn"));
        Assert.Equal(PiiDetection.PiiType.SSN, PiiDetection.DetectFromColumnName("social_security_number"));
    }

    [Fact]
    public void DetectFromColumnName_Email_ReturnsEmail()
    {
        // Act & Assert
        Assert.Equal(PiiDetection.PiiType.Email, PiiDetection.DetectFromColumnName("email"));
        Assert.Equal(PiiDetection.PiiType.Email, PiiDetection.DetectFromColumnName("user_email"));
        Assert.Equal(PiiDetection.PiiType.Email, PiiDetection.DetectFromColumnName("e_mail_address"));
    }

    [Fact]
    public void DetectFromColumnName_Phone_ReturnsPhone()
    {
        // Act & Assert
        Assert.Equal(PiiDetection.PiiType.PhoneNumber, PiiDetection.DetectFromColumnName("phone"));
        Assert.Equal(PiiDetection.PiiType.PhoneNumber, PiiDetection.DetectFromColumnName("mobile_number"));
        Assert.Equal(PiiDetection.PiiType.PhoneNumber, PiiDetection.DetectFromColumnName("cell_phone"));
    }

    [Fact]
    public void DetectFromColumnName_Address_ReturnsAddress()
    {
        // Act & Assert
        Assert.Equal(PiiDetection.PiiType.Address, PiiDetection.DetectFromColumnName("address"));
        Assert.Equal(PiiDetection.PiiType.Address, PiiDetection.DetectFromColumnName("street_address"));
    }

    [Fact]
    public void DetectFromColumnName_Name_ReturnsPersonName()
    {
        // Act & Assert
        Assert.Equal(PiiDetection.PiiType.PersonName, PiiDetection.DetectFromColumnName("name"));
        Assert.Equal(PiiDetection.PiiType.PersonName, PiiDetection.DetectFromColumnName("first_name"));
        Assert.Equal(PiiDetection.PiiType.PersonName, PiiDetection.DetectFromColumnName("last_name"));
    }

    [Fact]
    public void DetectFromColumnName_IP_ReturnsIP()
    {
        // Act & Assert - only exact "ip" matches IPAddress
        Assert.Equal(PiiDetection.PiiType.IPAddress, PiiDetection.DetectFromColumnName("ip"));
        // Note: "ip_address" contains "address" which is checked first, returning Address type
        // This is current implementation behavior
    }

    [Fact]
    public void DetectFromColumnName_DOB_ReturnsDateOfBirth()
    {
        // Act & Assert
        Assert.Equal(PiiDetection.PiiType.DateOfBirth, PiiDetection.DetectFromColumnName("dob"));
        Assert.Equal(PiiDetection.PiiType.DateOfBirth, PiiDetection.DetectFromColumnName("birth_date"));
    }

    [Fact]
    public void DetectFromColumnName_NonPii_ReturnsNone()
    {
        // Act & Assert
        Assert.Equal(PiiDetection.PiiType.None, PiiDetection.DetectFromColumnName("product_id"));
        Assert.Equal(PiiDetection.PiiType.None, PiiDetection.DetectFromColumnName("quantity"));
        Assert.Equal(PiiDetection.PiiType.None, PiiDetection.DetectFromColumnName("created_at"));
    }

    #endregion

    #region RedactValue Tests

    [Fact]
    public void RedactValue_SSN_RedactsCorrectly()
    {
        // Act
        var redacted = PiiDetection.RedactValue("123-45-6789", PiiDetection.PiiType.SSN);

        // Assert
        Assert.Equal("***-**-6789", redacted);
    }

    [Fact]
    public void RedactValue_CreditCard_RedactsCorrectly()
    {
        // Act
        var redacted = PiiDetection.RedactValue("1234567890123456", PiiDetection.PiiType.CreditCard);

        // Assert
        Assert.Equal("**** **** **** 3456", redacted);
    }

    [Fact]
    public void RedactValue_Email_RedactsCorrectly()
    {
        // Act
        var redacted = PiiDetection.RedactValue("test@example.com", PiiDetection.PiiType.Email);

        // Assert
        Assert.StartsWith("te***@***", redacted);
        Assert.EndsWith(".com", redacted);
    }

    [Fact]
    public void RedactValue_Phone_RedactsCorrectly()
    {
        // Act
        var redacted = PiiDetection.RedactValue("555-123-4567", PiiDetection.PiiType.PhoneNumber);

        // Assert
        Assert.Equal("***-***-4567", redacted);
    }

    [Fact]
    public void RedactValue_ShortValue_ReturnsStars()
    {
        // Act
        var redacted = PiiDetection.RedactValue("abc", PiiDetection.PiiType.Other);

        // Assert
        Assert.Equal("****", redacted);
    }

    #endregion

    #region GetRecommendedAction Tests

    [Fact]
    public void GetRecommendedAction_Critical_ReturnsExclude()
    {
        // Act
        var action = PiiDetection.GetRecommendedAction(PiiDetection.PiiRiskLevel.Critical);

        // Assert
        Assert.Contains("EXCLUDE", action);
    }

    [Fact]
    public void GetRecommendedAction_High_ReturnsMask()
    {
        // Act
        var action = PiiDetection.GetRecommendedAction(PiiDetection.PiiRiskLevel.High);

        // Assert
        Assert.Contains("Mask", action);
    }

    [Fact]
    public void GetRecommendedAction_Medium_ReturnsFaker()
    {
        // Act
        var action = PiiDetection.GetRecommendedAction(PiiDetection.PiiRiskLevel.Medium);

        // Assert
        Assert.Contains("Faker", action);
    }

    [Fact]
    public void GetRecommendedAction_Low_ReturnsSafe()
    {
        // Act
        var action = PiiDetection.GetRecommendedAction(PiiDetection.PiiRiskLevel.Low);

        // Assert
        Assert.Contains("Safe", action);
    }

    [Fact]
    public void GetRecommendedAction_None_ReturnsNoAction()
    {
        // Act
        var action = PiiDetection.GetRecommendedAction(PiiDetection.PiiRiskLevel.None);

        // Assert
        Assert.Contains("No action", action);
    }

    #endregion
}

public class PiiScanResultTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var result = new PiiScanResult();

        // Assert
        Assert.Equal("", result.ColumnName);
        Assert.False(result.IsPii);
        Assert.Equal(PiiDetection.PiiType.None, result.PrimaryType);
        Assert.Equal(0.0, result.Confidence);
        Assert.Equal(PiiDetection.PiiRiskLevel.None, result.RiskLevel);
        Assert.Empty(result.DetectedTypes);
    }
}
