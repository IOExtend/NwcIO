using System.Buffers.Binary;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class PartitionPropsTests
{
    private static readonly Tab T1 = new(null, "Element", "LcRevitData_Element", new Prop("Id", "LcRevitPropertyElementId", 2, 1001));
    private static readonly Tab T2 = new(null, "Element", "LcRevitData_Element", new Prop("Id", "LcRevitPropertyElementId", 2, 1002));
    private static readonly Tab TH = new(null, "Type", "LcRevitData_Type", new Prop("Name", "LcRevitPropertyElementName", 4, "T"));

    // One one-item class-86 tab: 60 bytes + 4 pad.
    private static readonly byte[] OneTab = P(p => HierarchyParserTests.PropertyTab(p, 1));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]   // L1's roots in stream 0, L2's in stream 1
    public void Read_NullSlotsTakeTheRootsInOrder(int streams)
    {
        var (h, p) = SyntheticTree.Write(streams, Top());
        var nodes = Read(h, p);
        var (l1, l2) = (nodes[2], nodes[3]);
        Assert.Equal([("Element", "LcRevitData_Element"), ("Type", "LcRevitData_Type"), ("Element ID", "LcRevitId")], l1.PropertyTabs.Select(t => (t.Name, t.InternalName)));
        var id = Assert.Single(l1.PropertyTabs[2].Properties);
        Assert.Equal(("Element ID", "Element ID", "LcRevitId", "LcRevitId", 2, "1001"), (id.Name, id.Class, id.TabInternalName, id.InternalName, id.ValueType, id.Value));
        Assert.Equal("1002", l2.PropertyTabs[2].Properties[0].Value);
        Assert.Same(l1.PropertyTabs[1], l2.PropertyTabs[1]);
        Assert.Equal(["1001", "T", "1001"], l1.Properties.Select(r => r.Value));
    }

    [Fact]
    public void Read_PropertiesOff_StillChecksTheRoots()
    {
        var (h, p) = SyntheticTree.Write(1, Top());
        Assert.Empty(Read(h, p, includeProperties: false)[2].PropertyTabs);
        Assert.Throws<InvalidDataException>(() => Read(h, [p[0][..^8]], includeProperties: false));
    }

    [Theory]
    [InlineData("extraRoot", "LcOpNwdLogicalHierarchy at byte ", ": 1 LcOaPartitionProps roots fill no null attribute slot")]
    [InlineData("missingRoot", "LcOpNwdLogicalHierarchy at byte ", ": null attribute slot 2 has no LcOaPartitionProps root")]
    [InlineData("noSegment", "LcOpNwdLogicalHierarchy at byte ", ": the header names 1 LcOaPartitionProps roots, but the file has no LcOaPartitionProps segment")]
    [InlineData("streamCount", "LcOaPartitionProps: 2 root streams, but the hierarchy header names 1", "")]
    [InlineData("rootClass52", "LcOaPartitionProps stream 0 at byte ", ": class 52, which this slot does not allow")]
    [InlineData("rootNull", "LcOaPartitionProps stream 0 at byte ", ": null pointer in a slot that needs an object")]
    [InlineData("name66", "LcOaPartitionProps stream 0 at byte ", ": class-66 name is not null")]
    [InlineData("flags66", "LcOaPartitionProps stream 0 at byte ", ": class-66 flags 00000001 are not 01000000")]
    [InlineData("value66", "LcOaPartitionProps stream 0 at byte ", ": class-66 value 0 is outside 1..2147483647")]
    [InlineData("rootPad", "LcOaPartitionProps stream 0 at byte ", ": end pad is not zero")]
    [InlineData("rootTrailing", "LcOaPartitionProps stream 0 at byte ", ": data does not end at the zero pad to a multiple of 8")]
    public void Read_BrokenLink_Throws(string defect, string start, string end)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ReadDefect(defect));
        Assert.StartsWith(start, ex.Message);
        Assert.EndsWith(end, ex.Message);
    }

    [Fact]
    public void Read_Class79Root_IsDropped()
    {
        var (h, p) = SyntheticTree.Write(1, new TreeNode("G", "LcRevitLayer", Children: [
            new("L1", "LcRevitInstance", Slots: [new PTab(T1), new P79(), new PId(1001)]),
            new("L2", "LcRevitInstance", Slots: [new PId(1002)])]));
        var nodes = Read(h, p);
        Assert.Equal(["LcRevitData_Element", "LcRevitId"], nodes[2].PropertyTabs.Select(t => t.InternalName));
        Assert.Equal("1002", Assert.Single(nodes[3].PropertyTabs).Properties[0].Value);
    }

    [Fact]
    public void Read_SharedStoredNode_TakesItsRootsOnce()
    {
        var shared = new TreeNode("S", "LcRevitInstance", Slots: [new PTab(T1)]);
        var (h, p) = SyntheticTree.Write(1, new TreeNode("A", "LcRevitLayer", Children: [shared]),
            new TreeNode("B", "LcRevitLayer", Children: [shared, new("L2", "LcRevitInstance", Slots: [new PTab(T2)])]));
        var nodes = Read(h, p);   // 0 root, 1 A, 2 S, 3 B, 4 S, 5 L2
        Assert.Same(nodes[2].PropertyTabs[0], nodes[4].PropertyTabs[0]);
        Assert.Equal("1002", nodes[5].PropertyTabs[0].Properties[0].Value);
    }

    [Fact]
    public void Read_PropsTabsCountTowardPropertyCap()
    {
        // G0 is a leaf whose one null slot takes a 129-item LcOaPartitionProps tab; G1..G20 double it: (129 + 1) * 2^20 references
        var w = HierarchyParserTests.Begin(roots: [1]);
        var ids = new int[21];
        for (int i = 20; i >= 1; i--)
        {
            ids[i] = w.Next;
            HierarchyParserTests.Group(w, 2);
        }

        ids[0] = w.Next;
        HierarchyParserTests.Leaf(w, 1, l => l.Null());
        for (int i = 1; i <= 20; i++)
        {
            w.Ref(ids[i - 1]);
        }

        var props = new LcGraphWriter();
        HierarchyParserTests.PropertyTab(props, 129);
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(HierarchyParserTests.End(w), "Model", true, null, () => [props.ToArray()]));
        Assert.Contains("134217728 property references", ex.Message);
    }

    [Fact]
    public void Open_ModelFile_AttachesTheProps() =>
        Assert.Equal(3, NwcDocument.Open(new MemoryStream(File(null))).RootNode!.Children[0].Children[0].PropertyTabs.Count);

    [Theory]
    [InlineData(0, 3, "LcOaPartitionProps: the table of contents names 3 streams, not 2")]
    [InlineData(4, 999, "LcOaPartitionProps: the table of contents gives stream 0 999 bytes, not ")]
    [InlineData(-1, 0, "LcOaPartitionProps: the table of contents pad is not zero")]   // -1: set the last TOC byte (a pad byte for k = 2) to 1
    [InlineData(-2, 0, "LcOaPartitionProps: the table of contents is 8 bytes, not 16")] // -2: keep only the first 8 bytes
    public void Open_BrokenToc_Throws(int offset, int value, string start)
    {
        var ex = Assert.Throws<InvalidDataException>(() => NwcDocument.Open(new MemoryStream(File(toc =>
        {
            if (offset == -2)
            {
                return toc[..8];
            }

            if (offset == -1)
            {
                toc[^1] = 1;
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(toc.AsSpan(offset), value);
            }

            return toc;
        }))));
        Assert.StartsWith(start, ex.Message);
    }

    [Fact]
    public void Open_WithoutTree_DoesNotReadTheProps() =>
        Assert.Null(NwcDocument.Open(new MemoryStream(File(toc => { toc[^1] = 1; return toc; })), new NwcReaderOptions { BuildSceneTree = false }).RootNode);

    [Fact]
    public void Open_TwoPropsEntries_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => NwcDocument.Open(new MemoryStream(TwoPropsEntriesFile())));
        Assert.StartsWith("LcOaPartitionProps DbCache entry at file byte ", ex.Message);
        Assert.EndsWith(": named more than once in the directory", ex.Message);
    }

    [Fact]
    public void Open_PartitionHierarchy_ThrowsNotSupportedBeforeThePropsChecks() =>
        Assert.Throws<NotSupportedException>(() => NwcDocument.Open(new MemoryStream(PartitionFileWithBrokenToc())));

    [Fact]
    public void Open_WithoutTree_ChecksNoPropsEntry()
    {
        var options = new NwcReaderOptions { BuildSceneTree = false };
        Assert.Null(NwcDocument.Open(new MemoryStream(PartitionFileWithBrokenToc()), options).RootNode);
        Assert.Null(NwcDocument.Open(new MemoryStream(TwoPropsEntriesFile()), options).RootNode);
    }

    [Fact]
    public void Open_SegmentWithOnlyItsToc_Throws()
    {
        // The entry is moved onto the TOC stream alone, a TOC for k = 0: a present segment with no root stream, header [1][1].
        var file = SyntheticNwcFile.Build(H([1], 1), null, [], [], partitionProps: [OneTab], editPropsToc: _ => new byte[8],
            editDirectory: e =>
            {
                int i = e.FindIndex(x => x.Name.EndsWith("LcOaPartitionProps", StringComparison.Ordinal));
                long b = e[i].Extra >> 32;
                e[i] = e[i] with { Offset = e[i].Offset + e[i].Length - b - 4, Length = b + 4 };
            });
        var ex = Assert.Throws<InvalidDataException>(() => NwcDocument.Open(new MemoryStream(file)));
        Assert.Equal("LcOaPartitionProps: 0 root streams, but the hierarchy header names 1", ex.Message);
    }

    private static IReadOnlyList<NwcNode> Read(byte[] h, byte[][] p, bool includeProperties = true) =>
        HierarchyParser.Read(h, "Model", includeProperties, null, () => p);

    private static TreeNode[] Top() =>
        [new("G", "LcRevitLayer", Children: [
            new("L1", "LcRevitInstance", Slots: [new PTab(T1), new HTab(TH), new PId(1001)]),
            new("L2", "LcRevitInstance", Slots: [new PTab(T2), new HTab(TH), new PId(1002)])])];

    // The model file of Top() in two root streams: its TOC is [2][len0][len1] plus 4 pad bytes, 16 bytes in all.
    private static byte[] File(Func<byte[], byte[]>? editPropsToc)
    {
        var (h, p) = SyntheticTree.Write(2, Top());
        return SyntheticNwcFile.Build(h, null, [], [], partitionProps: p, editPropsToc: editPropsToc);
    }

    private static byte[] TwoPropsEntriesFile()
    {
        var (h, p) = SyntheticTree.Write(2, Top());
        return SyntheticNwcFile.Build(h, null, [], [], partitionProps: p,
            editDirectory: e => e.Add(e.Single(x => x.Name.EndsWith("LcOaPartitionProps", StringComparison.Ordinal))));
    }

    // A .nwd-like hierarchy (a class-32 top-level node) whose header names one root, with a props TOC that names 3 streams, not 1.
    private static byte[] PartitionFileWithBrokenToc() =>
        SyntheticNwcFile.Build(HierarchyParserTests.PartitionHierarchy([1]), null, [], [], partitionProps: [OneTab], editPropsToc: toc =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(toc, 3);
            return toc;
        });

    // A top-level group whose one leaf has nullSlots null attribute slots; header [roots.Length][roots].
    private static byte[] H(int[] roots, int nullSlots)
    {
        var w = HierarchyParserTests.Begin(roots.Length, 1, roots);
        HierarchyParserTests.Group(w, 1);
        HierarchyParserTests.Leaf(w, nullSlots, l =>
        {
            for (int i = 0; i < nullSlots; i++)
            {
                l.Null();
            }
        });
        return HierarchyParserTests.End(w);
    }

    // One root stream written by write (ids from 100, padded to 8).
    private static byte[] P(Action<LcGraphWriter> write)
    {
        var p = new LcGraphWriter();
        write(p);
        return p.ToArray();
    }

    private static void Id66(LcGraphWriter p, string? name = null, int flags = 0x01000000, long value = 1001)
    {
        p.New(66);
        p.Str(name);
        HierarchyParserTests.Username(p, "Element ID", "LcRevitId");
        p.Int(flags).Long(value);
    }

    private static IReadOnlyList<NwcNode> ReadDefect(string defect) => defect switch
    {
        "extraRoot" => Read(H([2], 1), [P(p =>
        {
            HierarchyParserTests.PropertyTab(p, 1);
            HierarchyParserTests.PropertyTab(p, 1);
        })]),
        "missingRoot" => Read(H([1], 2), [OneTab]),
        "noSegment" => HierarchyParser.Read(H([1], 1), "Model", true, null, null),
        "streamCount" => Read(H([1], 1), [OneTab, P(_ => { })]),
        "rootClass52" => Read(H([1], 1), [P(p => HierarchyParserTests.Username(p, "K", "lcK"))]),
        "rootNull" => Read(H([1], 1), [P(p => p.Null())]),
        "name66" => Read(H([1], 1), [P(p => Id66(p, name: "x"))]),
        "flags66" => Read(H([1], 1), [P(p => Id66(p, flags: 1))]),
        "value66" => Read(H([1], 1), [P(p => Id66(p, value: 0))]),
        "rootPad" => Read(H([1], 1), [[.. OneTab[..^1], 1]]),
        "rootTrailing" => Read(H([1], 1), [[.. OneTab, .. new byte[8]]]),
        _ => throw new ArgumentOutOfRangeException(nameof(defect)),
    };
}
