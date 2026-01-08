using StyloFlow.Licensing.Cryptography;
using Xunit;

namespace StyloFlow.Tests.Licensing;

public class Ed25519SignerTests
{
    #region Key Generation Tests

    [Fact]
    public void GenerateKeyPair_ReturnsValidKeys()
    {
        // Act
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();

        // Assert
        Assert.NotEmpty(privateKey);
        Assert.NotEmpty(publicKey);

        // Ed25519 private key is 32 bytes = 44 base64 chars (with padding)
        // Ed25519 public key is 32 bytes = 44 base64 chars
        var privateBytes = Convert.FromBase64String(privateKey);
        var publicBytes = Convert.FromBase64String(publicKey);

        Assert.Equal(32, privateBytes.Length);
        Assert.Equal(32, publicBytes.Length);
    }

    [Fact]
    public void GenerateKeyPair_ProducesDifferentKeysEachTime()
    {
        // Act
        var (privateKey1, _) = Ed25519Signer.GenerateKeyPair();
        var (privateKey2, _) = Ed25519Signer.GenerateKeyPair();

        // Assert
        Assert.NotEqual(privateKey1, privateKey2);
    }

    [Fact]
    public void GetPublicKey_ExtractsCorrectPublicKey()
    {
        // Arrange
        var (privateKey, expectedPublicKey) = Ed25519Signer.GenerateKeyPair();

        // Act
        var extractedPublicKey = Ed25519Signer.GetPublicKey(privateKey);

        // Assert
        Assert.Equal(expectedPublicKey, extractedPublicKey);
    }

    #endregion

    #region Signing Tests

    [Fact]
    public void Sign_ProducesValidSignature()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();
        var message = "Hello, StyloFlow!";

        // Act
        var signature = Ed25519Signer.Sign(message, privateKey);

