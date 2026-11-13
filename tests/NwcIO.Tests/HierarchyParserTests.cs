using NwcIO.Internal.Parsing;
using Xunit;

namespace NwcIO.Tests;

public class HierarchyParserTests
{
    [Fact]
    public void Read_H1_ExpandsBackReferencedSubtreeInPreorder()
    {
        var nodes = HierarchyParser.Read(FormatVectors.H1, "Model");
        Assert.Equal(Enumerable.Range(0, 8), nodes.Select(n => n.Id));
        Assert.Equal(["Model", "A", "L1", "L2", "B", "A", "L1", "L2"], nodes.Select(n => n.Name));
        Assert.Equal([0, 53, 22, 22, 53, 53, 22, 22], nodes.Select(n => n.Tag));
        Assert.Equal(["Model", "LcTestGroup", "", "", "LcTestGroup", "LcTestGroup", "", ""], nodes.Select(n => n.Class));
        Assert.Equal([nodes[1], nodes[4]], nodes[0].Children);
        Assert.Equal([nodes[2], nodes[3]], nodes[1].Children);
        Assert.Equal([nodes[5]], nodes[4].Children);
        Assert.Equal([nodes[6], nodes[7]], nodes[5].Children);
        Assert.Same(nodes[4], nodes[5].Parent);
        Assert.NotSame(nodes[1], nodes[5]);
        Assert.All(nodes, n => Assert.Empty(n.Properties));
    }

    [Fact]
    public void Read_PropertyTab_MapsItemsToRecords()
    {
        var w = Begin();
        w.New(53);
        w.Str("G");
        Username(w, "Group", "LcTestGroup");
        w.Int(0).Int(0).Int(0).Int(1);
        w.New(22);
        w.Str("W");
        Username(w, "Wall", "LcRevitData_Element");
        w.Int(0).Int(0).Int(2);
        w.New(86);
        w.Str("Element");
        Username(w, "Element tab", "LcRevitData_Element");
        w.Int(0).Int(6);
        int keyArea = Username(w, "Area", "lcArea");
        w.Int(10).Double(2.5);
        int keyMark = Username(w, "Mark", "lcMark");
        w.Int(4).Str("M1");
        int keyCount = Username(w, "Count", "lcCount");
        w.Int(2).Int(7);
        int keyType = Username(w, "Type", "lcType");
        w.Int(8);
        Username(w, "Basic Wall", "lcTypeRef");
        int keyFlag = Username(w, "Flag", "lcFlag");
        w.Int(0);
        int keyBare = Username(w, null, "lcBare");
        w.Int(4).Str("x");
        w.New(86);
        w.Str(null);
        Username(w, "Identity Data", "LcRevitData_Identity");
        w.Int(0).Int(1);
        int keyMark2 = Username(w, "Mark", "lcMark2");
        w.Int(4).Str("M2");
        var bytes = End(w);

        var leaf = HierarchyParser.Read(bytes, "Model")[2];
        Assert.Equal(("W", "LcRevitData_Element", 22), (leaf.Name, leaf.Class, leaf.Tag));
        Assert.Equal(   // Class = tab: stored name first, then the username display name
            [(keyArea, 52, "Area", "Element", 10, "2.5"), (keyMark, 52, "Mark", "Element", 4, "M1"), (keyCount, 52, "Count", "Element", 2, "7"),
             (keyType, 52, "Type", "Element", 8, "Basic Wall"), (keyFlag, 52, "Flag", "Element", 0, (string?)null), (keyBare, 52, "", "Element", 4, "x"),
             (keyMark2, 52, "Mark", "Identity Data", 4, "M2")],
            leaf.Properties.Select(p => (p.FirstInt, p.Tag, p.Name, p.Class, p.ValueType, p.Value)));
    }

