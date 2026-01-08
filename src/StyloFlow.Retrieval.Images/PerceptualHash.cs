using System.Security.Cryptography;
using System.Text;

namespace StyloFlow.Retrieval.Images;

/// <summary>
/// Perceptual hashing algorithms for image similarity detection.
/// These hashes are robust to minor image modifications (resize, compression, color).
///
/// Algorithms:
/// - PDQ-style DCT hash (robust to rotation, scaling)
/// - Block mean hash (fast, compression resistant)
/// - Color histogram hash (geometric transform resistant)
/// </summary>
public static class PerceptualHash
{
    private const int DctHashSize = 16;
    private const int BlockHashSize = 8;
    private const int ColorBins = 16;

    /// <summary>
    /// Compute PDQ-style perceptual hash using DCT (Discrete Cosine Transform).
    /// Resistant to: rotation, scaling, compression, color adjustments.
    /// </summary>
    /// <param name="luminanceGrid">16x16 grayscale luminance values (0-1).</param>
    /// <returns>64-character hex hash.</returns>
    public static string ComputePdqHash(double[,] luminanceGrid)
    {
        if (luminanceGrid.GetLength(0) != DctHashSize || luminanceGrid.GetLength(1) != DctHashSize)
            throw new ArgumentException($"Grid must be {DctHashSize}x{DctHashSize}");

        var dct = ComputeDct2D(luminanceGrid);
        var median = ComputeMedian(dct);

        var hash = new StringBuilder();
        for (int y = 0; y < DctHashSize; y++)
        {
            for (int x = 0; x < DctHashSize; x++)
            {
                if (x == 0 && y == 0) continue; // Skip DC component
                hash.Append(dct[x, y] > median ? '1' : '0');
            }
        }

        return ConvertBinaryToHex(hash.ToString());
    }

    /// <summary>
    /// Compute block mean hash (aHash-like).
    /// Fast and resistant to compression and minor edits.
    /// </summary>
    /// <param name="luminanceGrid">8x8 grayscale luminance values (0-255).</param>
    /// <returns>16-character hex hash (64 bits).</returns>
    public static string ComputeBlockHash(double[,] luminanceGrid)
    {
        if (luminanceGrid.GetLength(0) != BlockHashSize || luminanceGrid.GetLength(1) != BlockHashSize)
            throw new ArgumentException($"Grid must be {BlockHashSize}x{BlockHashSize}");

        var median = ComputeMedian(luminanceGrid);
        var hash = new StringBuilder();

        for (int y = 0; y < BlockHashSize; y++)
        {
            for (int x = 0; x < BlockHashSize; x++)
            {
                hash.Append(luminanceGrid[x, y] > median ? '1' : '0');
            }
        }

        return ConvertBinaryToHex(hash.ToString());
    }

    /// <summary>
    /// Compute color histogram hash.
    /// Resistant to: rotation, cropping, scaling, geometric transforms.
    /// </summary>
    /// <param name="rHistogram">Red channel histogram (16 bins, normalized 0-1).</param>
    /// <param name="gHistogram">Green channel histogram.</param>
    /// <param name="bHistogram">Blue channel histogram.</param>
    /// <returns>32-character hex hash.</returns>
    public static string ComputeColorHash(double[] rHistogram, double[] gHistogram, double[] bHistogram)
    {
        if (rHistogram.Length != ColorBins || gHistogram.Length != ColorBins || bHistogram.Length != ColorBins)
            throw new ArgumentException($"Histograms must have {ColorBins} bins each");

        var combined = new StringBuilder();
        for (int i = 0; i < ColorBins; i++)
        {
            combined.Append($"{(byte)(rHistogram[i] * 255):X2}");
            combined.Append($"{(byte)(gHistogram[i] * 255):X2}");
            combined.Append($"{(byte)(bHistogram[i] * 255):X2}");
        }

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined.ToString()));
        return Convert.ToHexString(hashBytes)[..32];
    }

    /// <summary>
    /// Compute composite fingerprint combining multiple hash types.
    /// </summary>
    public static string ComputeCompositeHash(string pdqHash, string blockHash, string colorHash)
    {
        var combined = $"{pdqHash}|{blockHash}|{colorHash}";
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Calculate Hamming distance between two hex hashes.
    /// Lower distance = more similar images.
    /// </summary>
    /// <param name="hash1">First hash (hex string).</param>
    /// <param name="hash2">Second hash (hex string, same length).</param>
    /// <returns>Number of differing bits.</returns>
    public static int HammingDistance(string hash1, string hash2)
    {
        if (hash1.Length != hash2.Length)
            throw new ArgumentException("Hashes must be same length");

        var distance = 0;
        for (int i = 0; i < hash1.Length; i++)
        {
            var b1 = Convert.ToInt32(hash1[i].ToString(), 16);
            var b2 = Convert.ToInt32(hash2[i].ToString(), 16);
            var xor = b1 ^ b2;
            distance += System.Numerics.BitOperations.PopCount((uint)xor);
        }

        return distance;
    }

    /// <summary>
    /// Calculate normalized similarity (0-1) from Hamming distance.
    /// </summary>
    public static double HashSimilarity(string hash1, string hash2)
    {
        var distance = HammingDistance(hash1, hash2);
        var maxDistance = hash1.Length * 4; // 4 bits per hex char
        return 1.0 - (double)distance / maxDistance;
    }

    /// <summary>
    /// Simplified 2D DCT implementation.
    /// </summary>
    private static double[,] ComputeDct2D(double[,] input)
    {
        var size = input.GetLength(0);
        var output = new double[size, size];

        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                double sum = 0;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        sum += input[x, y] *
                               Math.Cos((2 * x + 1) * u * Math.PI / (2.0 * size)) *
                               Math.Cos((2 * y + 1) * v * Math.PI / (2.0 * size));
                    }
                }

                var cu = u == 0 ? 1 / Math.Sqrt(2) : 1;
                var cv = v == 0 ? 1 / Math.Sqrt(2) : 1;
                output[u, v] = 0.25 * cu * cv * sum;
            }
        }

        return output;
    }

    private static double ComputeMedian(double[,] values)
    {
        var flat = new List<double>();
        for (int x = 0; x < values.GetLength(0); x++)
        {
            for (int y = 0; y < values.GetLength(1); y++)
            {
                flat.Add(values[x, y]);
            }
        }
        flat.Sort();
        return flat[flat.Count / 2];
    }

    private static string ConvertBinaryToHex(string binary)
    {
        var hex = new StringBuilder();
        for (int i = 0; i < binary.Length; i += 4)
        {
            var chunk = binary.Substring(i, Math.Min(4, binary.Length - i)).PadRight(4, '0');
            var value = Convert.ToInt32(chunk, 2);
            hex.Append($"{value:X}");
        }
        return hex.ToString();
    }
}

/// <summary>
/// Helper for computing image histograms.
/// </summary>
public static class ImageHistogram
{
    /// <summary>
    /// Compute normalized histogram for a channel.
    /// </summary>
    /// <param name="values">Pixel values (0-255).</param>
    /// <param name="bins">Number of bins (default 16).</param>
    public static double[] Compute(IEnumerable<byte> values, int bins = 16)
    {
        var histogram = new int[bins];
        var total = 0;

        foreach (var value in values)
        {
            var bin = value * bins / 256;
            histogram[bin]++;
            total++;
        }

        return histogram.Select(h => total > 0 ? (double)h / total : 0).ToArray();
    }
}
