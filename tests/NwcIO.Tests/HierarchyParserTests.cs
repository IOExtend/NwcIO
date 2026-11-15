using NwcIO.Internal.Parsing;
using NwcIO.Model;
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
        Assert.Equal(
            [("LcRevitData_Element", "lcArea"), ("LcRevitData_Element", "lcMark"), ("LcRevitData_Element", "lcCount"), ("LcRevitData_Element", "lcType"),
             ("LcRevitData_Element", "lcFlag"), ("LcRevitData_Element", "lcBare"), ("LcRevitData_Identity", "lcMark2")],
            leaf.Properties.Select(p => (p.TabInternalName, p.InternalName)));
        Assert.Equal([("Element", "LcRevitData_Element", 6), ("Identity Data", "LcRevitData_Identity", 1)],
            leaf.PropertyTabs.Select(t => (t.Name, t.InternalName, t.Properties.Count)));
        Assert.Same(leaf.PropertyTabs[0].Properties[0], leaf.Properties[0]);
    }

    [Fact]
    public void Read_ValueType9_IsAString()
    {
        var leaf = HierarchyParser.Read(GroupWithLeaf(1, w =>
        {
            w.New(86);
            w.Str("T");
            Username(w, "T", "lcT");
            w.Int(0).Int(1);
            Username(w, "K", "lcK");
            w.Int(9).Str("ID-9");
        }), "Model")[2];
        Assert.Equal((9, "ID-9"), (leaf.Properties[0].ValueType, leaf.Properties[0].Value));
    }

    [Fact]
    public void Read_ValueType5_IsDateTimeFromUnixSeconds()
    {
        var leaf = HierarchyParser.Read(GroupWithLeaf(1, w =>
        {
            w.New(86);
            w.Str("T");
            Username(w, "T", "lcT");
            w.Int(0).Int(1);
            Username(w, "CreatedDate", "lcDate");
            w.Int(5).Long(1775044800L); // 2026-04-01T12:00:00Z
        }), "Model")[2];
        Assert.Equal((5, "2026-04-01T12:00:00Z"), (leaf.Properties[0].ValueType, leaf.Properties[0].Value));
    }

    [Fact]
    public void Read_ValueType12And13_Are3DAnd2DPoints()
    {
        var leaf = HierarchyParser.Read(GroupWithLeaf(1, w =>
        {
            w.New(86);
            w.Str("T");
            Username(w, "T", "lcT");
            w.Int(0).Int(2);
            Username(w, "P3", "lcP3");
            w.Int(12).Double(10.5).Double(20.5).Double(30.5);
            Username(w, "P2", "lcP2");
            w.Int(13).Double(0.25).Double(0.75);
        }), "Model")[2];
        Assert.Equal((12, "(10.5, 20.5, 30.5)"), (leaf.Properties[0].ValueType, leaf.Properties[0].Value));
        Assert.Equal((13, "(0.25, 0.75)"), (leaf.Properties[1].ValueType, leaf.Properties[1].Value));
    }

    [Fact]
    public void Read_EveryAttributeClass_ParsesAndKeepsOnlyProperties()
    {
        var bytes = GroupWithLeaf(6, w =>
        {
            w.New(31);
            w.Str("m");
            Doubles(w, 15);
            w.New(40);
            w.Str(null).Int(0).Int(131072);
            Doubles(w, 3);
            w.New(41);
            w.Str(null).Int(0).Int(131072);
            Doubles(w, 3 + 4);
            w.New(39);
            w.Str(null).Int(0).Int(131072);
            Doubles(w, 9 + 3);
            w.Int(0);
            Hyperlinks(w);
            Extra(w);
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        Assert.Equal([0, 53, 22], nodes.Select(n => n.Tag));
        Assert.Empty(nodes[2].Properties);
    }

    [Theory]
    [InlineData("trailingIntIn79", "hierarchy tail is not 1")]                    // the parser meets 7 where the tail must be 1
    [InlineData("payloadPad185", "pad after the bytes is not zero")]              // the 185 item with its pad byte 0x01
    [InlineData("itemFlag185", "class-185 item flag 2 is neither 0 nor 1")]      // the 185 item with e = 2
    [InlineData("lengthNegative185", "byte length -1 is negative")]               // the 185 item with length -1
    [InlineData("payloadPastEnd185", "1000 bytes pass the end of the data")]      // the 185 item with length 1000
    [InlineData("valueType99", "property value type 99 is unknown")]              // unknown class-86 value type
    public void Read_AttributeDefect_Throws(string defect, string rule) => AssertRule(rule, Defective(defect));

    [Theory]
    [InlineData("topLevelLeaf", "class 22, which this slot does not allow")]          // root's top-level slot holds a new 22 (observed: 53 or 32 only)
    [InlineData("kZero", "header count k is below 1")]                                // header k = 0
    [InlineData("extra200Null", "null pointer in a slot that needs an object")]       // class 200 with a null extra
    public void Read_SlotFamilyDefect_Throws(string defect, string rule) => AssertRule(rule, Defective(defect));

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

    // H1 byte edits (offsets from the H1 layout in FormatVectors)
    [Theory]
    [InlineData(260, 107, "back-reference to unfinished object 107")]                     // B's child -> B itself (unfinished)
    [InlineData(260, 999, "back-reference to unknown id 999")]                            // back-reference to an unknown id
    [InlineData(264, 2, "hierarchy tail is not 1")]                                       // tail [2]
    [InlineData(268, 1, "end pad is not zero")]                                           // non-zero tail pad
    [InlineData(192, 103, "back-reference to class 40, which this slot does not allow")]  // L2 username -> class-40 object
    [InlineData(80, 45, "class 45, which this slot does not allow")]                      // unknown class 45
    [InlineData(12, int.MaxValue, "count 2147483647 is negative or exceeds the remaining bytes")] // count beyond remaining bytes
    [InlineData(256, 0, "null pointer in a slot that needs an object")]                   // null child pointer
    public void Read_InvalidGraph_Throws(int offset, int value, string rule)
        => AssertRule(rule, FormatVectors.Patched(FormatVectors.H1, offset, BitConverter.GetBytes(value)));

    [Theory]
    [InlineData(29, 0x01, "pad after the bytes is not zero")]   // string pad of A's name
    [InlineData(28, 0xFF, "string is not valid UTF-8")]         // invalid UTF-8 in A's name
    public void Read_InvalidString_Throws(int offset, byte value, string rule)
        => AssertRule(rule, FormatVectors.Patched(FormatVectors.H1, offset, [value]));

    [Fact]
    public void Read_PartitionNode_ThrowsNotSupported()
    {
        var ex = Assert.Throws<NotSupportedException>(() => HierarchyParser.Read(PartitionHierarchy(), "Model"));
        Assert.Equal(
            "LcOpNwdLogicalHierarchy holds .nwd partition nodes (class 32), which are not decoded; open with BuildSceneTree = false to read geometry and placement.",
            ex.Message);
    }

    [Fact]
    public void Read_SharedPropertyTab_SharesRecords()
    {
        // two leaves of one group; the second back-references the first one's class-86 tab
        var w = Begin();
        Group(w, 2);
        int tab = 0;
        Leaf(w, 1, l =>
        {
            tab = l.Next;
            PropertyTab(l, 2);
        });
        Leaf(w, 1, l => l.Ref(tab));
        var nodes = HierarchyParser.Read(End(w), "Model");

        Assert.Equal(2, nodes[2].Properties.Count);
        Assert.All(Enumerable.Range(0, 2), i => Assert.Same(nodes[2].Properties[i], nodes[3].Properties[i]));
        Assert.Same(nodes[2].PropertyTabs[0], nodes[3].PropertyTabs[0]);
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
    public void Read_EmptyTabReferencesCountTowardPropertyCap()
    {
        // G0 is a leaf whose 129 slots point to one empty tab; G1..G20 double it: 129 * 2^20 tab references, each copied into PropertyTabs
        var w = Begin();
        var ids = new int[21];
        for (int i = 20; i >= 1; i--)
        {
            ids[i] = w.Next;
            Group(w, 2);
        }

        ids[0] = w.Next;
        Leaf(w, 129, l =>
        {
            int tab = l.Next;
            PropertyTab(l, 0);
            for (int i = 1; i < 129; i++)
            {
                l.Ref(tab);
            }
        });
        for (int i = 1; i <= 20; i++)
        {
            w.Ref(ids[i - 1]);
        }

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

    // [k = 1][roots ?? 0][root 100][n = 1], then the top-level partition node: [i32 0] + a class-53 body + 16 zero bytes for its trailer.
    internal static byte[] PartitionHierarchy(int[]? roots = null)
    {
        var w = Begin(roots: roots);
        w.New(32);
        w.Int(0);
        w.Str("P");
        Username(w, "P", "LcOaPartition");
        w.Int(0).Int(0).Int(0).Int(0);
        w.Int(0).Int(0).Int(0).Int(0);
        return End(w);
    }

    // A group whose one leaf holds a three-item class-86 tab.
    internal static byte[] PropertyLeafHierarchy() => GroupWithLeaf(1, w => PropertyTab(w, 3));

    // [k][roots, else k zeros][root 100][n]; the n top-level objects follow.
    internal static LcGraphWriter Begin(int k = 1, int n = 1, int[]? roots = null)
    {
        var w = new LcGraphWriter();
        w.Int(k);
        foreach (int count in roots ?? new int[k])
        {
            w.Int(count);
        }

        w.ImplicitRoot();
        w.Int(n);
        return w;
    }

    // The tail [1] and the zero pad to a multiple of 8.
    internal static byte[] End(LcGraphWriter w) => w.Int(1).ToArray();

    private static void AssertRule(string rule, byte[] bytes)
    {
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(bytes, "Model"));
        Assert.StartsWith("LcOpNwdLogicalHierarchy at byte ", ex.Message);
        Assert.EndsWith(": " + rule, ex.Message);
    }

    // The read's error message and the bytes it allocated on this thread.
    private static (string Message, long Allocated) ReadFailing(byte[] bytes)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Assert.Throws<InvalidDataException>(() => HierarchyParser.Read(bytes, "Model"));
        return (ex.Message, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // New class-86 tab "T" whose count items each back-reference its username as the key and carry no value: 12 bytes per item.
    internal static void PropertyTab(LcGraphWriter w, int count)
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

    internal static int Username(LcGraphWriter w, string? name, string? internalName)
    {
        int id = w.New(52);
        w.Str(name).Str(internalName);
        return id;
    }

    // New group "G" with no attributes; its childCount children follow.
    internal static void Group(LcGraphWriter w, int childCount)
    {
        w.New(53);
        w.Str("G");
        Username(w, "G", "LcTestGroup");
        w.Int(0).Int(0).Int(0).Int(childCount);
    }

    internal static void Leaf(LcGraphWriter w, int attributeCount, Action<LcGraphWriter> attributes)
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

    // Class 200 with a new class-185 extra holding one embedded item: length 3, payload bytes 01 02 03, then the pad byte (payload's top byte).
    private static void Extra(LcGraphWriter w, int itemFlag = 1, int payload = 0x00030201, long length = 3)
    {
        w.New(200);
        w.Str("j").Int(0).Int(0);
        w.New(185);
        w.Str("j").Str("i").Int(1);
        w.Str("s").Int(itemFlag).Str("t");
        w.Str("u").Long(length).Int(payload);
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
        "lengthNegative185" => GroupWithLeaf(1, w => Extra(w, length: -1)),
        "payloadPastEnd185" => GroupWithLeaf(1, w => Extra(w, length: 1000)),
        "valueType99" => GroupWithLeaf(1, w =>
        {
            w.New(86);
            w.Str("T");
            Username(w, "T", "lcT");
            w.Int(0).Int(1);
            Username(w, "K", "lcK");
            w.Int(99).Int(0);
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

    [Fact]
    public void Read_TransformClass41_DecodesTranslationAndRotation()
    {
        double h = Math.Sqrt(0.5);
        byte[] bytes = GroupWithLeaf(1, w =>
        {
            w.New(41);
            w.Str("Transform");
            w.Int(0).Int(0); // x, flags
            w.Double(10).Double(20).Double(30); // tx, ty, tz
            w.Double(0).Double(0).Double(h).Double(h); // qx, qy, qz, qw
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        var leaf = nodes[2];
        Assert.NotNull(leaf.Transform);
        Assert.Equal(new Vector3d(10, 20, 30), leaf.Transform.Translation);
        Assert.True(leaf.Transform.Rotation.IsNormalized());
        Assert.Equal(0, leaf.Transform.Rotation.X, 10);
        Assert.Equal(0, leaf.Transform.Rotation.Y, 10);
        Assert.Equal(h, leaf.Transform.Rotation.Z, 10);
        Assert.Equal(h, leaf.Transform.Rotation.W, 10);
        Assert.Equal(1.0, leaf.Transform.Scale);
    }

    [Fact]
    public void Read_MatrixClass39_DecodesAffineTransformAndScale()
    {
        byte[] bytes = GroupWithLeaf(1, w =>
        {
            w.New(39);
            w.Str("Matrix");
            w.Int(0).Int(0); // x, flags
            // 3x3 matrix with reflection across X and scaled by 2
            w.Double(-2).Double(0).Double(0);
            w.Double(0).Double(2).Double(0);
            w.Double(0).Double(0).Double(2);
            // translation
            w.Double(100).Double(200).Double(300);
            // reverses = 1 -> negative scale
            w.Int(1);
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        var leaf = nodes[2];
        Assert.NotNull(leaf.Transform);
        Assert.Equal(new Vector3d(100, 200, 300), leaf.Transform.Translation);
        Assert.Equal(-2.0, leaf.Transform.Scale);
        Assert.True(leaf.Transform.Rotation.IsNormalized());

        // Verify roundtrip to 4x4 matrix: ONLY X is reflected (-2), Y (+2) and Z (+2) are NOT inverted!
        double[] m = leaf.Transform.ToMatrix4x4();
        Assert.Equal(-2.0, m[0], 6);
        Assert.Equal(0.0, m[1], 6);
        Assert.Equal(0.0, m[2], 6);

        Assert.Equal(0.0, m[4], 6);
        Assert.Equal(2.0, m[5], 6);
        Assert.Equal(0.0, m[6], 6);

        Assert.Equal(0.0, m[8], 6);
        Assert.Equal(0.0, m[9], 6);
        Assert.Equal(2.0, m[10], 6);

        Assert.Equal(100.0, m[12], 6);
        Assert.Equal(200.0, m[13], 6);
        Assert.Equal(300.0, m[14], 6);
    }

    [Fact]
    public void Read_MatrixClass39_NonUniformScale_PreservesExactMatrix()
    {
        byte[] bytes = GroupWithLeaf(1, w =>
        {
            w.New(39);
            w.Str("Matrix");
            w.Int(0).Int(0); // x, flags
            // Non-uniform diagonal scale: sx=2, sy=3, sz=4
            w.Double(2).Double(0).Double(0);
            w.Double(0).Double(3).Double(0);
            w.Double(0).Double(0).Double(4);
            // translation
            w.Double(10).Double(20).Double(30);
            w.Int(0);
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        var leaf = nodes[2];
        Assert.NotNull(leaf.Transform);
        Assert.False(leaf.Transform.IsUniform);
        Assert.NotNull(leaf.Transform.Matrix);

        double[] m = leaf.Transform.ToMatrix4x4();
        Assert.Equal(2.0, m[0], 6);
        Assert.Equal(0.0, m[1], 6);
        Assert.Equal(0.0, m[2], 6);

        Assert.Equal(0.0, m[4], 6);
        Assert.Equal(3.0, m[5], 6);
        Assert.Equal(0.0, m[6], 6);

        Assert.Equal(0.0, m[8], 6);
        Assert.Equal(0.0, m[9], 6);
        Assert.Equal(4.0, m[10], 6);

        Assert.Equal(10.0, m[12], 6);
        Assert.Equal(20.0, m[13], 6);
        Assert.Equal(30.0, m[14], 6);
    }

    [Fact]
    public void Read_MatrixClass39_SmallUniformScale_PreservesScale()
    {
        byte[] bytes = GroupWithLeaf(1, w =>
        {
            w.New(39);
            w.Str("Matrix");
            w.Int(0).Int(0);
            w.Double(1e-10).Double(0).Double(0);
            w.Double(0).Double(1e-10).Double(0);
            w.Double(0).Double(0).Double(1e-10);
            w.Double(0).Double(0).Double(0);
            w.Int(0);
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        var leaf = nodes[2];
        Assert.NotNull(leaf.Transform);
        Assert.True(leaf.Transform.IsUniform);
        Assert.Equal(1e-10, leaf.Transform.Scale, 15);

        double[] m = leaf.Transform.ToMatrix4x4();
        Assert.Equal(1e-10, m[0], 15);
        Assert.Equal(1e-10, m[5], 15);
        Assert.Equal(1e-10, m[10], 15);
    }

    [Fact]
    public void Read_MatrixClass39_ZeroScale_PreservesZeroMatrix()
    {
        byte[] bytes = GroupWithLeaf(1, w =>
        {
            w.New(39);
            w.Str("Matrix");
            w.Int(0).Int(0);
            w.Double(0).Double(0).Double(0);
            w.Double(0).Double(0).Double(0);
            w.Double(0).Double(0).Double(0);
            w.Double(5).Double(6).Double(7);
            w.Int(0);
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        var leaf = nodes[2];
        Assert.NotNull(leaf.Transform);
        Assert.Equal(0.0, leaf.Transform.Scale);

        double[] m = leaf.Transform.ToMatrix4x4();
        Assert.Equal(0.0, m[0]);
        Assert.Equal(0.0, m[5]);
        Assert.Equal(0.0, m[10]);
        Assert.Equal(5.0, m[12]);
        Assert.Equal(6.0, m[13]);
        Assert.Equal(7.0, m[14]);
    }

    [Theory]
    [InlineData("mirror", new double[] { -2, 0, 0, 0, 2, 0, 0, 0, 2, 0, 0, 0 }, 1, -2.0, new double[] { -2, 2, 2 })]
    [InlineData("column-major Z+90", new double[] { 0, 1, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0 }, 0, 1.0, new double[] { 0, 0, 1 })]
    [InlineData("zero scale", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 0, 0.0, new double[] { 0, 0, 0 })]
    [InlineData("small uniform scale", new double[] { 1e-10, 0, 0, 0, 1e-10, 0, 0, 0, 1e-10, 0, 0, 0 }, 0, 1e-10, new double[] { 1e-10, 1e-10, 1e-10 })]
    [InlineData("non-uniform scale", new double[] { 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0 }, 0, 1.0, new double[] { 2, 3, 4 })]
    public void Read_MatrixClass39_ProbesMatrixScenarios(string name, double[] values, int reverses, double expectedScale, double[] expectedDiag)
    {
        byte[] bytes = GroupWithLeaf(1, w =>
        {
            w.New(39);
            w.Str("Matrix");
            w.Int(0).Int(0);
            foreach (double v in values)
            {
                w.Double(v);
            }
            w.Int(reverses);
        });

        var nodes = HierarchyParser.Read(bytes, "Model");
        var t = nodes[2].Transform;
        Assert.NotNull(t);
        Assert.Equal(expectedScale, t.Scale, 12);

        double[] m = t.ToMatrix4x4();
        Assert.Equal(expectedDiag[0], m[0], 6);
        Assert.Equal(expectedDiag[1], m[5], 6);
        Assert.Equal(expectedDiag[2], m[10], 6);

        if (name == "column-major Z+90")
        {
            Assert.True(Math.Abs(m[1] - 1.0) < 1e-12);
        }
    }
}