    [Fact]
    public void Read_EveryAttributeClass_ParsesAndKeepsOnlyProperties()
    {
        var bytes = GroupWithLeaf(6, w =>
        {
            w.New(31);
            w.Str("m");
            Doubles(w, 15);
            w.New(41);
            w.Str(null).Int(0).Int(131072);
            Doubles(w, 3 + 4);
            w.New(39);
            w.Str(null).Int(0).Int(131072);
            Doubles(w, 9 + 3);
            w.Int(0);
            Hyperlinks(w);
            Extra(w);
            w.Null();
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        Assert.Equal([0, 53, 22], nodes.Select(n => n.Tag));
        Assert.Empty(nodes[2].Properties);
    }

    [Theory]
    [InlineData("trailingIntIn79")]   // the refuted V79 'b' variant: the parser meets 7 where the tail must be 1
    [InlineData("payloadPad185")]     // the 185 item with its pad byte 0x01
    [InlineData("itemFlag185")]       // the 185 item with e = 2
    [InlineData("valueType9")]        // unknown class-86 value type
    public void Read_AttributeDefect_Throws(string defect)
    {
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(Defective(defect), "Model"));
        Assert.StartsWith("LcOpNwdLogicalHierarchy at byte ", ex.Message);
    }

    [Theory]
    [InlineData("topLevelLeaf")]      // root's top-level slot holds a new 22 (observed: 53 or 32 only)
    [InlineData("kZero")]             // header k = 0
    [InlineData("extra200Null")]      // class 200 with a null extra
    public void Read_SlotFamilyDefect_Throws(string defect)
    {
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(Defective(defect), "Model"));
        Assert.StartsWith("LcOpNwdLogicalHierarchy at byte ", ex.Message);
    }

