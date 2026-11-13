using NwcIO.Internal.Container;

namespace NwcIO;

/// <summary>
/// Provides methods for serializing and saving <see cref="NwcDocument"/> instances into
/// Autodesk Navisworks (.nwc) files using pure managed compression and binary layouts.
/// Stores vertex positions and rotation/translation placement. Triangle indices are not stored: the reader
/// rebuilds one strip over the positions, so meshes whose triangles differ from that strip are rejected, as are
/// scaled transforms and line meshes, before anything is written. Normals are not stored.
/// </summary>
public static class NwcWriter
{
    /// <summary>
    /// Writes the document to the specified file path.
    /// </summary>
    public static void Write(NwcDocument document, string path, string? header = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Serialize first, so that a document which can't be written doesn't truncate or create the file
        NwcContainerWriter.ValidateHeader(header);
        var streams = NwcStreamSerializer.SerializeDocument(document);
        using var fs = File.Create(path);
        NwcContainerWriter.Write(fs, streams, header);
    }

    /// <summary>
    /// Writes the document to the specified output stream.
    /// </summary>
    public static void Write(NwcDocument document, Stream stream, string? header = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);

        NwcContainerWriter.ValidateHeader(header);
        var streams = NwcStreamSerializer.SerializeDocument(document);
        NwcContainerWriter.Write(stream, streams, header);
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
