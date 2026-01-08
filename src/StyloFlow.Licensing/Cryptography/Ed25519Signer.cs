using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace StyloFlow.Licensing.Cryptography;

/// <summary>
/// Ed25519 digital signature operations for license signing.
/// Uses the NSec library for secure, constant-time Ed25519 operations.
/// </summary>
public static class Ed25519Signer
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    /// <summary>
    /// Generate a new Ed25519 key pair for signing licenses.
    /// The private key should be kept secure on the licensing server.
    /// The public key can be embedded in client applications.
    /// </summary>
    /// <returns>Base64-encoded private and public keys.</returns>
    public static (string PrivateKey, string PublicKey) GenerateKeyPair()
    {
        using var key = Key.Create(Algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });

        var privateKeyBytes = key.Export(KeyBlobFormat.RawPrivateKey);
        var publicKeyBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        return (
            Convert.ToBase64String(privateKeyBytes),
            Convert.ToBase64String(publicKeyBytes)
        );
    }

    /// <summary>
    /// Sign data with an Ed25519 private key.
    /// </summary>
    /// <param name="data">The data to sign.</param>
    /// <param name="privateKeyBase64">The base64-encoded private key.</param>
    /// <returns>Base64-encoded signature.</returns>
    public static string Sign(byte[] data, string privateKeyBase64)
    {
        var privateKeyBytes = Convert.FromBase64String(privateKeyBase64);

        using var key = Key.Import(Algorithm, privateKeyBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.None });

        var signature = Algorithm.Sign(key, data);
        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// Sign a string message with an Ed25519 private key.
    /// </summary>
    /// <param name="message">The message to sign (UTF-8 encoded).</param>
    /// <param name="privateKeyBase64">The base64-encoded private key.</param>
    /// <returns>Base64-encoded signature.</returns>
    public static string Sign(string message, string privateKeyBase64)
    {
        return Sign(Encoding.UTF8.GetBytes(message), privateKeyBase64);
    }

    /// <summary>
    /// Verify a signature with an Ed25519 public key.
    /// </summary>
    /// <param name="data">The original data that was signed.</param>
    /// <param name="signatureBase64">The base64-encoded signature.</param>
    /// <param name="publicKeyBase64">The base64-encoded public key.</param>
    /// <returns>True if the signature is valid.</returns>
    public static bool Verify(byte[] data, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            var publicKey = PublicKey.Import(Algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            return Algorithm.Verify(publicKey, data, signatureBytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verify a signature on a string message with an Ed25519 public key.
    /// </summary>
    /// <param name="message">The original message (UTF-8 encoded).</param>
    /// <param name="signatureBase64">The base64-encoded signature.</param>
    /// <param name="publicKeyBase64">The base64-encoded public key.</param>
    /// <returns>True if the signature is valid.</returns>
    public static bool Verify(string message, string signatureBase64, string publicKeyBase64)
    {
        return Verify(Encoding.UTF8.GetBytes(message), signatureBase64, publicKeyBase64);
    }

    /// <summary>
    /// Extract the public key from a private key.
    /// </summary>
    /// <param name="privateKeyBase64">The base64-encoded private key.</param>
    /// <returns>Base64-encoded public key.</returns>
    public static string GetPublicKey(string privateKeyBase64)
    {
        var privateKeyBytes = Convert.FromBase64String(privateKeyBase64);

        using var key = Key.Import(Algorithm, privateKeyBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.None });

        var publicKeyBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        return Convert.ToBase64String(publicKeyBytes);
    }
}

/// <summary>
/// License signing operations using Ed25519.
/// Provides higher-level operations for signing and verifying license tokens.
/// </summary>
public sealed class LicenseSigningService
{
    private readonly string? _privateKey;
    private readonly string _publicKey;

    /// <summary>
    /// Create a signing service with both private and public keys (for license issuer).
    /// </summary>
    /// <param name="privateKeyBase64">The base64-encoded private key.</param>
    public LicenseSigningService(string privateKeyBase64)
    {
        _privateKey = privateKeyBase64;
        _publicKey = Ed25519Signer.GetPublicKey(privateKeyBase64);
    }

    /// <summary>
    /// Create a verification-only service with just the public key (for clients).
    /// </summary>
    /// <param name="publicKeyBase64">The base64-encoded public key.</param>
    /// <param name="isPublicKey">Must be true to indicate this is a public key.</param>
    public LicenseSigningService(string publicKeyBase64, bool isPublicKey)
    {
        if (!isPublicKey)
            throw new ArgumentException("Set isPublicKey to true when providing a public key", nameof(isPublicKey));

        _privateKey = null;
        _publicKey = publicKeyBase64;
    }

    /// <summary>
    /// Gets the public key (safe to embed in applications).
    /// </summary>
    public string PublicKey => _publicKey;

    /// <summary>
    /// Whether this service can sign (has private key).
    /// </summary>
    public bool CanSign => _privateKey != null;

    /// <summary>
    /// Sign a license token JSON. The signature covers all fields except 'signature'.
    /// </summary>
    /// <param name="licenseJson">The license token JSON without signature.</param>
    /// <returns>The license JSON with signature field added.</returns>
    public string SignLicense(string licenseJson)
    {
        if (_privateKey == null)
            throw new InvalidOperationException("Cannot sign without private key");

        // Parse, add signature, serialize
        var doc = JsonDocument.Parse(licenseJson);
        var signableContent = GetSignableContent(doc);
        var signature = Ed25519Signer.Sign(signableContent, _privateKey);

        // Add signature to the JSON
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name != "signature")
                {
                    prop.WriteTo(writer);
                }
            }

            writer.WriteString("signature", signature);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Verify a signed license token.
    /// </summary>
    /// <param name="licenseJson">The license JSON with signature.</param>
    /// <returns>True if the signature is valid.</returns>
    public bool VerifyLicense(string licenseJson)
    {
        try
        {
            var doc = JsonDocument.Parse(licenseJson);

            if (!doc.RootElement.TryGetProperty("signature", out var signatureProp))
                return false;

            var signature = signatureProp.GetString();
            if (string.IsNullOrEmpty(signature))
                return false;

            var signableContent = GetSignableContent(doc);
            return Ed25519Signer.Verify(signableContent, signature, _publicKey);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the canonical signable content from a license document.
    /// This is deterministic and excludes the signature field.
    /// </summary>
    private static string GetSignableContent(JsonDocument doc)
    {
        // Create canonical JSON by sorting keys and excluding signature
        var sortedProperties = doc.RootElement
            .EnumerateObject()
            .Where(p => p.Name != "signature")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            foreach (var prop in sortedProperties)
            {
                prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