    [Fact]
    public void Read_DeepChain_ThrowsBeforeStackExhaustion()
    {
        var w = Begin();
        for (int i = 0; i < 2000; i++)
        {
            Group(w, 1);
        }

        Leaf(w, 0, _ => { });
        var bytes = End(w);

        Assert.Contains("nesting deeper than 256", Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(bytes, "Model")).Message);
    }

    [Fact]
    public void Read_DeepExpansionThroughBackReference_Throws()
    {
        // parse nesting stays near 200, but the second chain ends in the first one, so the expanded tree is 302 levels deep
        var w = Begin(n: 2);
        int first = w.Next;
        for (int i = 0; i < 200; i++)
        {
            Group(w, 1);
        }

        Leaf(w, 0, _ => { });
        for (int i = 0; i < 100; i++)
        {
            Group(w, 1);
        }

        w.Ref(first);
        var bytes = End(w);

        Assert.Contains("exceeds 256 levels", Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(bytes, "Model")).Message);
    }

    // H1 byte edits (offsets from the H1 layout in Appendix A)
    [Theory]
    [InlineData(260, 107)]          // B's child -> B itself (unfinished)
    [InlineData(260, 999)]          // back-reference to an unknown id
    [InlineData(264, 2)]            // tail [2]
    [InlineData(268, 1)]            // non-zero tail pad
    [InlineData(192, 103)]          // L2 username -> class-40 object
    [InlineData(80, 45)]            // unknown class 45
    [InlineData(12, int.MaxValue)]  // count beyond remaining bytes
    [InlineData(256, 0)]            // null child pointer
    public void Read_InvalidGraph_Throws(int offset, int value)
    {
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(FormatVectors.Patched(FormatVectors.H1, offset, BitConverter.GetBytes(value)), "Model"));
        Assert.StartsWith("LcOpNwdLogicalHierarchy at byte ", ex.Message);
    }

    [Theory]
    [InlineData(29, 0x01)]          // string pad of A's name
    [InlineData(28, 0xFF)]          // invalid UTF-8 in A's name
    public void Read_InvalidString_Throws(int offset, byte value)
    {
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(FormatVectors.Patched(FormatVectors.H1, offset, [value]), "Model"));
        Assert.StartsWith("LcOpNwdLogicalHierarchy at byte ", ex.Message);
    }

    [Fact]
    public void Read_PartitionNode_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => HierarchyParser.Read(PartitionHierarchy(), "Model"));
    }

    [Fact]
    public void Read_DoublingDag_ThrowsBeforeExpanding()
    {
        // G0 leaf; G1..G30 groups with children [new G(i-1), ref G(i-1)], so G30 expands to 2^31 - 1 nodes
        var w = Begin();
        var ids = new int[31];
        for (int i = 30; i >= 1; i--)
        {
            ids[i] = w.Next;
            Group(w, 2);
        }

        ids[0] = w.Next;
        Leaf(w, 0, _ => { });
        for (int i = 1; i <= 30; i++)
        {
            w.Ref(ids[i - 1]);
        }

        var bytes = End(w);

        Assert.Contains("8388608", Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(bytes, "Model")).Message);
    }

    [Fact]
    public void Read_DoublingDagOverPropertyLeaf_ThrowsBeforeExpanding()
    {
        // G0 is a leaf with 129 properties; G1..G20 double it, so G20 holds 2^21 - 1 nodes (under the node cap) but 129 * 2^20 property copies
        var w = Begin();
        var ids = new int[21];
        for (int i = 20; i >= 1; i--)
        {
            ids[i] = w.Next;
            Group(w, 2);
        }

        ids[0] = w.Next;
        Leaf(w, 1, l => PropertyTab(l, 129));
        for (int i = 1; i <= 20; i++)
        {
            w.Ref(ids[i - 1]);
        }

        var (message, allocated) = ReadFailing(End(w));

        Assert.Contains("134217728 property references", message);
        Assert.InRange(allocated, 0, 16 << 20);
    }

    [Fact]
    public void Read_RepeatedPropertyTab_ThrowsBeforeCopying()
    {
        // one leaf references the same 12,000-item tab 11,200 times: 134,400,000 property references from about 235 KB
        var w = Begin();
        Group(w, 1);
        Leaf(w, 11_200, l =>
        {
            int tab = l.Next;
            PropertyTab(l, 12_000);
            for (int i = 1; i < 11_200; i++)
            {
                l.Ref(tab);
            }
        });

        var (message, allocated) = ReadFailing(End(w));

        Assert.Contains("134217728 property references", message);
        Assert.InRange(allocated, 0, 16 << 20);
    }

    [Fact]
    public void Read_NestedChildCountsNearTheRemainingBytes_AllocatesOnlyWhatIsRead()
    {
        // 300 nested groups each declare 131,072 children, which 1 MiB of trailing bytes makes plausible; presized arrays would take 1 MiB per level
        const int declared = 131_072;
        var w = Begin();
        for (int i = 0; i < 300; i++)
        {
            Group(w, declared);
        }

        var (message, allocated) = ReadFailing([.. w.ToArray(), .. new byte[declared * 8]]);

        Assert.Contains("nesting deeper than 256", message);
        Assert.InRange(allocated, 0, 16 << 20);
    }

    // [k = 1][0][root 100][n = 1], then the top-level partition node: [i32 0] + a class-53 body + 16 zero bytes for its trailer.
    internal static byte[] PartitionHierarchy()
    {
        var w = Begin();
        w.New(32);
        w.Int(0);
        w.Str("P");
        Username(w, "P", "LcOaPartition");
        w.Int(0).Int(0).Int(0).Int(0);
        w.Int(0).Int(0).Int(0).Int(0);
        return End(w);
    }

    // [k][k zero ints][root 100][n]; the n top-level objects follow.
    private static LcGraphWriter Begin(int k = 1, int n = 1)
    {
        var w = new LcGraphWriter();
        w.Int(k);
        for (int i = 0; i < k; i++)
        {
            w.Int(0);
        }

        w.ImplicitRoot();
        w.Int(n);
        return w;
    }

    // The tail [1] and the zero pad to a multiple of 8.
    private static byte[] End(LcGraphWriter w) => w.Int(1).ToArray();

    // The read's error message and the bytes it allocated on this thread.
    private static (string Message, long Allocated) ReadFailing(byte[] bytes)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(bytes, "Model"));
        return (ex.Message, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // New class-86 tab "T" whose count items each back-reference its username as the key and carry no value: 12 bytes per item.
    private static void PropertyTab(LcGraphWriter w, int count)
    {
        w.New(86);
        w.Str("T");
        int key = Username(w, "K", "lcK");
        w.Int(0).Int(count);
        for (int i = 0; i < count; i++)
        {
            w.Ref(key).Int(0);
        }
    }

    private static int Username(LcGraphWriter w, string? name, string? internalName)
    {
        int id = w.New(52);
        w.Str(name).Str(internalName);
        return id;
    }

    // New group "G" with no attributes; its childCount children follow.
    private static void Group(LcGraphWriter w, int childCount)
    {
        w.New(53);
        w.Str("G");
        Username(w, "G", "LcTestGroup");
        w.Int(0).Int(0).Int(0).Int(childCount);
    }

    private static void Leaf(LcGraphWriter w, int attributeCount, Action<LcGraphWriter> attributes)
    {
        w.New(22);
        w.Str("L");
        Username(w, "L", "LcTestLeaf");
        w.Int(0).Int(0).Int(attributeCount);
        attributes(w);
    }

    // Top-level group G with one child: a leaf holding the attributes.
    private static byte[] GroupWithLeaf(int attributeCount, Action<LcGraphWriter> attributes, int k = 1)
    {
        var w = Begin(k);
        Group(w, 1);
        Leaf(w, attributeCount, attributes);
        return End(w);
    }

    private static void Doubles(LcGraphWriter w, int count)
    {
        for (int i = 0; i < count; i++)
        {
            w.Double(i + 1);
        }
    }

    // Class 79 with one item: "a", "b", a null item pointer, one point.
    private static void Hyperlinks(LcGraphWriter w)
    {
        w.New(79);
        w.Str("h").Int(0).Int(0).Int(1);
        w.Str("a").Str("b").Null().Int(1);
        Doubles(w, 3);
    }

    // Class 200 with a new class-185 extra holding one embedded item: 3 payload bytes 01 02 03, then the pad byte (payload's top byte).
    private static void Extra(LcGraphWriter w, int itemFlag = 1, int payload = 0x00030201)
    {
        w.New(200);
        w.Str("j").Int(0).Int(0);
        w.New(185);
        w.Str("j").Str("i").Int(1);
        w.Str("s").Int(itemFlag).Str("t");
        w.Str("u").Long(3).Int(payload);
    }

    private static byte[] Defective(string defect) => defect switch
    {
        "trailingIntIn79" => GroupWithLeaf(1, w =>
        {
            Hyperlinks(w);
            w.Int(7);
        }),
        "payloadPad185" => GroupWithLeaf(1, w => Extra(w, payload: 0x01030201)),
        "itemFlag185" => GroupWithLeaf(1, w => Extra(w, itemFlag: 2)),
        "valueType9" => GroupWithLeaf(1, w =>
        {
            w.New(86);
            w.Str("T");
            Username(w, "T", "lcT");
            w.Int(0).Int(1);
            Username(w, "K", "lcK");
            w.Int(9).Int(0);
        }),
        "topLevelLeaf" => End(LeafAtTopLevel()),
        "kZero" => GroupWithLeaf(0, _ => { }, k: 0),
        "extra200Null" => GroupWithLeaf(1, w =>
        {
            w.New(200);
            w.Str("j").Int(0).Int(0).Null();
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(defect)),
    };

    private static LcGraphWriter LeafAtTopLevel()
    {
        var w = Begin();
        Leaf(w, 0, _ => { });
        return w;
    }
}
