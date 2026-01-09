using Microsoft.Extensions.Logging;

namespace StyloFlow.Converters;

/// <summary>
/// Registry for content converters.
/// Routes conversion requests to the appropriate converter based on input/output types.
/// </summary>
public interface IConverterRegistry
{
    /// <summary>
    /// Get all registered converters.
    /// </summary>
    IReadOnlyList<IContentConverter> Converters { get; }

    /// <summary>
    /// Find the best converter for the given input and output types.
    /// </summary>
    IContentConverter? FindConverter(string inputMimeType, string outputFormat);

    /// <summary>
    /// Find all converters that can handle the given input type.
    /// </summary>
    IReadOnlyList<IContentConverter> FindConvertersForInput(string inputMimeType);

    /// <summary>
    /// Check if any converter can handle the conversion.
    /// </summary>
    bool CanConvert(string inputMimeType, string outputFormat);

    /// <summary>
    /// Register a converter.
    /// </summary>
    void Register(IContentConverter converter);
}

/// <summary>
/// Default implementation of converter registry.
/// </summary>
public class ConverterRegistry : IConverterRegistry
{
    private readonly List<IContentConverter> _converters = [];
    private readonly ILogger<ConverterRegistry>? _logger;
    private readonly object _lock = new();

    public ConverterRegistry(ILogger<ConverterRegistry>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<IContentConverter> Converters
    {
        get
        {
            lock (_lock)
            {
                return _converters.ToList().AsReadOnly();
            }
        }
    }

    public IContentConverter? FindConverter(string inputMimeType, string outputFormat)
    {
        lock (_lock)
        {
            return _converters
                .Where(c => c.SupportedInputTypes.Contains(inputMimeType, StringComparer.OrdinalIgnoreCase) &&
                           c.SupportedOutputTypes.Any(o => o.Contains(outputFormat, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(c => c.Priority)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<IContentConverter> FindConvertersForInput(string inputMimeType)
    {
        lock (_lock)
        {
            return _converters
                .Where(c => c.SupportedInputTypes.Contains(inputMimeType, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Priority)
                .ToList()
                .AsReadOnly();
        }
    }

    public bool CanConvert(string inputMimeType, string outputFormat)
    {
        return FindConverter(inputMimeType, outputFormat) != null;
    }

    public void Register(IContentConverter converter)
    {
        lock (_lock)
        {
            _converters.Add(converter);
            _logger?.LogDebug("Registered converter: {ConverterId} ({DisplayName})",
                converter.ConverterId, converter.DisplayName);
        }
    }
}

/// <summary>
/// Helper to chain multiple converters for complex conversions.
/// e.g., PDF -> Markdown -> HTML
/// </summary>
public class ConverterChain
{
    private readonly IConverterRegistry _registry;
    private readonly ISharedStorage _storage;
    private readonly ILogger<ConverterChain>? _logger;

    public ConverterChain(
        IConverterRegistry registry,
        ISharedStorage storage,
        ILogger<ConverterChain>? logger = null)
    {
        _registry = registry;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Convert through a chain of formats.
    /// </summary>
    /// <param name="inputPath">Input path in shared storage.</param>
    /// <param name="inputMimeType">Input MIME type.</param>
    /// <param name="formats">Formats to convert through (e.g., "markdown", "html").</param>
    /// <param name="options">Conversion options.</param>
    /// <returns>Final conversion result.</returns>
    public async Task<ConversionResult> ConvertThroughAsync(
        string inputPath,
        string inputMimeType,
        string[] formats,
        ConversionOptions options)
    {
        var currentPath = inputPath;
        var currentMimeType = inputMimeType;
        ConversionResult? lastResult = null;

        foreach (var format in formats)
        {
            var converter = _registry.FindConverter(currentMimeType, format);
            if (converter == null)
            {
                return ConversionResult.Failure(
                    $"No converter found for {currentMimeType} -> {format}");
            }

            _logger?.LogDebug("Converting {Input} to {Output} using {Converter}",
                currentMimeType, format, converter.ConverterId);

            lastResult = await converter.ConvertAsync(currentPath, currentMimeType, options);
            if (!lastResult.Success)
            {
                return lastResult;
            }

            currentPath = lastResult.OutputPath!;
            currentMimeType = lastResult.OutputMimeType!;
        }

        return lastResult ?? ConversionResult.Failure("No conversions performed");
    }
}
