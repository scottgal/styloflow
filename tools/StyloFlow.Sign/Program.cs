using System.CommandLine;
using StyloFlow.Sign;

var rootCommand = new RootCommand("StyloFlow package signing tool - secure your supply chain")
{
    Name = "sfsign"
};

// ============================================================================
// Key Management Commands
// ============================================================================

var keyCommand = new Command("key", "Manage signing keys");
rootCommand.AddCommand(keyCommand);

// sfsign key generate --id <id> --name <name> --keyring <path>
var keyGenCommand = new Command("generate", "Generate a new Ed25519 signing key pair");
var keyIdOption = new Option<string>("--id", "Key identifier (e.g., 'mostlylucid', 'vendor.example')") { IsRequired = true };
var keyNameOption = new Option<string?>("--name", "Display name for the key");
var keyringOption = new Option<string>("--keyring", () => "keyring.json", "Path to keyring file");
keyGenCommand.AddOption(keyIdOption);
keyGenCommand.AddOption(keyNameOption);
keyGenCommand.AddOption(keyringOption);
keyGenCommand.SetHandler(async (string id, string? name, string keyringPath) =>
{
    Console.WriteLine($"Generating Ed25519 key pair for '{id}'...");

    var key = PackageSigner.GenerateKey(id, name);

    // Load or create keyring
    var keyring = await PackageSigner.LoadKeyringAsync(keyringPath) ?? new Keyring();

    // Check for duplicate
    if (keyring.Keys.Any(k => k.Id == id))
    {
        Console.Error.WriteLine($"Error: Key with ID '{id}' already exists in keyring");
        Environment.Exit(1);
    }

    keyring.Keys.Add(key);
    await PackageSigner.SaveKeyringAsync(keyring, keyringPath);

    Console.WriteLine($"Key generated successfully!");
    Console.WriteLine($"  ID:         {key.Id}");
    Console.WriteLine($"  Name:       {key.Name ?? "(none)"}");
    Console.WriteLine($"  Public Key: {key.PublicKey}");
    Console.WriteLine($"  Keyring:    {keyringPath}");
    Console.WriteLine();
    Console.WriteLine("IMPORTANT: Keep your keyring file secure! It contains private keys.");

}, keyIdOption, keyNameOption, keyringOption);
keyCommand.AddCommand(keyGenCommand);

