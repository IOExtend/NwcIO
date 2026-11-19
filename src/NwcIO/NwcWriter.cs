using NwcIO.Internal;
using NwcIO.Internal.Container;

namespace NwcIO;

/// <summary>
/// Provides methods for serializing and saving <see cref="NwcDocument"/> instances into
/// .nwc files in NwcIO's own layout (not verified to open in Autodesk Navisworks) using pure managed compression and binary layouts.
/// Stores vertex positions, triangle indices, the mesh id and name, and rotation/translation placement.
/// Meshes whose triangles differ from the single strip the reader rebuilds are written with the writer's
/// index trailer (docs/format-notes.md §2.6), which the reader prefers over the strip.
/// Scaled transforms and line meshes are rejected before anything is written. Normals are not stored.
/// </summary>
public static class NwcWriter
{
    /// <summary>
    /// Writes an interoperable 448 cache in feet from an owned triangle model with known source units.
    /// Bake mesh rotation and scale into the vertices before calling. Node properties are written as
    /// display text. The stream must be writable and positioned at the start of a new file.
    /// </summary>
    public static void WriteNative(NwcDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite || (stream.CanSeek && (stream.Position != 0 || stream.Length != 0)))
            throw new ArgumentException("A native NWC output stream must be writable and empty.", nameof(stream));

        var segments = NativeCacheModelWriter.Serialize(document);
        NativeCacheContainerWriter.Write(stream, segments);
    }

    /// <summary>
    /// Writes the document to the specified file path, replacing the destination after the complete file is written.
    /// </summary>
    public static void Write(NwcDocument document, string path, string? header = null)
        => Write(document, path, header, default);

    internal static void Write(NwcDocument document, string path, string? header, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        cancellationToken.ThrowIfCancellationRequested();
        NwcContainerWriter.ValidateHeader(header);
        var streams = NwcStreamSerializer.SerializeDocument(document);
        AtomicFileWriter.Write(path, output => NwcContainerWriter.Write(output, streams, header, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Writes the document to the specified output stream.
    /// </summary>
    public static void Write(NwcDocument document, Stream stream, string? header = null)
        => Write(document, stream, header, default);

    internal static void Write(NwcDocument document, Stream stream, string? header, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        NwcContainerWriter.ValidateHeader(header);
        var streams = NwcStreamSerializer.SerializeDocument(document);
        NwcContainerWriter.Write(stream, streams, header, cancellationToken);
    }

    /// <summary>
    /// Serializes the document into a byte array containing a complete .nwc file.
    /// </summary>
    public static byte[] WriteToBytes(NwcDocument document, string? header = null)
    {
        using var ms = new MemoryStream();
        Write(document, ms, header);
        return ms.ToArray();
    }
}
