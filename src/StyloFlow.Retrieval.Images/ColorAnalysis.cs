namespace StyloFlow.Retrieval.Images;

/// <summary>
/// Color analysis utilities for image retrieval and classification.
/// </summary>
public static class ColorAnalysis
{
    /// <summary>
    /// Extract dominant colors from RGB pixel data using k-means clustering.
    /// </summary>
    /// <param name="pixels">List of (R, G, B) tuples.</param>
    /// <param name="k">Number of dominant colors to extract.</param>
    /// <param name="maxIterations">Maximum clustering iterations.</param>
    /// <returns>List of dominant colors with their percentages.</returns>
    public static List<DominantColor> ExtractDominantColors(
        IReadOnlyList<(byte R, byte G, byte B)> pixels,
        int k = 5,
        int maxIterations = 20)
    {
        if (pixels.Count == 0) return new List<DominantColor>();

        // Sample pixels if too many (for performance)
        var sample = pixels.Count > 10000
            ? SamplePixels(pixels, 10000)
            : pixels.ToList();

        // Initialize centroids randomly
        var rng = new Random(42);
        var centroids = sample
            .OrderBy(_ => rng.Next())
            .Take(k)
            .Select(p => (R: (double)p.R, G: (double)p.G, B: (double)p.B))
            .ToList();

        // K-means clustering
        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Assign pixels to nearest centroid
            var clusters = Enumerable.Range(0, k).Select(_ => new List<(byte R, byte G, byte B)>()).ToList();

            foreach (var pixel in sample)
            {
                var nearestIdx = 0;
                var nearestDist = double.MaxValue;

                for (int i = 0; i < centroids.Count; i++)
                {
                    var dist = ColorDistance(pixel, centroids[i]);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestIdx = i;
                    }
                }

                clusters[nearestIdx].Add(pixel);
            }

            // Update centroids
            var newCentroids = new List<(double R, double G, double B)>();
            for (int i = 0; i < k; i++)
            {
                if (clusters[i].Count == 0)
                {
                    newCentroids.Add(centroids[i]);
                }
                else
                {
                    newCentroids.Add((
                        R: clusters[i].Average(p => p.R),
                        G: clusters[i].Average(p => p.G),
                        B: clusters[i].Average(p => p.B)
                    ));
                }
            }

            centroids = newCentroids;
        }

        // Calculate percentages
        var clusterCounts = new int[k];
        foreach (var pixel in sample)
        {
            var nearestIdx = 0;
            var nearestDist = double.MaxValue;

            for (int i = 0; i < centroids.Count; i++)
            {
                var dist = ColorDistance(pixel, centroids[i]);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestIdx = i;
                }
            }

            clusterCounts[nearestIdx]++;
        }

        return centroids
            .Select((c, i) => new DominantColor(
                (byte)c.R, (byte)c.G, (byte)c.B,
                (double)clusterCounts[i] / sample.Count))
            .OrderByDescending(c => c.Percentage)
            .ToList();
    }

    /// <summary>
    /// Calculate color temperature (warm/cool) from dominant colors.
    /// </summary>
    /// <param name="dominantColors">List of dominant colors.</param>
    /// <returns>Temperature score (-1 = cool, 0 = neutral, 1 = warm).</returns>
    public static double CalculateColorTemperature(IEnumerable<DominantColor> dominantColors)
    {
        var score = 0.0;
        var totalWeight = 0.0;

        foreach (var color in dominantColors)
        {
            // Warm colors: high R, low B
            // Cool colors: high B, low R
            var warmth = (color.R - color.B) / 255.0;
            score += warmth * color.Percentage;
            totalWeight += color.Percentage;
        }

        return totalWeight > 0 ? score / totalWeight : 0;
    }

    /// <summary>
    /// Calculate color diversity/saturation.
    /// </summary>
    public static double CalculateColorDiversity(
        IReadOnlyList<(byte R, byte G, byte B)> pixels)
    {
        if (pixels.Count == 0) return 0;

        var uniqueColors = new HashSet<uint>();
        var sampleStep = Math.Max(1, pixels.Count / 1000);

        for (int i = 0; i < pixels.Count; i += sampleStep)
        {
            var p = pixels[i];
            var key = ((uint)p.R << 16) | ((uint)p.G << 8) | p.B;
            uniqueColors.Add(key);
        }

        var maxColors = Math.Min(1000, pixels.Count / sampleStep);
        return (double)uniqueColors.Count / maxColors;
    }

    /// <summary>
    /// Convert RGB to HSL.
    /// </summary>
    public static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
    {
        var rf = r / 255.0;
        var gf = g / 255.0;
        var bf = b / 255.0;

        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var l = (max + min) / 2;

        if (max == min)
            return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

        double h;
        if (max == rf)
            h = ((gf - bf) / d + (gf < bf ? 6 : 0)) / 6;
        else if (max == gf)
            h = ((bf - rf) / d + 2) / 6;
        else
            h = ((rf - gf) / d + 4) / 6;

        return (h * 360, s, l);
    }

    private static double ColorDistance((byte R, byte G, byte B) p, (double R, double G, double B) c)
    {
        var dr = p.R - c.R;
        var dg = p.G - c.G;
        var db = p.B - c.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static List<(byte R, byte G, byte B)> SamplePixels(
        IReadOnlyList<(byte R, byte G, byte B)> pixels, int sampleSize)
    {
        var rng = new Random(42);
        return pixels.OrderBy(_ => rng.Next()).Take(sampleSize).ToList();
    }
}

/// <summary>
/// Represents a dominant color in an image.
/// </summary>
public record DominantColor(byte R, byte G, byte B, double Percentage)
{
    /// <summary>
    /// Get hex color string.
    /// </summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Get HSL representation.
    /// </summary>
    public (double H, double S, double L) ToHsl() => ColorAnalysis.RgbToHsl(R, G, B);
}
