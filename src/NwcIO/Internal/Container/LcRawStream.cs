namespace NwcIO.Internal.Container;

/// <summary>How a raw stream was identified.</summary>
public enum NwcStreamKind
{
    Unknown,
    /// <summary>Partition 0 header: source file path, timestamp, loader plugin, export options.</summary>
    PartitionHeader,
    /// <summary>Table of contents: [int32 x][int32 count][count x int32 compressed sizes][...].</summary>
    Toc,
    /// <summary>Scene tree / property records (starts with 64 00 00 00 56 00 00 00).</summary>
    SceneTree,
    /// <summary>GUID table: [int32 ?][int32 count][count x 16B GUIDs].</summary>
    Guids,
    /// <summary>Transform records (4x4 matrices).</summary>
    Transforms,
    /// <summary>3D mesh geometry and vertex buffers (tags 94, 60).</summary>
    Geometry,
    /// <summary>XRefTable JSON fragment.</summary>
    XRefJson,
    /// <summary>LcOpDBCache products footer.</summary>
    DbCache,
    /// <summary>Saved viewpoints and cameras (tag 517 with position and quaternion records).</summary>
    SavedViews,
    /// <summary>Selection sets, search sets, and set folders.</summary>
    SelectionSets,
    /// <summary>4D TimeLiner construction schedule and tasks.</summary>
    TimeLiner,
    /// <summary>Writer-side per-mesh transform ids ("NWTL").</summary>
    TransformLinks,
}

/// <summary>
/// One zlib-decompressed stream ("chunk") of the container plus its raw placement in the file.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Matches Navisworks LcUStream naming convention")]
public sealed record LcRawStream
{
    public required int Index { get; init; }

    /// <summary>Absolute file offset where the zlib data starts.</summary>
    public required long FileOffset { get; init; }

    /// <summary>Compressed (zlib) byte length in the file.</summary>
    public required long CompressedLength { get; init; }

    /// <summary>Bytes skipped in the file right before this stream (framing/section text).</summary>
    public required int GapBefore { get; init; }

    public required byte[] Data { get; init; }

    public NwcStreamKind Kind { get; set; } = NwcStreamKind.Unknown;

    /// <summary>Index of the LcUStream section this stream belongs to.</summary>
    public required int SectionIndex { get; init; }
}

/// <summary>The parsed container: section headers and the ordered stream list.</summary>
public sealed record NwcContainer
{
    public required IReadOnlyList<LcUStreamHeader> Sections { get; init; }

    public required IReadOnlyList<LcRawStream> Streams { get; init; }

    public required long FileLength { get; init; }

    /// <summary>Bytes at the file tail that could not be attributed to any stream.</summary>
    public required long TrailingGap { get; init; }
}
