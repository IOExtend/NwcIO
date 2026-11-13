using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>The model segments a DbCache directory locates (docs/format-notes.md §2.7); an absent segment is null or empty.</summary>
internal sealed record ModelSegments(
    NwcDbCacheDirectory Directory,
    LcRawStream? Hierarchy,
    LcRawStream? Fragments,
    IReadOnlyList<LcRawStream> GeometryChunks,
    LcRawStream? Toc);

/// <summary>The strictly read model: the tree root (null without a scene tree) and one mesh and transform per fragment.</summary>
internal sealed record ModelGraph(NwcNode? Root, IReadOnlyList<NwcMesh> Meshes, IReadOnlyList<NwcTransform> Transforms);

/// <summary>
/// Reads a Navisworks model file through its DbCache directory (docs/format-notes.md §2.7): the hierarchy, geometry and fragments
/// segments, linked as docs/format-notes.md §2.2 and §2.4 say. Every defect throws; nothing falls back to the lenient parsers.
/// </summary>
internal static class ModelGraphReader
{
    private const string HierarchySegment = "LcOpNwdLogicalHierarchy";
    private const string FragmentsSegment = "LcOpNwdFragments";
    private const string GeometrySegment = "LcOpNwdGeometry";

    /// <returns>The segments, or null when the container is not a model file (it then keeps the lenient path).</returns>
    /// <exception cref="InvalidDataException">The directory names a model segment more than once or in a wrong shape.</exception>
    public static ModelSegments? Locate(NwcContainer container)
    {
        var section1 = container.Streams.Where(s => s.SectionIndex == 1).Take(2).ToList();

        // Not a directory means not a model file, so the parse error is not needed.
        if (section1.Count != 1 || !DbCacheParser.TryParse(section1[0].Data, out var directory, out _))
        {
            return null;
        }

        NwcDbCacheEntry? hierarchy = null, fragments = null, geometry = null;
        foreach (var entry in directory.Entries)
        {
            switch (entry.Name[(entry.Name.LastIndexOf('\\') + 1)..])
            {
                case HierarchySegment:
                    hierarchy = Once(hierarchy, entry, HierarchySegment);
                    break;
                case FragmentsSegment:
                    fragments = Once(fragments, entry, FragmentsSegment);
                    break;
                case GeometrySegment:
                    geometry = Once(geometry, entry, GeometrySegment);
                    break;
            }
        }

        if (hierarchy is null && fragments is null && geometry is null)
        {
            return null;
        }

        var streams = container.Streams;
        var hierarchyStream = hierarchy is null ? null : SingleStream(streams, hierarchy, HierarchySegment);
        var fragmentsStream = fragments is null ? null : SingleStream(streams, fragments, FragmentsSegment);
        LcRawStream[] chunks = [];
        LcRawStream? toc = null;
        if (geometry is not null)
        {
            var segment = GeometryStreams(streams, geometry);
            chunks = segment[..^1];
            toc = segment[^1];
        }

        if (fragmentsStream is not null)
        {
            fragmentsStream.Kind = NwcStreamKind.Transforms;
        }

        foreach (var chunk in chunks)
        {
            chunk.Kind = NwcStreamKind.Geometry;
        }

        if (toc is not null)
        {
            toc.Kind = NwcStreamKind.Toc;
        }

        return new ModelSegments(directory, hierarchyStream, fragmentsStream, chunks, toc);
    }

    /// <summary>The tree first, so a tree that cannot be read fails before any geometry work; then geometry, fragments and links.</summary>
    /// <exception cref="InvalidDataException">A segment breaks its grammar or a fragment breaks a link rule.</exception>
    /// <exception cref="NotSupportedException">The tree holds class-32 nodes and <see cref="NwcReaderOptions.BuildSceneTree"/> is on.</exception>
    public static ModelGraph Read(ModelSegments segments, NwcReaderOptions options, string rootName)
    {
        IReadOnlyList<NwcNode>? preorder = null;
        if (options.BuildSceneTree)
        {
            preorder = segments.Hierarchy is null
                ? [new NwcNode { Id = 0, Name = rootName, Class = "Model", Tag = 0 }]
                : HierarchyParser.Read(segments.Hierarchy.Data, rootName, options.ReadPropertyRecords);
        }

        var meshes = new List<NwcMesh>();
        var transforms = new List<NwcTransform>();
        if ((options.ReadMeshes || options.ReadTransforms) && (segments.Toc is not null || segments.Fragments is not null))
        {
            if (segments.Toc is null)
            {
                throw new InvalidDataException($"{FragmentsSegment}: fragments without geometry");
            }

            var records = GeometrySegmentReader.Read(segments.GeometryChunks, segments.Toc);
            Fragment[] fragments = segments.Fragments is null ? [] : FragmentParser.Read(segments.Fragments.Data);
            CheckLinks(fragments, records, preorder);
            object? elementKey = null;
            for (int k = 0; k < fragments.Length; k++)
            {
                var fragment = fragments[k];
                NwcTransform? transform = null;
                if (options.ReadTransforms)
                {
                    transform = new NwcTransform
                    {
                        Id = k,
                        Translation = fragment.Translation,
                        Rotation = fragment.Rotation,
                        Scale = fragment.Scale,
                        BoundingBox = fragment.Bounds,
                    };
                    transforms.Add(transform);
                }

                if (options.ReadMeshes)
                {
                    // Meshes of one record share its arrays: it is decoded once.
                    var record = records[fragment.Record - 1];
                    var leaf = preorder?[fragment.Seq];
                    // Seq never decreases, so the fragments of one leaf are consecutive; a new object keeps keys apart across documents.
                    elementKey = k > 0 && fragment.Seq == fragments[k - 1].Seq ? elementKey : new object();
                    var mesh = new NwcMesh
                    {
                        Id = k,
                        Name = $"Mesh_{k}",
                        Vertices = record.Vertices,
                        Indices = record.Indices,
                        Normals = record.Normals,
                        LineIndices = record.LineIndices,
                        BoundingBox = record.Bounds,
                        Transform = transform,
                        Node = leaf,
                        ElementKey = elementKey,
                    };
                    meshes.Add(mesh);
                    if (leaf is not null)
                    {
                        leaf.Meshes.Add(mesh);
                        leaf.Mesh ??= mesh;
                    }
                }
            }
        }

        return new ModelGraph(preorder?[0], meshes, transforms);
    }

