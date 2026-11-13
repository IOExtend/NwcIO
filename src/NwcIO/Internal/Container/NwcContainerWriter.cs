using System.IO.Compression;
using System.Text;

namespace NwcIO.Internal.Container;

/// <summary>
/// Writes the LcUStream container of NWC/NWD files:
/// ASCII section header line followed by back-to-back zlib compressed streams.
/// </summary>
public static class NwcContainerWriter
{
    public const string DefaultHeader = "#LcUStream-V1.1 binary double release utf8 navisworks:448 lizlib\n";

    public static void Write(Stream output, IReadOnlyList<byte[]> decompressedStreams, string? header = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(decompressedStreams);

        ValidateHeader(header);
        header ??= DefaultHeader;
        if (!header.EndsWith('\n'))
        {
            header += '\n';
        }

        byte[] headerBytes = Encoding.ASCII.GetBytes(header);
        output.Write(headerBytes);

        for (int i = 0; i < decompressedStreams.Count; i++)
        {
            byte[] streamData = decompressedStreams[i];
            using var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
            zlib.Write(streamData, 0, streamData.Length);
        }

        output.Flush();
    }

    internal static void ValidateHeader(string? header)
    {
        if (header is not null && !header.StartsWith("#LcUStream", StringComparison.Ordinal))
        {
            throw new ArgumentException("Header must start with '#LcUStream'.", nameof(header));
        }
    }

    public static byte[] Write(IReadOnlyList<byte[]> decompressedStreams, string? header = null)
    {
        using var ms = new MemoryStream();
        Write(ms, decompressedStreams, header);
        return ms.ToArray();
    }
}