        // Assert
        Assert.NotEmpty(signature);
        var signatureBytes = Convert.FromBase64String(signature);
        Assert.Equal(64, signatureBytes.Length); // Ed25519 signatures are 64 bytes
    }

    [Fact]
    public void Sign_SameMessage_ProducesSameSignature()
    {
        // Ed25519 is deterministic - same input always gives same output
        var (privateKey, _) = Ed25519Signer.GenerateKeyPair();
        var message = "Deterministic test";

        // Act
        var signature1 = Ed25519Signer.Sign(message, privateKey);
        var signature2 = Ed25519Signer.Sign(message, privateKey);

        // Assert
        Assert.Equal(signature1, signature2);
    }

    [Fact]
    public void Sign_DifferentMessages_ProduceDifferentSignatures()
    {
        // Arrange
        var (privateKey, _) = Ed25519Signer.GenerateKeyPair();

        // Act
        var signature1 = Ed25519Signer.Sign("Message 1", privateKey);
        var signature2 = Ed25519Signer.Sign("Message 2", privateKey);

        // Assert
        Assert.NotEqual(signature1, signature2);
    }

    [Fact]
    public void Sign_ByteArray_Works()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var signature = Ed25519Signer.Sign(data, privateKey);

        // Assert
        Assert.True(Ed25519Signer.Verify(data, signature, publicKey));
    }

    #endregion

    #region Verification Tests

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();
        var message = "Test message";
        var signature = Ed25519Signer.Sign(message, privateKey);

        // Act
        var isValid = Ed25519Signer.Verify(message, signature, publicKey);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void Verify_TamperedMessage_ReturnsFalse()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();
        var message = "Original message";
        var signature = Ed25519Signer.Sign(message, privateKey);

        // Act
        var isValid = Ed25519Signer.Verify("Tampered message", signature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Verify_WrongPublicKey_ReturnsFalse()
    {
        // Arrange
        var (privateKey, _) = Ed25519Signer.GenerateKeyPair();
        var (_, wrongPublicKey) = Ed25519Signer.GenerateKeyPair();
        var message = "Test message";
        var signature = Ed25519Signer.Sign(message, privateKey);

        // Act
        var isValid = Ed25519Signer.Verify(message, signature, wrongPublicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Verify_InvalidSignature_ReturnsFalse()
    {
        // Arrange
        var (_, publicKey) = Ed25519Signer.GenerateKeyPair();
        var message = "Test message";
        var invalidSignature = Convert.ToBase64String(new byte[64]); // All zeros

        // Act
        var isValid = Ed25519Signer.Verify(message, invalidSignature, publicKey);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void Verify_MalformedSignature_ReturnsFalse()
    {
        // Arrange
        var (_, publicKey) = Ed25519Signer.GenerateKeyPair();
        var message = "Test message";

        // Act
        var isValid = Ed25519Signer.Verify(message, "not-valid-base64!!!", publicKey);

        // Assert
        Assert.False(isValid);
    }

    #endregion
}

public class LicenseSigningServiceTests
{
    #region License Signing Tests

    [Fact]
    public void SignLicense_AddsSignatureField()
    {
        // Arrange
        var (privateKey, _) = Ed25519Signer.GenerateKeyPair();
        var service = new LicenseSigningService(privateKey);
        var licenseJson = """{"licenseId":"test-123","tier":"professional"}""";

        // Act
        var signedJson = service.SignLicense(licenseJson);

        // Assert
        Assert.Contains("\"signature\":", signedJson);
    }

    [Fact]
    public void SignLicense_PreservesOriginalFields()
    {
        // Arrange
        var (privateKey, _) = Ed25519Signer.GenerateKeyPair();
        var service = new LicenseSigningService(privateKey);
        var licenseJson = """{"licenseId":"test-123","tier":"professional","features":["documents.*"]}""";

        // Act
        var signedJson = service.SignLicense(licenseJson);

        // Assert
        Assert.Contains("\"licenseId\":\"test-123\"", signedJson);
        Assert.Contains("\"tier\":\"professional\"", signedJson);
        Assert.Contains("\"features\":", signedJson);
    }

    [Fact]
    public void VerifyLicense_ValidSignature_ReturnsTrue()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();
        var signer = new LicenseSigningService(privateKey);
        var verifier = new LicenseSigningService(publicKey, isPublicKey: true);

        var licenseJson = """
        {
            "licenseId": "lic-abc123",
            "issuedTo": "test@example.com",
            "tier": "professional"
        }
        """;

        // Act
        var signedJson = signer.SignLicense(licenseJson);
        var isValid = verifier.VerifyLicense(signedJson);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyLicense_TamperedLicense_ReturnsFalse()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();
        var signer = new LicenseSigningService(privateKey);
        var verifier = new LicenseSigningService(publicKey, isPublicKey: true);

        var licenseJson = """{"licenseId":"original","tier":"starter"}""";
        var signedJson = signer.SignLicense(licenseJson);

        // Tamper with the license
        var tamperedJson = signedJson.Replace("starter", "enterprise");

        // Act
        var isValid = verifier.VerifyLicense(tamperedJson);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void VerifyLicense_MissingSignature_ReturnsFalse()
    {
        // Arrange
        var (_, publicKey) = Ed25519Signer.GenerateKeyPair();
        var verifier = new LicenseSigningService(publicKey, isPublicKey: true);
        var licenseJson = """{"licenseId":"test","tier":"professional"}""";

        // Act
        var isValid = verifier.VerifyLicense(licenseJson);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void SignLicense_ThrowsWithoutPrivateKey()
    {
        // Arrange
        var (_, publicKey) = Ed25519Signer.GenerateKeyPair();
        var verifier = new LicenseSigningService(publicKey, isPublicKey: true);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            verifier.SignLicense("""{"test":"value"}"""));
    }

    [Fact]
    public void CanSign_TrueForSigner_FalseForVerifier()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();

        // Act
        var signer = new LicenseSigningService(privateKey);
        var verifier = new LicenseSigningService(publicKey, isPublicKey: true);

        // Assert
        Assert.True(signer.CanSign);
        Assert.False(verifier.CanSign);
    }

    [Fact]
    public void PublicKey_SameForSignerAndVerifier()
    {
        // Arrange
        var (privateKey, publicKey) = Ed25519Signer.GenerateKeyPair();

        // Act
        var signer = new LicenseSigningService(privateKey);
        var verifier = new LicenseSigningService(publicKey, isPublicKey: true);

        // Assert
        Assert.Equal(publicKey, signer.PublicKey);
        Assert.Equal(publicKey, verifier.PublicKey);
    }

    #endregion
}

public class ApiAuthenticatorTests
{
    #region Request Signing Tests

    [Fact]
    public void SignRequest_ReturnsRequiredHeaders()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());

        // Act
        var headers = authenticator.SignRequest("GET", "/api/v1/status");

        // Assert
        Assert.True(headers.ContainsKey(ApiAuthenticator.AuthHeader));
        Assert.True(headers.ContainsKey(ApiAuthenticator.TimestampHeader));
    }

    [Fact]
    public void SignRequest_AuthHeaderContainsLicenseId()
    {
        // Arrange
        var licenseId = "lic-test-456";
        var authenticator = new ApiAuthenticator(licenseId, CreateTestSignature());

        // Act
        var headers = authenticator.SignRequest("POST", "/api/v1/report");

        // Assert
        Assert.StartsWith($"{licenseId}:", headers[ApiAuthenticator.AuthHeader]);
    }

    [Fact]
    public void SignRequest_TimestampIsRecent()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var headers = authenticator.SignRequest("GET", "/api/v1/status");
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Assert
        var timestamp = long.Parse(headers[ApiAuthenticator.TimestampHeader]);
        Assert.InRange(timestamp, before, after);
    }

    #endregion

    #region Request Verification Tests

    [Fact]
    public void VerifyRequest_ValidSignature_ReturnsSuccess()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var headers = authenticator.SignRequest("GET", "/api/v1/status");

        var authParts = headers[ApiAuthenticator.AuthHeader].Split(':', 2);
        var timestamp = long.Parse(headers[ApiAuthenticator.TimestampHeader]);

        // Act
        var result = authenticator.VerifyRequest("GET", "/api/v1/status", timestamp, authParts[1]);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("lic-123", result.LicenseId);
    }

    [Fact]
    public void VerifyRequest_TamperedPath_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var headers = authenticator.SignRequest("GET", "/api/v1/status");

        var authParts = headers[ApiAuthenticator.AuthHeader].Split(':', 2);
        var timestamp = long.Parse(headers[ApiAuthenticator.TimestampHeader]);

        // Act - verify with different path
        var result = authenticator.VerifyRequest("GET", "/api/v1/other", timestamp, authParts[1]);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Invalid signature", result.ErrorMessage);
    }

    [Fact]
    public void VerifyRequest_TamperedMethod_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var headers = authenticator.SignRequest("GET", "/api/v1/status");

        var authParts = headers[ApiAuthenticator.AuthHeader].Split(':', 2);
        var timestamp = long.Parse(headers[ApiAuthenticator.TimestampHeader]);

        // Act - verify with different method
        var result = authenticator.VerifyRequest("POST", "/api/v1/status", timestamp, authParts[1]);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void VerifyRequest_OldTimestamp_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds();

        // Act
        var result = authenticator.VerifyRequest("GET", "/api/v1/status", oldTimestamp, "any-signature");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("too old", result.ErrorMessage);
    }

    [Fact]
    public void VerifyRequest_FutureTimestamp_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

        // Act
        var result = authenticator.VerifyRequest("GET", "/api/v1/status", futureTimestamp, "any-signature");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("future", result.ErrorMessage);
    }

    #endregion

    #region Header Verification Tests

    [Fact]
    public void VerifyHeaders_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var headers = authenticator.SignRequest("POST", "/api/v1/report");

        // Act
        var result = authenticator.VerifyHeaders(
            "POST",
            "/api/v1/report",
            headers[ApiAuthenticator.AuthHeader],
            headers[ApiAuthenticator.TimestampHeader]);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void VerifyHeaders_MissingAuthHeader_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());

        // Act
        var result = authenticator.VerifyHeaders("GET", "/api", null, "12345");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Missing auth header", result.ErrorMessage);
    }

    [Fact]
    public void VerifyHeaders_MissingTimestamp_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());

        // Act
        var result = authenticator.VerifyHeaders("GET", "/api", "lic-123:sig", null);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("timestamp", result.ErrorMessage);
    }

    [Fact]
    public void VerifyHeaders_WrongLicenseId_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var headers = authenticator.SignRequest("GET", "/api");

        // Act
        var result = authenticator.VerifyHeaders(
            "GET",
            "/api",
            "wrong-license:signature",
            headers[ApiAuthenticator.TimestampHeader]);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("mismatch", result.ErrorMessage);
    }

    #endregion

    #region Body Hash Tests

    [Fact]
    public void SignRequest_WithBodyHash_IncludesInSignature()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var bodyHash = ApiAuthenticator.HashBody("""{"data":"test"}""");

        // Act
        var headers1 = authenticator.SignRequest("POST", "/api", bodyHash);
        var headers2 = authenticator.SignRequest("POST", "/api", null);

        // Assert - different body hash should produce different signature
        Assert.NotEqual(
            headers1[ApiAuthenticator.AuthHeader],
            headers2[ApiAuthenticator.AuthHeader]);
    }

    [Fact]
    public void VerifyRequest_WithBodyHash_Validates()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var body = """{"report":{"workUnits":100}}""";
        var bodyHash = ApiAuthenticator.HashBody(body);
        var headers = authenticator.SignRequest("POST", "/api/report", bodyHash);

        var authParts = headers[ApiAuthenticator.AuthHeader].Split(':', 2);
        var timestamp = long.Parse(headers[ApiAuthenticator.TimestampHeader]);

        // Act
        var result = authenticator.VerifyRequest("POST", "/api/report", timestamp, authParts[1], bodyHash);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void VerifyRequest_WrongBodyHash_ReturnsFailed()
    {
        // Arrange
        var authenticator = new ApiAuthenticator("lic-123", CreateTestSignature());
        var originalHash = ApiAuthenticator.HashBody("original body");
        var tamperedHash = ApiAuthenticator.HashBody("tampered body");

        var headers = authenticator.SignRequest("POST", "/api", originalHash);
        var authParts = headers[ApiAuthenticator.AuthHeader].Split(':', 2);
        var timestamp = long.Parse(headers[ApiAuthenticator.TimestampHeader]);

        // Act - verify with different body hash
        var result = authenticator.VerifyRequest("POST", "/api", timestamp, authParts[1], tamperedHash);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void HashBody_ProducesConsistentHash()
    {
        // Arrange
        var body = """{"test":"data","number":123}""";

        // Act
        var hash1 = ApiAuthenticator.HashBody(body);
        var hash2 = ApiAuthenticator.HashBody(body);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    #endregion

    #region Helper Methods

    private static string CreateTestSignature()
    {
        // Create a valid Ed25519 signature for testing
        var (privateKey, _) = Ed25519Signer.GenerateKeyPair();
        return Ed25519Signer.Sign("test-license-content", privateKey);
    }

    #endregion
}
