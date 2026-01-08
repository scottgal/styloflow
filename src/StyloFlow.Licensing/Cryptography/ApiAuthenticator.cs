using System.Security.Cryptography;
using System.Text;

namespace StyloFlow.Licensing.Cryptography;

/// <summary>
/// Authenticates API requests using the license as proof of identity.
/// Uses HMAC-SHA256 with a key derived from the license signature.
/// </summary>
public sealed class ApiAuthenticator
{
    private readonly string _licenseId;
    private readonly byte[] _authKey;

    /// <summary>
    /// Header name for the authentication token.
    /// </summary>
    public const string AuthHeader = "X-StyloFlow-Auth";

    /// <summary>
    /// Header name for the timestamp.
    /// </summary>
    public const string TimestampHeader = "X-StyloFlow-Timestamp";

    /// <summary>
    /// Maximum age of a request before it's considered stale (prevents replay attacks).
    /// </summary>
    public static readonly TimeSpan MaxRequestAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Create an authenticator from a license signature.
    /// The auth key is derived from the signature using HKDF.
    /// </summary>
    /// <param name="licenseId">The license ID.</param>
    /// <param name="licenseSignature">The Ed25519 signature from the license.</param>
    public ApiAuthenticator(string licenseId, string licenseSignature)
    {
        _licenseId = licenseId;
        _authKey = DeriveAuthKey(licenseSignature);
    }

    /// <summary>
    /// Create an authenticator with a pre-shared secret (for testing or alternative auth).
    /// </summary>
    /// <param name="licenseId">The license ID.</param>
    /// <param name="secretKey">A 32-byte secret key.</param>
    /// <param name="isRawKey">Must be true.</param>
    public ApiAuthenticator(string licenseId, byte[] secretKey, bool isRawKey)
    {
        if (!isRawKey)
            throw new ArgumentException("Set isRawKey to true", nameof(isRawKey));
        if (secretKey.Length != 32)
            throw new ArgumentException("Secret key must be 32 bytes", nameof(secretKey));

        _licenseId = licenseId;
        _authKey = secretKey;
    }

    /// <summary>
    /// Sign an API request, returning the authentication headers.
    /// </summary>
    /// <param name="method">HTTP method (GET, POST, etc.).</param>
    /// <param name="path">Request path (e.g., /api/v1/workunit/report).</param>
    /// <param name="bodyHash">Optional SHA256 hash of the request body (for POST/PUT).</param>
    /// <returns>Dictionary of headers to add to the request.</returns>
    public Dictionary<string, string> SignRequest(
        string method,
        string path,
        string? bodyHash = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeRequestSignature(method, path, timestamp, bodyHash);

        return new Dictionary<string, string>
        {
            [TimestampHeader] = timestamp.ToString(),
            [AuthHeader] = $"{_licenseId}:{signature}"
        };
    }

    /// <summary>
    /// Verify an API request signature.
    /// </summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Request path.</param>
    /// <param name="timestamp">Unix timestamp from the request.</param>
    /// <param name="signature">Signature from the request.</param>
    /// <param name="bodyHash">Optional body hash.</param>
    /// <returns>Verification result.</returns>
    public ApiAuthResult VerifyRequest(
        string method,
        string path,
        long timestamp,
        string signature,
        string? bodyHash = null)
    {
        // Check timestamp freshness
        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var age = DateTimeOffset.UtcNow - requestTime;

        if (age > MaxRequestAge)
        {
            return ApiAuthResult.Failed("Request too old (possible replay attack)");
        }

        if (age < -TimeSpan.FromMinutes(1)) // Allow 1 minute clock skew into future
        {
            return ApiAuthResult.Failed("Request timestamp in future");
        }

        // Verify signature
        var expectedSignature = ComputeRequestSignature(method, path, timestamp, bodyHash);

        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature),
            Encoding.UTF8.GetBytes(expectedSignature)))
        {
            return ApiAuthResult.Failed("Invalid signature");
        }

        return ApiAuthResult.Success(_licenseId);
    }

    /// <summary>
    /// Parse and verify authentication headers from a request.
    /// </summary>
    /// <param name="method">HTTP method.</param>
    /// <param name="path">Request path.</param>
    /// <param name="authHeader">Value of X-StyloFlow-Auth header.</param>
    /// <param name="timestampHeader">Value of X-StyloFlow-Timestamp header.</param>
    /// <param name="bodyHash">Optional body hash.</param>
    /// <returns>Verification result.</returns>
    public ApiAuthResult VerifyHeaders(
        string method,
        string path,
        string? authHeader,
        string? timestampHeader,
        string? bodyHash = null)
    {
        if (string.IsNullOrEmpty(authHeader))
            return ApiAuthResult.Failed("Missing auth header");

        if (string.IsNullOrEmpty(timestampHeader) || !long.TryParse(timestampHeader, out var timestamp))
            return ApiAuthResult.Failed("Missing or invalid timestamp header");

        // Parse auth header: licenseId:signature
        var parts = authHeader.Split(':', 2);
        if (parts.Length != 2)
            return ApiAuthResult.Failed("Invalid auth header format");

        var licenseId = parts[0];
        var signature = parts[1];

        if (licenseId != _licenseId)
            return ApiAuthResult.Failed("License ID mismatch");

        return VerifyRequest(method, path, timestamp, signature, bodyHash);
    }

    /// <summary>
    /// Compute SHA256 hash of request body for signing.
    /// </summary>
    public static string HashBody(byte[] body)
    {
        var hash = SHA256.HashData(body);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Compute SHA256 hash of request body for signing.
    /// </summary>
    public static string HashBody(string body)
    {
        return HashBody(Encoding.UTF8.GetBytes(body));
    }

    private string ComputeRequestSignature(string method, string path, long timestamp, string? bodyHash)
    {
        // Canonical string to sign: METHOD\nPATH\nTIMESTAMP\nBODYHASH
        var stringToSign = $"{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{bodyHash ?? ""}";
        var dataToSign = Encoding.UTF8.GetBytes(stringToSign);

        using var hmac = new HMACSHA256(_authKey);
        var signature = hmac.ComputeHash(dataToSign);

        return Convert.ToBase64String(signature);
    }

    private static byte[] DeriveAuthKey(string licenseSignature)
    {
        // Use HKDF to derive a 32-byte key from the license signature
        var signatureBytes = Convert.FromBase64String(licenseSignature);

        // HKDF-SHA256 with info = "styloflow-api-auth"
        var info = Encoding.UTF8.GetBytes("styloflow-api-auth");

        return HKDF.DeriveKey(HashAlgorithmName.SHA256, signatureBytes, 32, info: info);
    }
}

/// <summary>
/// Result of API authentication verification.
/// </summary>
public sealed record ApiAuthResult
{
    /// <summary>
    /// Whether authentication succeeded.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The authenticated license ID (if valid).
    /// </summary>
    public string? LicenseId { get; init; }

    /// <summary>
    /// Error message (if invalid).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static ApiAuthResult Success(string licenseId) => new()
    {
        IsValid = true,
        LicenseId = licenseId
    };

    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static ApiAuthResult Failed(string error) => new()
    {
        IsValid = false,
        ErrorMessage = error
    };
}
