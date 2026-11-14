using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class ModelGraphReaderTests
{
    [Fact]
    public void Open_F1File_LinksFragmentsToLeavesAndPoses()
    {
        var doc = NwcDocument.Open(new MemoryStream(SyntheticNwcFile.Build(FormatVectors.H1, FormatVectors.F1, [FormatVectors.R1], [120])));
        Assert.Equal(2, doc.Meshes.Count);
        Assert.Equal(2, doc.Transforms.Count);
        for (int k = 0; k < 2; k++) { Assert.Equal(k, doc.Meshes[k].Id); Assert.Same(doc.Transforms[k], doc.Meshes[k].Transform); }
        Assert.Equal([2, 7], doc.Meshes.Select(m => m.Node!.Id));
        Assert.All(doc.Meshes, m => { Assert.Equal(22, m.Node!.Tag); Assert.Contains(m, m.Node.Meshes); Assert.Same(m, m.Node.Mesh); });
        Assert.Same(doc.Meshes[0].Vertices, doc.Meshes[1].Vertices);           // shared record
        Assert.Same(doc.Meshes[0].Indices, doc.Meshes[1].Indices);
        Assert.Equal([new Vector3d(10, 0, 0), new(11, 0, 0), new(10, 1, 0), new(11, 1, 0)], doc.Meshes[0].GetWorldVertices());
        Assert.Equal([new Vector3d(0, 0, 0), new(-1, 0, 0), new(0, -1, 0), new(-1, -1, 0)], doc.Meshes[1].GetWorldVertices());
        Assert.Equal([0, 2, 1, 2, 3, 1], doc.Meshes[1].GetWorldIndices());
        Assert.Equal([0, 1, 2, 2, 1, 3], doc.Meshes[1].Indices);
        Assert.Equal(-1.0, doc.Transforms[1].Scale);
        Assert.Equal(new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)), doc.Transforms[0].BoundingBox);
        Assert.Equal(Enumerable.Range(0, 8), Preorder(doc.RootNode!).Select(n => n.Id));
        Assert.All(Preorder(doc.RootNode!), n => { Assert.Null(n.Transform); Assert.Null(n.ElementGuid); });
        Assert.Equal(3, doc.DbCache!.Entries.Count);
    }

    [Fact]
    public void Open_MixedFile_DecodesLinesNormalsAndCrossingRecords()
    {
        var doc = NwcDocument.Open(new MemoryStream(MixedFile()));
        Assert.Equal(5, doc.Meshes.Count);
        Assert.Equal([2, 3, 6, 7, 7], doc.Meshes.Select(m => m.Node!.Id));
        var leaf = doc.Meshes[3].Node!;
        Assert.Equal([doc.Meshes[3], doc.Meshes[4]], leaf.Meshes);
        Assert.Same(doc.Meshes[3], leaf.Mesh);
        Assert.Equal([0, 1, 2, 2, 1, 3, 4, 5, 6], doc.Meshes[1].Indices);
        Assert.Equal(new Vector3d(0, 0, -1), doc.Meshes[1].Normals![0]);
        Assert.Empty(doc.Meshes[2].Indices);
        Assert.Equal([0, 1, 1, 2, 2, 3, 4, 5], doc.Meshes[2].LineIndices);
        Assert.Null(doc.Meshes[2].Normals);
        Assert.Equal(-1.0, doc.Transforms[3].Scale);
        Assert.All(doc.Meshes.Skip(3), m => { Assert.Same(doc.Meshes[0].Vertices, m.Vertices); Assert.Same(doc.Meshes[0].Indices, m.Indices); });
        Assert.Equal(new Vector3d(0, 5, 0), doc.Meshes[1].GetWorldVertices()[0]);

        // streams: hierarchy, fragments, chunk 0, chunk 1, TOC, directory
        Assert.Equal(
            [NwcStreamKind.Transforms, NwcStreamKind.Geometry, NwcStreamKind.Geometry, NwcStreamKind.Toc],
            doc.Container.Streams.Skip(1).Take(4).Select(s => s.Kind));
    }

    [Theory]
    [InlineData(false, true, true)]   // ReadMeshes off: Meshes empty, Transforms 5
    [InlineData(true, false, true)]   // ReadTransforms off: Transforms empty, Meshes 5 with Transform null, Node set
    [InlineData(true, true, false)]   // BuildSceneTree off: RootNode null, every Node null
    [InlineData(false, false, true)]  // neither: no meshes, no transforms, tree only
    public void Open_MixedFileWithOptions_ReadsOnlyRequestedParts(bool meshes, bool transforms, bool tree)
    {
        var doc = NwcDocument.Open(
            new MemoryStream(MixedFile()),
            new NwcReaderOptions { ReadMeshes = meshes, ReadTransforms = transforms, BuildSceneTree = tree });
        Assert.Equal(meshes ? 5 : 0, doc.Meshes.Count);
        Assert.Equal(transforms ? 5 : 0, doc.Transforms.Count);
        Assert.Equal(tree, doc.RootNode is not null);
        Assert.All(doc.Meshes, m => { Assert.Equal(transforms, m.Transform is not null); Assert.Equal(tree, m.Node is not null); });
        if (tree)
        {
            Assert.Equal(Enumerable.Range(0, 8), Preorder(doc.RootNode!).Select(n => n.Id));
            Assert.Equal(meshes ? 5 : 0, Preorder(doc.RootNode!).Sum(n => n.Meshes.Count));
        }
    }

    // F1 edits through FormatVectors.Patched (link rules) and file-level defects
    [Theory]
    [InlineData("seqDecreasing", "fragment 1: seq is less than the previous fragment's")]  // F1 int@36 = 3, int@284 = 2: both leaves
    [InlineData("seqOnGroup", "fragment 1: seq does not name a class-22 leaf")]            // F1 int@284 = 4 (node B)
    [InlineData("recOutOfRange", "fragment 0: record 2 is outside 1..1")]                  // F1 int@248 = 2 with N = 1
    [InlineData("bboxChanged", "fragment 0: bounding box differs from the record's")]      // F1 f32@200 = 2.0
    [InlineData("countChanged", "fragment 0: primitive count differs from the record's")]  // F1 int@24 = 3
    public void Open_LinkDefect_Throws(string defect, string rule)
    { Assert.Equal("LcOpNwdFragments " + rule, Assert.Throws<InvalidDataException>(() => Open(defect)).Message); }

    [Theory]
    [InlineData("fragmentsWithoutGeometry", "LcOpNwdFragments: fragments without geometry")] // Build(H1, F1, [], [])
    [InlineData("duplicateGeometryEntry", ": named more than once in the directory")]      // editDirectory adds a second "...\LcOpNwdGeometry"
    [InlineData("wrongB", ": b is not the last stream's compressed length")]                // geometry entry b + 1
    [InlineData("wrongHierarchyLength", ": no stream spans exactly the entry's offset and length")] // hierarchy entry length + 1
    [InlineData("tocPad", "LcOpNwdGeometry TOC: pad byte is not zero at +28")]              // mixed file TOC byte 28 = 1
    [InlineData("tocChunkCount", "LcOpNwdGeometry TOC: chunk count does not match the chunk streams at +4")] // TOC ch = 3
    [InlineData("tocChunkSize", "LcOpNwdGeometry TOC: chunk size does not match the compressed length at +8")] // TOC size[0] + 1
    public void Open_SegmentDefect_Throws(string defect, string rule)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Open(defect));
        Assert.StartsWith("LcOpNwd", ex.Message);
        Assert.EndsWith(rule, ex.Message);
    }

    [Fact]
    public void Open_FragmentsWithoutHierarchy_NeedTreeOff()
    {
        var file = SyntheticNwcFile.Build(null, FormatVectors.F1, [FormatVectors.R1], [120]);
        var ex = Assert.Throws<InvalidDataException>(() => NwcDocument.Open(new MemoryStream(file)));
        Assert.Equal("LcOpNwdFragments fragment 0: seq is outside the hierarchy preorder", ex.Message);

        var doc = NwcDocument.Open(new MemoryStream(file), new NwcReaderOptions { BuildSceneTree = false });
        Assert.Equal(2, doc.Meshes.Count);
        Assert.Null(doc.RootNode);
    }

    [Fact]
    public void Open_GeometryWithoutHierarchyOrFragments_GivesNamedEmptyRoot()
    {
        var doc = NwcDocument.Open(new MemoryStream(SyntheticNwcFile.Build(null, null, [FormatVectors.R1], [120])), fallbackName: "plan.nwc");
        var root = doc.RootNode!;
        Assert.Equal(("plan", "Model", 0), (root.Name, root.Class, root.Id));
        Assert.Empty(root.Children);
        Assert.Empty(doc.Meshes);
    }

    [Fact]
    public void Open_PrefixedDirectoryNames_OpensLikeCache()
    {
        var doc = NwcDocument.Open(new MemoryStream(
            SyntheticNwcFile.Build(FormatVectors.H1, FormatVectors.F1, [FormatVectors.R1], [120], namePrefix: @"Proj\Sub\")));
        Assert.Equal(2, doc.Meshes.Count);
        Assert.Equal(2, doc.Transforms.Count);
        Assert.Equal([2, 7], doc.Meshes.Select(m => m.Node!.Id));
        Assert.All(doc.DbCache!.Entries, e => Assert.StartsWith(@"Proj\Sub\", e.Name));
    }

    [Fact]
    public void Open_GeometrySegmentFirst_LocatesChunksByOffset()
    {
        var mixed = NwcDocument.Open(new MemoryStream(MixedFile()));
        var doc = NwcDocument.Open(new MemoryStream(MixedFile(geometryFirst: true)));
        Assert.Equal([NwcStreamKind.Geometry, NwcStreamKind.Geometry, NwcStreamKind.Toc], doc.Container.Streams.Take(3).Select(s => s.Kind));
        Assert.Equal(mixed.Meshes.Select(m => m.Node!.Id), doc.Meshes.Select(m => m.Node!.Id));
        Assert.Equal(mixed.Meshes.Select(m => m.GetWorldVertices()), doc.Meshes.Select(m => m.GetWorldVertices()));
        Assert.Equal(mixed.Meshes.Select(m => m.Indices), doc.Meshes.Select(m => m.Indices));
        Assert.Equal(mixed.Meshes.Select(m => m.LineIndices), doc.Meshes.Select(m => m.LineIndices));
    }

    [Fact]   // a .nwf-like multi-section file
    public void Open_MultiSectionFileWithoutModelSegments_KeepsTodaysPath()
    {
        byte[] xrefs = Encoding.UTF8.GetBytes(
            """{"Type":"XRefTable","Version":1,"SavedFilename":"fed.nwf","Table":[{"OriginalPath":"a.nwc","RemappedPath":""}]}""");
        var doc = NwcDocument.Open(new MemoryStream(SyntheticNwcFile.Build(
            null, null, [], [], extraStreams: [("LcOpXRefTable", xrefs), ("transforms", LegacyTransformStream())])));
        Assert.Single(doc.XRefs!.References);
        Assert.Equal(new Vector3d(5, 6, 7), Assert.Single(doc.Transforms).Translation);   // only the lenient TransformParser reads it
        Assert.Empty(doc.Meshes);
    }

    [Fact]
    public void Open_SectionOneNotADirectory_KeepsTodaysPath()
    {
        var file = SyntheticNwcFile.Build(FormatVectors.H1, FormatVectors.F1, [FormatVectors.R1], [120], section1: LegacyTransformStream());
        var doc = NwcDocument.Open(new MemoryStream(file));
        Assert.Contains(doc.Transforms, t => t.Translation == new Vector3d(5, 6, 7));
    }

    [Fact]
    public void Fragments_RebuildsF1ByteForByte() =>
        Assert.Equal(FormatVectors.F1, Fragments(new(2, new(10, 0, 0), 1, 2, 4, Rec: 1), new(7, Vector3d.Zero, -1, 2, 4, SharesGeometryOf: 0)));

    [Fact]
    public void Open_PartitionHierarchy_NeedsBuildSceneTreeOff()
    {
        Assert.Throws<NotSupportedException>(() => NwcDocument.Open(new MemoryStream(PartitionFile())));
        var doc = NwcDocument.Open(new MemoryStream(PartitionFile()), new NwcReaderOptions { BuildSceneTree = false });
        Assert.Equal(2, doc.Meshes.Count);
        Assert.All(doc.Meshes, m => Assert.Null(m.Node));
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 0)]
    public void Open_ReadPropertyRecords_ControlsLeafProperties(bool readProperties, int expected)
    {
        var file = SyntheticNwcFile.Build(HierarchyParserTests.PropertyLeafHierarchy(), null, [], []);
        var doc = NwcDocument.Open(new MemoryStream(file), new NwcReaderOptions { ReadPropertyRecords = readProperties });
        var leaf = doc.RootNode!.Children[0].Children[0];
        Assert.Equal(expected, leaf.Properties.Count);
        Assert.Equal(expected == 0 ? 0 : 1, leaf.PropertyTabs.Count);
    }

    [Fact]
    public void Save_StrictlyReadMeshesAndTransforms_ReopenWithSamePlacement()
    {
        // F1 with fragment 1's pose turned into class 15: two class-15 fragments on leaves 2 and 3 sharing record 1
        byte[] fragments = Fragments(new(2, new(10, 0, 0), 1, 2, 4, Rec: 1), new(3, new(0, 5, 0), 1, 2, 4, SharesGeometryOf: 0));
        var doc = NwcDocument.Open(new MemoryStream(SyntheticNwcFile.Build(FormatVectors.H1, fragments, [FormatVectors.R1], [120])));
        var reopened = NwcDocument.Open(new MemoryStream(NwcDocument.Create(meshes: doc.Meshes, transforms: doc.Transforms).ToByteArray()));
        Assert.Equal(2, reopened.Meshes.Count);
        Assert.Equal(doc.Meshes.Select(m => m.Vertices), reopened.Meshes.Select(m => m.Vertices));
        Assert.Equal(doc.Meshes.Select(m => m.Transform!.Translation), reopened.Meshes.Select(m => m.Transform!.Translation));
        Assert.Equal(doc.Meshes.Select(m => m.Indices), reopened.Meshes.Select(m => m.Indices));
    }

    [Fact]   // R2 holds three strips; the reader of a saved file would rebuild one strip over its seven vertices
    public void Save_StrictlyReadMultiStripMesh_Rejects()
    {
        byte[] file = SyntheticNwcFile.Build(FormatVectors.H1, Fragments(new Frag(2, Vector3d.Zero, 1, 3, 4, Rec: 1, MaxZ: 1)), [FormatVectors.R2], [152]);
        var doc = NwcDocument.Open(new MemoryStream(file), new NwcReaderOptions { BuildSceneTree = false, ReadPropertyRecords = false });
        Assert.Contains("Mesh 0 has triangles the .nwc writer cannot store", Assert.Throws<ArgumentException>(() => doc.ToByteArray()).Message);
    }

    [Fact]
    public void Save_StrictlyReadDocumentWithMirrorOrLines_Rejects()
    {
        var doc = NwcDocument.Open(new MemoryStream(MixedFile()));
        Assert.Throws<ArgumentException>(() => doc.ToByteArray());
    }

    /// <summary>The hierarchy with a class-32 top-level node (the H1 bytes with the first node as class 32) + F1 + [R1].</summary>
    internal static byte[] PartitionFile() =>
        SyntheticNwcFile.Build(HierarchyParserTests.PartitionHierarchy(), FormatVectors.F1, [FormatVectors.R1], [120]);

    private static IEnumerable<NwcNode> Preorder(NwcNode node) => [node, .. node.Children.SelectMany(Preorder)];

    // A stream in the lenient transform layout TransformParser reads: [n = 1][100][13][101][15], t = (5, 6, 7), q = (0, 0, 0, 1), unaligned.
    private static byte[] LegacyTransformStream()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(1);
            w.Write(100);
            w.Write(13);
            w.Write(101);
            w.Write(15);
            foreach (double value in new double[] { 5, 6, 7, 0, 0, 0, 1 })
            {
                w.Write(value);
            }
        }

        return stream.ToArray();
    }

    // H1 + Fmix + R1..R3 cut at byte 200, so R2 crosses the chunk boundary.
    private static byte[] MixedFile(bool geometryFirst = false, Action<byte[]>? editToc = null)
    {
        byte[] block = [.. FormatVectors.R1, .. FormatVectors.R2, .. FormatVectors.R3];
        return SyntheticNwcFile.Build(
            FormatVectors.H1, Fmix(), [block[..200], block[200..]], [120, 152, 120], geometryFirst: geometryFirst, editToc: editToc);
    }

    // k3 shares k0's class-93 object; k4 has its own class 93 with the same rec, the second sharing form.
    private static byte[] Fmix() => Fragments(
        new(2, new(10, 0, 0), 1, 2, 4, Rec: 1),
        new(3, new(0, 5, 0), 1, 3, 4, Rec: 2, MaxZ: 1),
        new(6, Vector3d.Zero, 1, 4, 8, Rec: 3),
        new(7, Vector3d.Zero, -1, 2, 4, SharesGeometryOf: 0),
        new(7, new(0, 0, 5), 1, 2, 4, Rec: 1));

    /// <summary>One fragment: <paramref name="S"/> 1 writes a class-15 pose, else class 16; a new class 93 with bbox (0,0,0)-(1,1,MaxZ), or a back-reference to fragment <paramref name="SharesGeometryOf"/>'s.</summary>
    internal sealed record Frag(int Seq, Vector3d T, double S, int A, int B, int Rec = 0, float MaxZ = 0, int SharesGeometryOf = -1);

    // The F1 layout generalised: fragment 0 holds the class-55 appearance, the others back-reference it.
    internal static byte[] Fragments(params Frag[] fragments)
    {
        var w = new LcGraphWriter();
        w.Int(fragments.Length);
        int appearance = 0;
        var geometry = new int[fragments.Length];
        for (int k = 0; k < fragments.Length; k++)
        {
            var f = fragments[k];
            w.New(13);
            w.Double(1.0).Int(f.A).Int(f.B).Int(0).Int(f.Seq);
            w.New(f.S == 1 ? 15 : 16);
            w.Double(f.T.X).Double(f.T.Y).Double(f.T.Z);
            w.Double(0).Double(0).Double(0).Double(1);
            if (f.S != 1)
            {
                w.Double(f.S);
            }

            if (k == 0)
            {
                appearance = w.New(55);
                w.Int(1);
                w.New(54);
                for (int i = 0; i < 14; i++)
                {
                    w.Int(0);
                }
            }
            else
            {
                w.Ref(appearance);
            }

            if (f.SharesGeometryOf >= 0)
            {
                geometry[k] = geometry[f.SharesGeometryOf];
                w.Ref(geometry[k]);
            }
            else
            {
                geometry[k] = w.New(93);
                w.Float(0).Float(0).Float(0).Float(1).Float(1).Float(f.MaxZ);
                w.Double(0.5).Double(0.5).Double(0);
                w.Float(0).Int(7).Int(f.Rec);
            }
        }

        return w.ToArray();
    }

    private static NwcDocument Open(string defect) => NwcDocument.Open(new MemoryStream(defect switch
    {
        "seqDecreasing" => F1File(FormatVectors.Patched(FormatVectors.Patched(FormatVectors.F1, 36, BitConverter.GetBytes(3)), 284, BitConverter.GetBytes(2))),
        "seqOnGroup" => F1File(FormatVectors.Patched(FormatVectors.F1, 284, BitConverter.GetBytes(4))),
        "recOutOfRange" => F1File(FormatVectors.Patched(FormatVectors.F1, 248, BitConverter.GetBytes(2))),
        "bboxChanged" => F1File(FormatVectors.Patched(FormatVectors.F1, 200, BitConverter.GetBytes(2.0f))),
        "countChanged" => F1File(FormatVectors.Patched(FormatVectors.F1, 24, BitConverter.GetBytes(3))),
        "fragmentsWithoutGeometry" => SyntheticNwcFile.Build(FormatVectors.H1, FormatVectors.F1, [], []),
        "duplicateGeometryEntry" => F1File(FormatVectors.F1, e => e.Add(Entry(e, "LcOpNwdGeometry") with { Name = @"copy\LcOpNwdGeometry" })),
        "wrongB" => F1File(FormatVectors.F1, e => Replace(e, "LcOpNwdGeometry", x => x with { Extra = x.Extra + (1L << 32) })),
        "wrongHierarchyLength" => F1File(FormatVectors.F1, e => Replace(e, "LcOpNwdLogicalHierarchy", x => x with { Length = x.Length + 1 })),
        "tocPad" => MixedFile(editToc: t => t[28] = 1),
        "tocChunkCount" => MixedFile(editToc: t => BinaryPrimitives.WriteInt32LittleEndian(t.AsSpan(4), 3)),
        "tocChunkSize" => MixedFile(editToc: t => BinaryPrimitives.WriteInt32LittleEndian(t.AsSpan(8), BinaryPrimitives.ReadInt32LittleEndian(t.AsSpan(8)) + 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(defect)),
    }));

    private static byte[] F1File(byte[] fragments, Action<List<NwcDbCacheEntry>>? editDirectory = null) =>
        SyntheticNwcFile.Build(FormatVectors.H1, fragments, [FormatVectors.R1], [120], editDirectory: editDirectory);

    private static NwcDbCacheEntry Entry(List<NwcDbCacheEntry> entries, string segment) =>
        entries.Single(e => e.Name.EndsWith(@"\" + segment, StringComparison.Ordinal));

    private static void Replace(List<NwcDbCacheEntry> entries, string segment, Func<NwcDbCacheEntry, NwcDbCacheEntry> edit)
    {
        int i = entries.IndexOf(Entry(entries, segment));
        entries[i] = edit(entries[i]);
    }
}
