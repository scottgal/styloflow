using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace StyloFlow.Sign;

/// <summary>
/// Package signature manifest that travels with or alongside a package.
/// Supports multiple signatures for cross-signing and trust chains.
/// </summary>
public sealed record PackageSignatureManifest
{
    /// <summary>
    /// Manifest format version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Package identifier (typically filename without extension).
    /// </summary>
    [JsonPropertyName("packageId")]
    public required string PackageId { get; init; }

    /// <summary>
    /// SHA-256 hash of the package file.
    /// </summary>
    [JsonPropertyName("packageHash")]
    public required string PackageHash { get; init; }

    /// <summary>
    /// Size of the package in bytes.
    /// </summary>
    [JsonPropertyName("packageSize")]
    public required long PackageSize { get; init; }

    /// <summary>
    /// When the package was created/signed.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Collection of signatures from different signers.
    /// </summary>
    [JsonPropertyName("signatures")]
    public List<PackageSignature> Signatures { get; init; } = new();
}

/// <summary>
/// A single signature on a package.
/// </summary>
public sealed record PackageSignature
{
    /// <summary>
    /// Signer identity (e.g., "mostlylucid", "vendor.example.com").
    /// </summary>
    [JsonPropertyName("signerId")]
    public required string SignerId { get; init; }

    /// <summary>
    /// Signer display name.
    /// </summary>
    [JsonPropertyName("signerName")]
    public string? SignerName { get; init; }

    /// <summary>
    /// Public key of the signer (for verification).
    /// </summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }

    /// <summary>
    /// Ed25519 signature of the package hash + metadata.
    /// </summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>
    /// When this signature was created.
    /// </summary>
    [JsonPropertyName("signedAt")]
    public required DateTimeOffset SignedAt { get; init; }

    /// <summary>
    /// Signature type: "author" (package creator), "vendor" (distribution), "audit" (third-party).
    /// </summary>
    [JsonPropertyName("signatureType")]
    public required string SignatureType { get; init; }

    /// <summary>
    /// Optional: Reference to a cross-signing certificate.
    /// </summary>
    [JsonPropertyName("crossSignRef")]
    public string? CrossSignRef { get; init; }
}

/// <summary>
/// Key identity stored in a keyring file.
/// </summary>
public sealed record KeyIdentity
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }

    [JsonPropertyName("privateKey")]
    public string? PrivateKey { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "signing";
}

/// <summary>
/// Keyring file containing multiple key identities.
/// </summary>
public sealed record Keyring
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    [JsonPropertyName("keys")]
    public List<KeyIdentity> Keys { get; init; } = new();
}

/// <summary>
/// Trusted keys configuration for verification.
/// </summary>
public sealed record TrustConfig
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Trusted root keys (vendor keys).
    /// </summary>
    [JsonPropertyName("trustedRoots")]
    public List<TrustedKey> TrustedRoots { get; init; } = new();

    /// <summary>
    /// Cross-signing certificates.
    /// </summary>
    [JsonPropertyName("crossSignings")]
    public List<CrossSigning> CrossSignings { get; init; } = new();
}

public sealed record TrustedKey
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }

    [JsonPropertyName("validFrom")]
    public DateTimeOffset? ValidFrom { get; init; }

    [JsonPropertyName("validTo")]
    public DateTimeOffset? ValidTo { get; init; }
}

public sealed record CrossSigning
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// The key being vouched for.
    /// </summary>
    [JsonPropertyName("subjectKeyId")]
    public required string SubjectKeyId { get; init; }

    [JsonPropertyName("subjectPublicKey")]
    public required string SubjectPublicKey { get; init; }

    /// <summary>
    /// The trusted key doing the vouching.
    /// </summary>
    [JsonPropertyName("issuerKeyId")]
    public required string IssuerKeyId { get; init; }

    /// <summary>
    /// Signature from issuer over subject's public key + metadata.
    /// </summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    [JsonPropertyName("signedAt")]
    public required DateTimeOffset SignedAt { get; init; }

    [JsonPropertyName("validTo")]
    public DateTimeOffset? ValidTo { get; init; }
}

