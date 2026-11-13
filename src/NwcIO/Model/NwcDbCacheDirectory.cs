namespace NwcIO.Model;

/// <summary>
/// Represents a named segment or table entry recorded in the Navisworks database cache stream.
/// </summary>
/// <param name="Name">Segment name (e.g., "cache\LcOpNwdGeometry", "cache\LcOpGuidStore").</param>
/// <param name="Flags">Flags or schema version associated with the segment.</param>
/// <param name="Offset">Absolute file offset of the segment data.</param>
/// <param name="Length">Byte length of the segment data.</param>
/// <param name="Extra">The two trailing 32-bit fields as <c>a + b * 2^32</c>: <c>(0, 0)</c> means one zlib stream, <c>(4, b)</c> a 4-byte prefix plus streams whose last compressed length is <c>b</c>.</param>
public sealed record NwcDbCacheEntry(
    string Name,
    int Flags,
    long Offset,
    long Length,
    long Extra);

/// <summary>
/// Master directory of cached data tables registered in the <c>LcOpDBCache</c> stream.
/// </summary>
/// <param name="DatabaseName">Name of the <c>LcOpDBCache</c> entry, or an empty string when the directory has none.</param>
/// <param name="Entries">Every directory entry in file order.</param>
public sealed record NwcDbCacheDirectory(
    string DatabaseName,
    IReadOnlyList<NwcDbCacheEntry> Entries)
{
    /// <summary>
    /// Finds a cache entry by its segment name (case-insensitive).
    /// </summary>
    public NwcDbCacheEntry? Find(string segmentName)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (string.Equals(Entries[i].Name, segmentName, StringComparison.OrdinalIgnoreCase) ||
                Entries[i].Name.EndsWith(segmentName, StringComparison.OrdinalIgnoreCase))
            {
                return Entries[i];
            }
        }

        return null;
    }
}
