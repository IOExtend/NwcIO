namespace NwcIO.Model;

/// <summary>
/// Source-file metadata parsed from the partition-0 header stream:
/// the originating document, its timestamp, and the loader plugin that produced the cache.
/// </summary>
public sealed class NwcFileInfo
{
    /// <summary>Path of the source document the cache was exported from (e.g. an .rvt file).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Timestamp stored in the file (Unix seconds, interpreted as UTC).</summary>
    public DateTime? SourceTimestampUtc { get; set; }

    /// <summary>Size of the source document in bytes, if recorded in the header stream.</summary>
    public long? SourceByteSize { get; set; }

    /// <summary>Loader plugin identifier, e.g. "LcNwcLoaderPlugin:lcldrevit".</summary>
    public string? LoaderPlugin { get; set; }

    /// <summary>Export options recorded by the writer (structure under reverse engineering;
    /// provided as an ordered string list for now).</summary>
    public IReadOnlyList<string> Options { get; set; } = [];
}
