using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// The model segments a DbCache directory locates (docs/format-notes.md §2.7); an absent segment is null or empty. PartitionProps
/// locates and checks the <c>LcOaPartitionProps</c> segment only when called, and returns null when the directory names none.
/// </summary>
internal sealed record ModelSegments(
    NwcDbCacheDirectory Directory,
    LcRawStream? Hierarchy,
    LcRawStream? Fragments,
    IReadOnlyList<LcRawStream> GeometryChunks,
    LcRawStream? Toc,
    LcRawStream? GuidStore,
    Func<IReadOnlyList<byte[]>?> PartitionProps);

/// <summary>
/// The strictly read model: the tree root (null without a scene tree), one mesh and transform per fragment, the GUID table (empty when
/// not read), and the materials of the meshes in order of first use (empty without meshes, the tree or <c>ReadMaterials</c>).
/// </summary>
internal sealed record ModelGraph(
    NwcNode? Root,
    IReadOnlyList<NwcMesh> Meshes,
    IReadOnlyList<NwcTransform> Transforms,
    IReadOnlyList<Guid> Guids,
    IReadOnlyList<NwcMaterial> Materials);

/// <summary>
/// Reads a Navisworks model file through its DbCache directory (docs/format-notes.md §2.7): the hierarchy, geometry and fragments
/// segments, linked as docs/format-notes.md §2.2 and §2.4 say. Every defect throws; nothing falls back to the lenient parsers.
/// </summary>
internal static class ModelGraphReader
{
    private const string HierarchySegment = "LcOpNwdLogicalHierarchy";
    private const string FragmentsSegment = "LcOpNwdFragments";
    private const string GeometrySegment = "LcOpNwdGeometry";
    private const string GuidStoreSegment = "LcOpGuidStore";
    private const string PartitionPropsSegment = "LcOaPartitionProps";

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

        NwcDbCacheEntry? hierarchy = null, fragments = null, geometry = null, guidStore = null;
        var props = new List<NwcDbCacheEntry>();
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
                case GuidStoreSegment:
                    guidStore = Once(guidStore, entry, GuidStoreSegment);
                    break;
                case PartitionPropsSegment:
                    // Only recorded: the tree reader checks them, so a .nwd fails at its first class-32 node first (docs/format-notes.md §2.2.1).
                    props.Add(entry);
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
        var guidStoreStream = guidStore is null ? null : SingleStream(streams, guidStore, GuidStoreSegment);
        LcRawStream[] chunks = [];
        LcRawStream? toc = null;
        if (geometry is not null)
        {
            var segment = PrefixedStreams(streams, geometry, GeometrySegment);
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

        return new ModelSegments(directory, hierarchyStream, fragmentsStream, chunks, toc, guidStoreStream, () => PartitionRootStreams(streams, props));
    }

    /// <summary>The tree first, so a tree that cannot be read fails before any geometry work; then geometry, fragments and links.</summary>
    /// <exception cref="InvalidDataException">A segment breaks its grammar or a fragment breaks a link rule.</exception>
    /// <exception cref="NotSupportedException">The model's scene tree holds partition (class-32) nodes or places one Revit element under two parents (a linked model); open it with <see cref="NwcReaderOptions.BuildSceneTree"/> off.</exception>
    public static ModelGraph Read(ModelSegments segments, NwcReaderOptions options, string rootName)
    {
        // The tree needs the table for element GUIDs whatever ReadGuids says.
        Guid[] guids = segments.GuidStore is not null && (options.ReadGuids || (options.BuildSceneTree && segments.Hierarchy is not null))
            ? ReadGuidStore(segments.GuidStore.Data)
            : [];
        IReadOnlyList<NwcNode>? preorder = null;
        if (options.BuildSceneTree)
        {
            preorder = segments.Hierarchy is null
                ? [new NwcNode { Id = 0, Name = rootName, Class = "Model", Tag = 0 }]
                : HierarchyParser.Read(segments.Hierarchy.Data, rootName, options.ReadPropertyRecords, guids, segments.PartitionProps);
        }

        var meshes = new List<NwcMesh>();
        var transforms = new List<NwcTransform>();
        var materials = new List<NwcMaterial>();
        var materialsByKey = new Dictionary<(string, byte, byte, byte, byte), NwcMaterial>();
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
                        LineColors = record.LineColors,
                        GeometryIndex = fragment.Record - 1,
                        BoundingBox = record.Bounds,
                        Transform = transform,
                        Node = leaf,
                        ElementKey = elementKey,

                        // Names come from the tree, so there are no materials without it.
                        Material = leaf is not null && options.ReadMaterials ? MaterialOf(leaf.MaterialName, fragment.Look, materialsByKey, materials) : null,
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

        return new ModelGraph(preorder?[0], meshes, transforms, guids, materials);
    }

