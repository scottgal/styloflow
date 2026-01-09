namespace StyloFlow.Converters;

/// <summary>
/// Options for content conversion.
/// </summary>
public class ConversionOptions
{
    /// <summary>
    /// Output format to convert to.
    /// </summary>
    public string OutputFormat { get; set; } = "markdown";

    /// <summary>
    /// Whether to include images/assets in output.
    /// </summary>
    public bool ExtractAssets { get; set; } = true;

    /// <summary>
    /// Whether to preserve page structure (for PDFs).
    /// </summary>
    public bool PreservePageStructure { get; set; } = true;

    /// <summary>
    /// Timeout for conversion in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Additional converter-specific options.
    /// </summary>
    public Dictionary<string, object>? Options { get; set; }

    /// <summary>
    /// Cancellation token.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// Result of a conversion operation.
/// Output content is stored in shared storage, not passed directly.
/// </summary>
public class ConversionResult
{
    /// <summary>
    /// Whether conversion succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Path to converted content in shared storage.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// MIME type of output content.
    /// </summary>
    public string? OutputMimeType { get; init; }

    /// <summary>
    /// Size of output in bytes.
    /// </summary>
    public long? OutputSizeBytes { get; init; }

    /// <summary>
    /// Hash of output content for deduplication.
    /// </summary>
    public string? OutputHash { get; init; }

    /// <summary>
    /// Extracted assets (paths in shared storage).
    /// </summary>
    public IReadOnlyList<ConvertedAsset>? Assets { get; init; }

    /// <summary>
    /// Metadata extracted during conversion.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Error message if conversion failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Time taken for conversion.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Converter that produced this result.
    /// </summary>
    public string? ProducerName { get; init; }

    public static ConversionResult Failure(string error, TimeSpan duration = default) => new()
    {
        Success = false,
        Error = error,
        Duration = duration
    };
}

/// <summary>
/// An asset extracted during conversion (image, attachment, etc.).
/// Assets are stored in shared storage.
/// </summary>
public record ConvertedAsset
{
    /// <summary>
    /// Path to asset in shared storage.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Original filename.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// MIME type of asset.
    /// </summary>
    public required string MimeType { get; init; }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Page number or location in source (if applicable).
    /// </summary>
    public int? SourcePage { get; init; }
}

/// <summary>
/// Progress info for long-running conversions.
/// </summary>
public class ConversionProgress
{
    public double PercentComplete { get; init; }
    public string? CurrentStep { get; init; }
    public int? CurrentPage { get; init; }
    public int? TotalPages { get; init; }
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Interface for content converters.
/// Converters transform files from one format to another (e.g., PDF to Markdown).
///
/// IMPORTANT: Converters save output to shared storage - never in signals!
/// Signals should only contain paths/references to converted content.
/// </summary>
public interface IContentConverter
{
    /// <summary>
    /// Unique identifier for this converter.
    /// </summary>
    string ConverterId { get; }

    /// <summary>
    /// Human-readable name.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Input MIME types this converter can handle.
    /// </summary>
    IReadOnlyList<string> SupportedInputTypes { get; }

    /// <summary>
    /// Output MIME types this converter can produce.
    /// </summary>
    IReadOnlyList<string> SupportedOutputTypes { get; }

    /// <summary>
    /// Priority when multiple converters support the same type.
    /// Higher priority wins.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Check if converter can handle the given input.
    /// </summary>
    bool CanConvert(string inputPath, string inputMimeType, string outputFormat);

    /// <summary>
    /// Convert content from input to output format.
    /// Output is saved to shared storage; result contains the path.
    /// </summary>
    /// <param name="inputPath">Path to input file (in shared storage or filesystem).</param>
    /// <param name="inputMimeType">MIME type of input.</param>
    /// <param name="options">Conversion options.</param>
    /// <param name="progress">Optional progress callback.</param>
    /// <returns>Conversion result with output path in shared storage.</returns>
    Task<ConversionResult> ConvertAsync(
        string inputPath,
        string inputMimeType,
        ConversionOptions options,
        IProgress<ConversionProgress>? progress = null);

    /// <summary>
    /// Check if the converter backend is available/healthy.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