/// <summary>
/// Package signing and verification operations.
/// </summary>
public static class PackageSigner
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Generate a new signing key pair.
    /// </summary>
    public static KeyIdentity GenerateKey(string id, string? name = null)
    {
        using var key = Key.Create(Algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });

        return new KeyIdentity
        {
            Id = id,
            Name = name,
            PublicKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            PrivateKey = Convert.ToBase64String(key.Export(KeyBlobFormat.RawPrivateKey)),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Compute SHA-256 hash of a file.
    /// </summary>
    public static async Task<string> HashFileAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Create a signature manifest for a package.
    /// </summary>
    public static async Task<PackageSignatureManifest> CreateManifestAsync(
        string packagePath,
        CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(packagePath);
        var hash = await HashFileAsync(packagePath, ct);

        return new PackageSignatureManifest
        {
            PackageId = Path.GetFileNameWithoutExtension(packagePath),
            PackageHash = hash,
            PackageSize = fileInfo.Length,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Sign a package manifest with a key.
    /// </summary>
    public static PackageSignature SignManifest(
        PackageSignatureManifest manifest,
        KeyIdentity key,
        string signatureType = "author")
    {
        if (string.IsNullOrEmpty(key.PrivateKey))
            throw new InvalidOperationException("Private key required for signing");

        // Create canonical content to sign
        var contentToSign = $"{manifest.PackageId}\n{manifest.PackageHash}\n{manifest.PackageSize}\n{manifest.Timestamp:O}";
        var dataToSign = Encoding.UTF8.GetBytes(contentToSign);

        // Sign with Ed25519
        var privateKeyBytes = Convert.FromBase64String(key.PrivateKey);
        using var signingKey = Key.Import(Algorithm, privateKeyBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.None });

        var signature = Algorithm.Sign(signingKey, dataToSign);

        return new PackageSignature
        {
            SignerId = key.Id,
            SignerName = key.Name,
            PublicKey = key.PublicKey,
            Signature = Convert.ToBase64String(signature),
            SignedAt = DateTimeOffset.UtcNow,
            SignatureType = signatureType
        };
    }

    /// <summary>
    /// Verify a signature on a package manifest.
    /// </summary>
    public static bool VerifySignature(
        PackageSignatureManifest manifest,
        PackageSignature signature)
    {
        try
        {
            var contentToSign = $"{manifest.PackageId}\n{manifest.PackageHash}\n{manifest.PackageSize}\n{manifest.Timestamp:O}";
            var dataToSign = Encoding.UTF8.GetBytes(contentToSign);

            var publicKeyBytes = Convert.FromBase64String(signature.PublicKey);
            var signatureBytes = Convert.FromBase64String(signature.Signature);

            var publicKey = PublicKey.Import(Algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            return Algorithm.Verify(publicKey, dataToSign, signatureBytes);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verify package file matches manifest hash.
    /// </summary>
    public static async Task<bool> VerifyPackageHashAsync(
        string packagePath,
        PackageSignatureManifest manifest,
        CancellationToken ct = default)
    {
        var actualHash = await HashFileAsync(packagePath, ct);
        return actualHash == manifest.PackageHash;
    }

    /// <summary>
    /// Create a cross-signing certificate.
    /// </summary>
    public static CrossSigning CreateCrossSigning(
        KeyIdentity issuerKey,
        KeyIdentity subjectKey,
        DateTimeOffset? validTo = null)
    {
        if (string.IsNullOrEmpty(issuerKey.PrivateKey))
            throw new InvalidOperationException("Issuer private key required for cross-signing");

        var id = $"{issuerKey.Id}->{subjectKey.Id}";
        var contentToSign = $"{subjectKey.Id}\n{subjectKey.PublicKey}\n{DateTimeOffset.UtcNow:O}";
        var dataToSign = Encoding.UTF8.GetBytes(contentToSign);

        var privateKeyBytes = Convert.FromBase64String(issuerKey.PrivateKey);
        using var signingKey = Key.Import(Algorithm, privateKeyBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.None });

        var signature = Algorithm.Sign(signingKey, dataToSign);

        return new CrossSigning
        {
            Id = id,
            SubjectKeyId = subjectKey.Id,
            SubjectPublicKey = subjectKey.PublicKey,
            IssuerKeyId = issuerKey.Id,
            Signature = Convert.ToBase64String(signature),
            SignedAt = DateTimeOffset.UtcNow,
            ValidTo = validTo
        };
    }

    /// <summary>
    /// Save a manifest to a file.
    /// </summary>
    public static async Task SaveManifestAsync(
        PackageSignatureManifest manifest,
        string path,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <summary>
    /// Load a manifest from a file.
    /// </summary>
    public static async Task<PackageSignatureManifest?> LoadManifestAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<PackageSignatureManifest>(json, JsonOptions);
    }

    /// <summary>
    /// Save a keyring to a file.
    /// </summary>
    public static async Task SaveKeyringAsync(
        Keyring keyring,
        string path,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(keyring, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <summary>
    /// Load a keyring from a file.
    /// </summary>
    public static async Task<Keyring?> LoadKeyringAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<Keyring>(json, JsonOptions);
    }

    /// <summary>
    /// Save trust configuration to a file.
    /// </summary>
    public static async Task SaveTrustConfigAsync(
        TrustConfig config,
        string path,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <summary>
    /// Load trust configuration from a file.
    /// </summary>
    public static async Task<TrustConfig?> LoadTrustConfigAsync(
        string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<TrustConfig>(json, JsonOptions);
    }
}