// sfsign key list --keyring <path>
var keyListCommand = new Command("list", "List keys in a keyring");
keyListCommand.AddOption(keyringOption);
keyListCommand.SetHandler(async (string keyringPath) =>
{
    var keyring = await PackageSigner.LoadKeyringAsync(keyringPath);
    if (keyring == null || keyring.Keys.Count == 0)
    {
        Console.WriteLine("No keys found in keyring.");
        return;
    }

    Console.WriteLine($"Keys in {keyringPath}:");
    Console.WriteLine(new string('-', 80));

    foreach (var key in keyring.Keys)
    {
        var hasPrivate = !string.IsNullOrEmpty(key.PrivateKey) ? "yes" : "no";
        Console.WriteLine($"  {key.Id,-20} {key.Name ?? "",-20} Private: {hasPrivate}");
        Console.WriteLine($"    Public: {key.PublicKey[..20]}...");
        Console.WriteLine($"    Created: {key.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();
    }
}, keyringOption);
keyCommand.AddCommand(keyListCommand);

// sfsign key export-public --id <id> --keyring <path> --output <path>
var keyExportCommand = new Command("export-public", "Export public key for distribution");
var keyExportIdOption = new Option<string>("--id", "Key ID to export") { IsRequired = true };
var keyExportOutputOption = new Option<string?>("--output", "Output file (or stdout if not specified)");
keyExportCommand.AddOption(keyExportIdOption);
keyExportCommand.AddOption(keyringOption);
keyExportCommand.AddOption(keyExportOutputOption);
keyExportCommand.SetHandler(async (string id, string keyringPath, string? output) =>
{
    var keyring = await PackageSigner.LoadKeyringAsync(keyringPath);
    var key = keyring?.Keys.FirstOrDefault(k => k.Id == id);

    if (key == null)
    {
        Console.Error.WriteLine($"Error: Key '{id}' not found");
        Environment.Exit(1);
    }

    var publicOnlyKey = new KeyIdentity
    {
        Id = key.Id,
        Name = key.Name,
        PublicKey = key.PublicKey,
        CreatedAt = key.CreatedAt
    };

    var json = System.Text.Json.JsonSerializer.Serialize(publicOnlyKey, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    if (string.IsNullOrEmpty(output))
    {
        Console.WriteLine(json);
    }
    else
    {
        await File.WriteAllTextAsync(output, json);
        Console.WriteLine($"Public key exported to: {output}");
    }
}, keyExportIdOption, keyringOption, keyExportOutputOption);
keyCommand.AddCommand(keyExportCommand);

// ============================================================================
// Signing Commands
// ============================================================================

var signCommand = new Command("sign", "Sign a package file");
var packageOption = new Option<string>("--package", "Path to package file (.sfpkg, .nupkg, etc.)") { IsRequired = true };
var keyIdSignOption = new Option<string>("--key", "Key ID to use for signing") { IsRequired = true };
var signTypeOption = new Option<string>("--type", () => "author", "Signature type: author, vendor, audit");
var manifestOption = new Option<string?>("--manifest", "Path to signature manifest (default: <package>.sig.json)");
signCommand.AddOption(packageOption);
signCommand.AddOption(keyringOption);
signCommand.AddOption(keyIdSignOption);
signCommand.AddOption(signTypeOption);
signCommand.AddOption(manifestOption);
signCommand.SetHandler(async (string packagePath, string keyringPath, string keyId, string signType, string? manifestPath) =>
{
    if (!File.Exists(packagePath))
    {
        Console.Error.WriteLine($"Error: Package not found: {packagePath}");
        Environment.Exit(1);
    }

    var keyring = await PackageSigner.LoadKeyringAsync(keyringPath);
    var key = keyring?.Keys.FirstOrDefault(k => k.Id == keyId);

    if (key == null)
    {
        Console.Error.WriteLine($"Error: Key '{keyId}' not found in keyring");
        Environment.Exit(1);
    }

    if (string.IsNullOrEmpty(key.PrivateKey))
    {
        Console.Error.WriteLine($"Error: Key '{keyId}' does not have a private key (cannot sign)");
        Environment.Exit(1);
    }

    var actualManifestPath = manifestPath ?? $"{packagePath}.sig.json";

    Console.WriteLine($"Signing package: {packagePath}");
    Console.WriteLine($"  Using key: {keyId}");

    // Load existing manifest or create new one
    var manifest = await PackageSigner.LoadManifestAsync(actualManifestPath);
    if (manifest == null)
    {
        manifest = await PackageSigner.CreateManifestAsync(packagePath);
        Console.WriteLine($"  Created new manifest (hash: {manifest.PackageHash[..20]}...)");
    }
    else
    {
        // Verify existing hash matches
        var currentHash = await PackageSigner.HashFileAsync(packagePath);
        if (currentHash != manifest.PackageHash)
        {
            Console.Error.WriteLine($"Error: Package hash does not match existing manifest!");
            Console.Error.WriteLine($"  Expected: {manifest.PackageHash}");
            Console.Error.WriteLine($"  Actual:   {currentHash}");
            Environment.Exit(1);
        }
        Console.WriteLine($"  Using existing manifest (hash verified)");
    }

    // Check if this key already signed
    if (manifest.Signatures.Any(s => s.SignerId == keyId))
    {
        Console.Error.WriteLine($"Error: Key '{keyId}' has already signed this package");
        Environment.Exit(1);
    }

    // Sign and add to manifest
    var signature = PackageSigner.SignManifest(manifest, key, signType);
    manifest.Signatures.Add(signature);

    await PackageSigner.SaveManifestAsync(manifest, actualManifestPath);

    Console.WriteLine($"  Signature type: {signType}");
    Console.WriteLine($"  Signature: {signature.Signature[..20]}...");
    Console.WriteLine();
    Console.WriteLine($"Manifest saved: {actualManifestPath}");
    Console.WriteLine($"Total signatures: {manifest.Signatures.Count}");

}, packageOption, keyringOption, keyIdSignOption, signTypeOption, manifestOption);
rootCommand.AddCommand(signCommand);

// ============================================================================
// Verification Commands
// ============================================================================

var verifyCommand = new Command("verify", "Verify a signed package");
var trustConfigOption = new Option<string?>("--trust", "Path to trust configuration file");
var requireOption = new Option<string[]>("--require", () => Array.Empty<string>(), "Required signer IDs");
verifyCommand.AddOption(packageOption);
verifyCommand.AddOption(manifestOption);
verifyCommand.AddOption(trustConfigOption);
verifyCommand.AddOption(requireOption);
verifyCommand.SetHandler(async (string packagePath, string? manifestPath, string? trustPath, string[] required) =>
{
    var actualManifestPath = manifestPath ?? $"{packagePath}.sig.json";

    if (!File.Exists(packagePath))
    {
        Console.Error.WriteLine($"Error: Package not found: {packagePath}");
        Environment.Exit(1);
    }

    if (!File.Exists(actualManifestPath))
    {
        Console.Error.WriteLine($"Error: Signature manifest not found: {actualManifestPath}");
        Environment.Exit(1);
    }

    var manifest = await PackageSigner.LoadManifestAsync(actualManifestPath);
    if (manifest == null)
    {
        Console.Error.WriteLine($"Error: Failed to parse manifest");
        Environment.Exit(1);
    }

    Console.WriteLine($"Verifying: {packagePath}");
    Console.WriteLine(new string('-', 60));

    // Verify package hash
    var hashValid = await PackageSigner.VerifyPackageHashAsync(packagePath, manifest);
    Console.WriteLine($"Package hash: {(hashValid ? "VALID" : "INVALID")}");

    if (!hashValid)
    {
        Console.Error.WriteLine("ERROR: Package has been modified!");
        Environment.Exit(1);
    }

    // Load trust config if provided
    TrustConfig? trust = null;
    if (!string.IsNullOrEmpty(trustPath))
    {
        trust = await PackageSigner.LoadTrustConfigAsync(trustPath);
    }

    // Verify each signature
    Console.WriteLine();
    Console.WriteLine("Signatures:");
    var allValid = true;
    var validSigners = new List<string>();

    foreach (var sig in manifest.Signatures)
    {
        var isValid = PackageSigner.VerifySignature(manifest, sig);
        var status = isValid ? "VALID" : "INVALID";
        var trusted = "";

        if (trust != null && isValid)
        {
            var isTrusted = trust.TrustedRoots.Any(t => t.Id == sig.SignerId && t.PublicKey == sig.PublicKey);
            trusted = isTrusted ? " (TRUSTED)" : " (untrusted)";
        }

        Console.WriteLine($"  [{status}] {sig.SignerId} ({sig.SignatureType}){trusted}");
        Console.WriteLine($"         Signed: {sig.SignedAt:yyyy-MM-dd HH:mm:ss}");

        if (!isValid)
            allValid = false;
        else
            validSigners.Add(sig.SignerId);
    }

    // Check required signers
    if (required.Length > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Required signers:");
        foreach (var req in required)
        {
            var found = validSigners.Contains(req);
            Console.WriteLine($"  {req}: {(found ? "PRESENT" : "MISSING")}");
            if (!found)
                allValid = false;
        }
    }

    Console.WriteLine();
    if (allValid)
    {
        Console.WriteLine("VERIFICATION PASSED");
        Environment.Exit(0);
    }
    else
    {
        Console.WriteLine("VERIFICATION FAILED");
        Environment.Exit(1);
    }

}, packageOption, manifestOption, trustConfigOption, requireOption);
rootCommand.AddCommand(verifyCommand);

// ============================================================================
// Cross-Signing Commands
// ============================================================================

var crossSignCommand = new Command("cross-sign", "Create a cross-signing certificate");
var issuerKeyOption = new Option<string>("--issuer", "Issuer key ID (the trusted key)") { IsRequired = true };
var subjectKeyOption = new Option<string>("--subject", "Subject key ID (the key being vouched for)") { IsRequired = true };
var subjectKeyringOption = new Option<string?>("--subject-keyring", "Keyring containing subject key (if different)");
var validToOption = new Option<DateTime?>("--valid-to", "Expiration date for the cross-signing");
var crossSignOutputOption = new Option<string>("--output", () => "cross-signing.json", "Output file for cross-signing config");
crossSignCommand.AddOption(issuerKeyOption);
crossSignCommand.AddOption(subjectKeyOption);
crossSignCommand.AddOption(keyringOption);
crossSignCommand.AddOption(subjectKeyringOption);
crossSignCommand.AddOption(validToOption);
crossSignCommand.AddOption(crossSignOutputOption);
crossSignCommand.SetHandler(async (string issuerId, string subjectId, string keyringPath, string? subjectKeyringPath, DateTime? validTo, string output) =>
{
    var issuerKeyring = await PackageSigner.LoadKeyringAsync(keyringPath);
    var issuerKey = issuerKeyring?.Keys.FirstOrDefault(k => k.Id == issuerId);

    if (issuerKey == null)
    {
        Console.Error.WriteLine($"Error: Issuer key '{issuerId}' not found");
        Environment.Exit(1);
    }

    var subjectKeyring = await PackageSigner.LoadKeyringAsync(subjectKeyringPath ?? keyringPath);
    var subjectKey = subjectKeyring?.Keys.FirstOrDefault(k => k.Id == subjectId);

    if (subjectKey == null)
    {
        Console.Error.WriteLine($"Error: Subject key '{subjectId}' not found");
        Environment.Exit(1);
    }

    Console.WriteLine($"Creating cross-signing certificate...");
    Console.WriteLine($"  Issuer (trusted):  {issuerId}");
    Console.WriteLine($"  Subject (vouched): {subjectId}");

    var crossSign = PackageSigner.CreateCrossSigning(
        issuerKey,
        subjectKey,
        validTo.HasValue ? new DateTimeOffset(validTo.Value, TimeSpan.Zero) : null);

    // Load or create trust config
    var trustConfig = await PackageSigner.LoadTrustConfigAsync(output) ?? new TrustConfig();

    // Add the cross-signing
    trustConfig.CrossSignings.RemoveAll(c => c.Id == crossSign.Id);
    trustConfig.CrossSignings.Add(crossSign);

    await PackageSigner.SaveTrustConfigAsync(trustConfig, output);

    Console.WriteLine();
    Console.WriteLine($"Cross-signing certificate created: {crossSign.Id}");
    Console.WriteLine($"Saved to: {output}");

}, issuerKeyOption, subjectKeyOption, keyringOption, subjectKeyringOption, validToOption, crossSignOutputOption);
rootCommand.AddCommand(crossSignCommand);

// ============================================================================
// Trust Commands
// ============================================================================

var trustCommand = new Command("trust", "Manage trusted keys");
rootCommand.AddCommand(trustCommand);

// sfsign trust add --key <id> --keyring <path> --config <path>
var trustAddCommand = new Command("add", "Add a key to the trust configuration");
var trustKeyOption = new Option<string>("--key", "Key ID to trust") { IsRequired = true };
var trustConfigPathOption = new Option<string>("--config", () => "trust.json", "Trust configuration file");
trustAddCommand.AddOption(trustKeyOption);
trustAddCommand.AddOption(keyringOption);
trustAddCommand.AddOption(trustConfigPathOption);
trustAddCommand.SetHandler(async (string keyId, string keyringPath, string configPath) =>
{
    var keyring = await PackageSigner.LoadKeyringAsync(keyringPath);
    var key = keyring?.Keys.FirstOrDefault(k => k.Id == keyId);

    if (key == null)
    {
        Console.Error.WriteLine($"Error: Key '{keyId}' not found");
        Environment.Exit(1);
    }

    var trustConfig = await PackageSigner.LoadTrustConfigAsync(configPath) ?? new TrustConfig();

    if (trustConfig.TrustedRoots.Any(t => t.Id == keyId))
    {
        Console.WriteLine($"Key '{keyId}' is already trusted");
        return;
    }

    trustConfig.TrustedRoots.Add(new TrustedKey
    {
        Id = key.Id,
        Name = key.Name,
        PublicKey = key.PublicKey,
        ValidFrom = DateTimeOffset.UtcNow
    });

    await PackageSigner.SaveTrustConfigAsync(trustConfig, configPath);
    Console.WriteLine($"Key '{keyId}' added to trusted roots in {configPath}");

}, trustKeyOption, keyringOption, trustConfigPathOption);
trustCommand.AddCommand(trustAddCommand);

// sfsign trust list --config <path>
var trustListCommand = new Command("list", "List trusted keys");
trustListCommand.AddOption(trustConfigPathOption);
trustListCommand.SetHandler(async (string configPath) =>
{
    var trustConfig = await PackageSigner.LoadTrustConfigAsync(configPath);
    if (trustConfig == null || trustConfig.TrustedRoots.Count == 0)
    {
        Console.WriteLine("No trusted keys configured.");
        return;
    }

    Console.WriteLine("Trusted root keys:");
    foreach (var key in trustConfig.TrustedRoots)
    {
        Console.WriteLine($"  {key.Id,-20} {key.Name ?? ""}");
        Console.WriteLine($"    Public: {key.PublicKey[..20]}...");
    }

    if (trustConfig.CrossSignings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Cross-signings:");
        foreach (var cs in trustConfig.CrossSignings)
        {
            Console.WriteLine($"  {cs.IssuerKeyId} -> {cs.SubjectKeyId}");
            Console.WriteLine($"    Signed: {cs.SignedAt:yyyy-MM-dd}");
        }
    }
}, trustConfigPathOption);
trustCommand.AddCommand(trustListCommand);

return await rootCommand.InvokeAsync(args);