    // One material per distinct (name, RGBA bytes), its id the order of first use (docs/format-notes.md §2.9). Shininess has no defined
    // PBR equivalent, so Roughness and Metallic keep their defaults.
    private static NwcMaterial MaterialOf(
        string? name, FragmentLook look, Dictionary<(string, byte, byte, byte, byte), NwcMaterial> byKey, List<NwcMaterial> materials)
    {
        (string Name, byte R, byte G, byte B, byte A) key =
            (string.IsNullOrEmpty(name) ? "Default" : name, Byte(look.R), Byte(look.G), Byte(look.B), Byte(1.0 - look.Transparency));
        if (!byKey.TryGetValue(key, out var material))
        {
            material = new NwcMaterial { Id = materials.Count, Name = key.Name, Color = new RgbaColor(key.R / 255f, key.G / 255f, key.B / 255f, key.A / 255f) };
            byKey.Add(key, material);
            materials.Add(material);
        }

        return material;
    }

    // The float is widened before scaling, and a half rounds away from zero.
    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);

    // [i32 a = 1][i32 n][n x 16-byte GUID] in Windows layout (docs/format-notes.md §2.3).
    private static Guid[] ReadGuidStore(byte[] data)
    {
        if (data.Length < 8)
        {
            throw new InvalidDataException($"{GuidStoreSegment}: shorter than its 8-byte head");
        }

        int a = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (a != 1)
        {
            throw new InvalidDataException($"{GuidStoreSegment}: head a is {a}, not 1");
        }

        int n = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        if (n < 0 || data.Length != 8 + (16L * n))
        {
            throw new InvalidDataException($"{GuidStoreSegment}: {n} GUIDs do not fill {data.Length} bytes");
        }

        var guids = new Guid[n];
        for (int i = 0; i < n; i++)
        {
            guids[i] = new Guid(data.AsSpan(8 + (16 * i), 16));
        }

        return guids;
    }

    // The LcOaPartitionProps root streams, once the last stream, the table of contents [i32 k][k x i32 compressed length] zero-padded
    // to 8, matches them (docs/format-notes.md §2.2.1); null when the directory names no such segment, empty for a TOC alone (k = 0).
    private static byte[][]? PartitionRootStreams(IReadOnlyList<LcRawStream> streams, List<NwcDbCacheEntry> entries)
    {
        NwcDbCacheEntry? entry = null;
        foreach (var named in entries)
        {
            entry = Once(entry, named, PartitionPropsSegment);
        }

        if (entry is null)
        {
            return null;
        }

        var segment = PrefixedStreams(streams, entry, PartitionPropsSegment);
        byte[] toc = segment[^1].Data;
        int k = segment.Length - 1;
        int expected = (4 + (4 * k) + 7) & ~7;
        if (toc.Length != expected)
        {
            throw new InvalidDataException($"{PartitionPropsSegment}: the table of contents is {toc.Length} bytes, not {expected}");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(toc);
        if (count != k)
        {
            throw new InvalidDataException($"{PartitionPropsSegment}: the table of contents names {count} streams, not {k}");
        }

        for (int i = 0; i < k; i++)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(toc.AsSpan(4 + (4 * i)));
            if (length != segment[i].CompressedLength)
            {
                throw new InvalidDataException($"{PartitionPropsSegment}: the table of contents gives stream {i} {length} bytes, not {segment[i].CompressedLength}");
            }
        }

        if (toc.AsSpan(4 + (4 * k)).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidDataException($"{PartitionPropsSegment}: the table of contents pad is not zero");
        }

        return [.. segment.Take(k).Select(s => s.Data)];
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

    // (4, b): a 4-byte prefix, then contiguous streams up to offset + length, the last of compressed length b. The prefix lies outside
    // every stream, which the container does not keep, so it is not checked.
    private static LcRawStream[] PrefixedStreams(IReadOnlyList<LcRawStream> streams, NwcDbCacheEntry entry, string segment)
    {
        CheckFlags(entry, segment);
        if ((int)entry.Extra != 4)
        {
            throw Entry(segment, entry, "a is not 4");
        }

        int first = IndexAt(streams, entry.Offset + 4);
        if (first < 0)
        {
            throw Entry(segment, entry, "no stream starts after the 4-byte prefix");
        }

        long end = entry.Offset + entry.Length;
        int last = first;
        while (EndOf(streams[last]) < end)
        {
            if (last + 1 == streams.Count || streams[last + 1].FileOffset != EndOf(streams[last]))
            {
                throw Entry(segment, entry, "streams are not contiguous up to the entry's end");
            }

            last++;
        }

        if (EndOf(streams[last]) != end)
        {
            throw Entry(segment, entry, "streams do not end at the entry's end");
        }

        if (streams[last].CompressedLength != (int)(entry.Extra >> 32))
        {
            throw Entry(segment, entry, "b is not the last stream's compressed length");
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
