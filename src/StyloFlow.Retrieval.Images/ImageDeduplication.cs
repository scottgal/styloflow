namespace StyloFlow.Retrieval.Images;

/// <summary>
/// Image deduplication using perceptual hashing.
/// Finds duplicate and near-duplicate images in a collection.
/// </summary>
public sealed class ImageDeduplicator
{
    private readonly int _hammingThreshold;

    /// <summary>
    /// Creates an image deduplicator.
    /// </summary>
    /// <param name="hammingThreshold">Maximum Hamming distance to consider similar (0-64, lower = stricter).</param>
    public ImageDeduplicator(int hammingThreshold = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hammingThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hammingThreshold, 64);
        _hammingThreshold = hammingThreshold;
    }

    /// <summary>
    /// Find duplicate groups from a collection of image hashes.
    /// </summary>
    /// <typeparam name="T">Type of image identifier.</typeparam>
    /// <param name="items">Items with their perceptual hashes.</param>
    /// <returns>Groups of duplicate images.</returns>
    public List<DuplicateGroup<T>> FindDuplicates<T>(
        IEnumerable<(T Id, string Hash, long FileSize)> items) where T : notnull
    {
        var itemList = items.ToList();
        var groups = new List<DuplicateGroup<T>>();
        var processed = new HashSet<int>();

        for (int i = 0; i < itemList.Count; i++)
        {
            if (processed.Contains(i)) continue;

            var (id, hash, size) = itemList[i];
            var groupMembers = new List<DuplicateItem<T>>
            {
                new(id, hash, size, 0)
            };
            processed.Add(i);

            // Find all similar images
            for (int j = i + 1; j < itemList.Count; j++)
            {
                if (processed.Contains(j)) continue;

                var (otherId, otherHash, otherSize) = itemList[j];
                var distance = PerceptualHash.HammingDistance(hash, otherHash);

                if (distance <= _hammingThreshold)
                {
                    groupMembers.Add(new DuplicateItem<T>(otherId, otherHash, otherSize, distance));
                    processed.Add(j);
                }
            }

            if (groupMembers.Count > 1)
            {
                // Sort by file size (recommend keeping smallest)
                groupMembers = groupMembers.OrderBy(m => m.FileSize).ToList();
                groups.Add(new DuplicateGroup<T>(groupMembers, hash));
            }
        }

        return groups.OrderByDescending(g => g.Items.Count).ToList();
    }

    /// <summary>
    /// Find near-duplicates of a specific image.
    /// </summary>
    public List<DuplicateItem<T>> FindSimilar<T>(
        string queryHash,
        IEnumerable<(T Id, string Hash, long FileSize)> candidates) where T : notnull
    {
        return candidates
            .Select(c =>
            {
                var distance = PerceptualHash.HammingDistance(queryHash, c.Hash);
                return new DuplicateItem<T>(c.Id, c.Hash, c.FileSize, distance);
            })
            .Where(d => d.HammingDistance <= _hammingThreshold)
            .OrderBy(d => d.HammingDistance)
            .ToList();
    }

    /// <summary>
    /// Calculate statistics about potential space savings.
    /// </summary>
    public DeduplicationStats CalculateStats<T>(List<DuplicateGroup<T>> groups) where T : notnull
    {
        var totalDuplicates = groups.Sum(g => g.Items.Count - 1);
        var wastedSpace = groups.Sum(g =>
        {
            var sizes = g.Items.Select(i => i.FileSize).ToList();
            var keepSize = sizes.Min();
            return sizes.Sum() - keepSize;
        });

        return new DeduplicationStats(groups.Count, totalDuplicates, wastedSpace);
    }
}

/// <summary>
/// A group of duplicate or similar images.
/// </summary>
public record DuplicateGroup<T>(
    List<DuplicateItem<T>> Items,
    string RepresentativeHash) where T : notnull;

/// <summary>
/// Information about a duplicate image.
/// </summary>
public record DuplicateItem<T>(
    T Id,
    string Hash,
    long FileSize,
    int HammingDistance) where T : notnull;

/// <summary>
/// Statistics about deduplication results.
/// </summary>
public record DeduplicationStats(
    int GroupCount,
    int TotalDuplicates,
    long WastedSpace);