    private static NwcDbCacheEntry Once(NwcDbCacheEntry? found, NwcDbCacheEntry entry, string segment) =>
        found is null ? entry : throw Entry(segment, entry, "named more than once in the directory");

    // (a, b) = (0, 0): one zlib stream at exactly the entry's offset and length.
    private static LcRawStream SingleStream(IReadOnlyList<LcRawStream> streams, NwcDbCacheEntry entry, string segment)
    {
        CheckFlags(entry, segment);
        if (entry.Extra != 0)
        {
            throw Entry(segment, entry, "a and b are not 0");
        }

        int i = IndexAt(streams, entry.Offset);
        if (i < 0 || streams[i].CompressedLength != entry.Length)
        {
            throw Entry(segment, entry, "no stream spans exactly the entry's offset and length");
        }

        return streams[i];
    }

    // (4, b): a 4-byte prefix, then contiguous streams up to offset + length, the last of compressed length b.
    private static LcRawStream[] GeometryStreams(IReadOnlyList<LcRawStream> streams, NwcDbCacheEntry entry)
    {
        CheckFlags(entry, GeometrySegment);
        if ((int)entry.Extra != 4)
        {
            throw Entry(GeometrySegment, entry, "a is not 4");
        }

        int first = IndexAt(streams, entry.Offset + 4);
        if (first < 0)
        {
            throw Entry(GeometrySegment, entry, "no stream starts after the 4-byte prefix");
        }

        long end = entry.Offset + entry.Length;
        int last = first;
        while (EndOf(streams[last]) < end)
        {
            if (last + 1 == streams.Count || streams[last + 1].FileOffset != EndOf(streams[last]))
            {
                throw Entry(GeometrySegment, entry, "streams are not contiguous up to the entry's end");
            }

            last++;
        }

        if (EndOf(streams[last]) != end)
        {
            throw Entry(GeometrySegment, entry, "streams do not end at the entry's end");
        }

        if (streams[last].CompressedLength != (int)(entry.Extra >> 32))
        {
            throw Entry(GeometrySegment, entry, "b is not the last stream's compressed length");
        }

        return [.. streams.Skip(first).Take(last - first + 1)];
    }

    private static void CheckFlags(NwcDbCacheEntry entry, string segment)
    {
        if (entry.Flags != 1)
        {
            throw Entry(segment, entry, "flags are not 1");
        }
    }

    // The container lists streams in file order; chunk positions differ between .nwc and .nwd, so streams are found by offset.
    private static int IndexAt(IReadOnlyList<LcRawStream> streams, long offset)
    {
        for (int i = 0; i < streams.Count; i++)
        {
            if (streams[i].FileOffset == offset)
            {
                return i;
            }
        }

        return -1;
    }

    private static long EndOf(LcRawStream stream) => stream.FileOffset + stream.CompressedLength;

    // The links of docs/format-notes.md §2.2, §2.4 and §2.5, all checked before any mesh or transform is built.
    private static void CheckLinks(Fragment[] fragments, GeometryRecord[] records, IReadOnlyList<NwcNode>? preorder)
    {
        for (int k = 0; k < fragments.Length; k++)
        {
            var fragment = fragments[k];
            if (k > 0 && fragment.Seq < fragments[k - 1].Seq)
            {
                throw Link(k, "seq is less than the previous fragment's");
            }

            if (preorder is not null)
            {
                if ((uint)fragment.Seq >= (uint)preorder.Count)
                {
                    throw Link(k, "seq is outside the hierarchy preorder");
                }

                if (preorder[fragment.Seq].Tag != 22)
                {
                    throw Link(k, "seq does not name a class-22 leaf");
                }
            }

            if (fragment.Record < 1 || fragment.Record > records.Length)
            {
                throw Link(k, $"record {fragment.Record} is outside 1..{records.Length}");
            }

            var record = records[fragment.Record - 1];
            if (fragment.Bounds != record.Bounds)
            {
                throw Link(k, "bounding box differs from the record's");
            }

            if (fragment.PrimitiveCount != record.PrimitiveCount)
            {
                throw Link(k, "primitive count differs from the record's");
            }
        }
    }

    private static InvalidDataException Entry(string segment, NwcDbCacheEntry entry, string rule) =>
        new($"{segment} DbCache entry at file byte {entry.Offset}: {rule}");

    private static InvalidDataException Link(int fragment, string rule) => new($"{FragmentsSegment} fragment {fragment}: {rule}");
}
