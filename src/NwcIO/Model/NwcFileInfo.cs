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

    /// <summary>Internal format version declared in the section header (e.g. 448 for Navisworks 2016–2025); null when undeclared.</summary>
    public int? FormatVersion { get; set; }

    /// <summary>Text character encoding declared by the section header ("C" or "utf8"); null when undeclared.</summary>
    public string? Encoding { get; set; }

    /// <summary>Chunk layout marker declared by the section header ("lichunk-008", "lizlib"); null when undeclared.</summary>
    public string? ChunkMarker { get; set; }

    /// <summary>
    /// Length unit of coordinates and raw length values. Navisworks does not store the unit in a stream we decode;
    /// the reader infers <see cref="NwcLengthUnit.Feet"/> for the Revit loader/exporter and leaves it
    /// <see cref="NwcLengthUnit.Unknown"/> otherwise. Override with <see cref="NwcReaderOptions.LengthUnit"/>.
    /// </summary>
    public NwcLengthUnit LengthUnit { get; set; } = NwcLengthUnit.Unknown;
}
