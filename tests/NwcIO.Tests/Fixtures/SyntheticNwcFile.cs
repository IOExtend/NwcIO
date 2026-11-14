using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Tests;

/// <summary>Builds a two-section Navisworks model file whose DbCache directory locates its segments (docs/format-notes.md §2.7).</summary>
internal static class SyntheticNwcFile
{
    private const string Hierarchy = "LcOpNwdLogicalHierarchy";
    private const string Fragments = "LcOpNwdFragments";
    private const string Geometry = "LcOpNwdGeometry";

    /// <summary>
    /// Section 0: the default header line, then the geometry segment (first when <paramref name="geometryFirst"/>, else after the
    /// hierarchy and fragments), then <paramref name="extraStreams"/>. A null hierarchy or fragments, or no chunks and no record
    /// lengths, emits neither the segment nor its entry. Geometry = [i32 65536] + zlib(chunk)... + zlib(TOC), the TOC computed
    /// from the compressed sizes and padded to 8, <paramref name="editToc"/> applied before compression. Each extra stream is one
    /// zlib stream with a (0,0) entry named exactly its name. Section 1: the header line + zlib(directory); model entries are
    /// named <paramref name="namePrefix"/> + segment name, with absolute offsets, (0,0) for single streams and (4, b) for geometry.
    /// <paramref name="editDirectory"/> runs on the entries before the directory is written; <paramref name="section1"/>, when set,
    /// is written as section 1's one stream in place of the directory.
    /// </summary>
    public static byte[] Build(byte[]? hierarchy, byte[]? fragments, IReadOnlyList<byte[]> chunks, IReadOnlyList<int> recordLengths,
                               string namePrefix = @"cache\", bool geometryFirst = false, Action<byte[]>? editToc = null,
                               Action<List<NwcDbCacheEntry>>? editDirectory = null,
                               IReadOnlyList<(string Name, byte[] Data)>? extraStreams = null, byte[]? section1 = null)
    {
        var header = Encoding.ASCII.GetBytes(NwcContainerWriter.DefaultHeader);
        var file = new List<byte>(header);
        var entries = new List<NwcDbCacheEntry>();

        void Single(string name, byte[] data)
        {
            long offset = file.Count;
            var zlib = Zlib(data);
            file.AddRange(zlib);
            entries.Add(new NwcDbCacheEntry(name, 1, offset, zlib.Length, 0));
        }

        void GeometrySegment()
        {
            if (chunks.Count == 0 && recordLengths.Count == 0)
            {
                return;
            }

            long offset = file.Count;
            file.AddRange(BitConverter.GetBytes(65536));
            var toc = new byte[(8 + (4 * chunks.Count) + (4 * recordLengths.Count) + 7) / 8 * 8];
            BinaryPrimitives.WriteInt32LittleEndian(toc, recordLengths.Count);
            BinaryPrimitives.WriteInt32LittleEndian(toc.AsSpan(4), chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                var zlib = Zlib(chunks[i]);
                file.AddRange(zlib);
                BinaryPrimitives.WriteInt32LittleEndian(toc.AsSpan(8 + (4 * i)), zlib.Length);
            }

            for (int k = 0; k < recordLengths.Count; k++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(toc.AsSpan(8 + (4 * chunks.Count) + (4 * k)), recordLengths[k]);
            }

            editToc?.Invoke(toc);
            var tocZlib = Zlib(toc);
            file.AddRange(tocZlib);
            entries.Add(new NwcDbCacheEntry(namePrefix + Geometry, 1, offset, file.Count - offset, 4 + ((long)tocZlib.Length << 32)));
        }

        if (geometryFirst)
        {
            GeometrySegment();
        }

        if (hierarchy is not null)
        {
            Single(namePrefix + Hierarchy, hierarchy);
        }

        if (fragments is not null)
        {
            Single(namePrefix + Fragments, fragments);
        }

        if (!geometryFirst)
        {
            GeometrySegment();
        }

        foreach (var (name, data) in extraStreams ?? [])
        {
            Single(name, data);
        }

        editDirectory?.Invoke(entries);

        // [i32 -1][i32 n], then per entry [str name][i32 flags] PAD8 [i64 offset][i64 length][i32 a][i32 b] (docs/format-notes.md §2.7).
        var directory = new LcGraphWriter().Int(-1).Int(entries.Count);
        foreach (var entry in entries)
        {
            directory.Str(entry.Name).Int(entry.Flags).Long(entry.Offset).Long(entry.Length).Int((int)entry.Extra).Int((int)(entry.Extra >> 32));
        }

        file.AddRange(header);
        file.AddRange(Zlib(section1 ?? directory.ToArray()));
        return [.. file];
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>The partition-0 header stream with its zero pads (docs/format-notes.md �2.1).</summary>
    public static byte[] PartitionHeader(string sourcePath, long unixSeconds, string loaderPlugin) =>
        new LcGraphWriter().Long(7).Str(sourcePath).Int(-1).Long(unixSeconds).Long(0).Str(loaderPlugin).ToArray();
}
