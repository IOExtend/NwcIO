using System.IO.Compression;
using NwcIO.Model;

namespace NwcIO.Internal.Container;

internal readonly record struct NativeCacheSegment(string Name, byte[] Data, int Flags = 1, int PrefixBytes = 0, int TocBytes = 0);

internal static class NativeCacheContainerWriter
{
    private static ReadOnlySpan<byte> ModelHeader => "#LcUStream-V1.1 binary double release C lichunk-008\n"u8;
    private static ReadOnlySpan<byte> DirectoryHeader => "#LcUStream-V1.1 binary double release utf8 navisworks:448 lizlib\n\0\0\0\0\0\0\0"u8;

    public static void Write(Stream output, IReadOnlyList<NativeCacheSegment> segments)
    {
        using var directory = new NativeCacheBinaryWriter();
        directory.Write(-1);
        directory.Write(segments.Count);
        var entries = new List<NwcDbCacheEntry>(segments.Count);
        long offset = ModelHeader.Length;
        output.Write(ModelHeader);
        foreach (var segment in segments)
        {
            long length = segment.Data.LongLength;
            long extra = segment.PrefixBytes + ((long)segment.TocBytes << 32);
            entries.Add(new(segment.Name, segment.Flags, offset, length, extra));
            directory.WriteText(segment.Name);
            directory.Write(segment.Flags);
            directory.Align(8);
            directory.Write(offset);
            directory.Write(length);
            directory.Write(extra);
            output.Write(segment.Data);
            offset = checked(offset + length);
        }

        output.Write(DirectoryHeader);
        output.Write(Compress(directory.Finish()));
        Span<byte> trailer = stackalloc byte[16];
        NativeCacheChecksum.Write(entries, trailer);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(trailer[8..], offset);
        output.Write(trailer);
        output.Write(ModelHeader);
    }

    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return [0x78, 0x9c, 0x03, 0, 0, 0, 0, 1]; // ZLibStream omits streams with no input.

        using var bytes = new MemoryStream();
        using (var zlib = new ZLibStream(bytes, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return bytes.ToArray();
    }
}
