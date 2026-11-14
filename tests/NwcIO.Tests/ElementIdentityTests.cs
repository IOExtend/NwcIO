using System.Buffers.Binary;
using System.Globalization;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class ElementIdentityTests
{
    private static readonly Guid G1 = Guid.Parse("7b0c5a4e-3f2d-4b1a-9c8e-6d5f4e3ef9b1");
    private static readonly Guid G2 = Guid.Parse("11111111-2222-4333-8444-555555555555");

    [Fact]
    public void Open_ElementNodes_TakeTheirGuidFromTheTable()
    {
        var doc = Open([new("Layer", "LcRevitLayer", Children: [new("E1", "LcRevitInstance", X: 1), new("E2", "LcRevitInstance", X: 2)])], [G1, G2]);
        var layer = doc.RootNode!.Children[0];
        var (e1, e2) = (layer.Children[0], layer.Children[1]);
        Assert.Equal([G1, G2], doc.Guids);
        Assert.True(e1.IsElement); Assert.Equal(G1, e1.ElementGuid); Assert.Same(e1, e1.Element);
        Assert.Equal(G2, e2.ElementGuid);
        Assert.False(layer.IsElement); Assert.Null(layer.Element); Assert.Null(layer.ElementGuid);
    }

    [Fact]
    public void Open_PartOfAnElement_PointsToTheElement()
    {
        var doc = Open([new("Layer", "LcRevitLayer", Children: [new("E", "LcRevitComposite", X: 1, Children: [new("Solid", "LcRevitSolid")])])], [G1]);
        var e = doc.RootNode!.Children[0].Children[0];
        var part = e.Children[0];
        Assert.False(part.IsElement); Assert.Same(e, part.Element); Assert.Null(part.ElementGuid);
    }

    [Fact]
    public void Open_SiblingPairWithOneIndex_FirstIsTheElement()
    {
        var doc = Open([new("Type", "LcRevitCollection", Children: [
            new("Door", "LcRevitComposite", X: 1, Children: [new("Panel", "LcRevitSolid")]),
            new("Door", "LcRevitInstance", X: 1)])], [G1]);
        var type = doc.RootNode!.Children[0];
        var (first, second) = (type.Children[0], type.Children[1]);
        Assert.True(first.IsElement); Assert.Equal(G1, first.ElementGuid);
        Assert.False(second.IsElement); Assert.Null(second.ElementGuid); Assert.Same(first, second.Element);
        Assert.Same(first, first.Children[0].Element);
    }

    [Fact]
    public void Open_ChildOfAPairsSecondNode_PointsToTheFirst()
    {
        var doc = Open([new("Type", "LcRevitCollection", Children: [
            new("Door", "LcRevitComposite", X: 1),
            new("Door", "LcRevitComposite", X: 1, Children: [new("Panel", "LcRevitSolid")])])], [G1]);
        var type = doc.RootNode!.Children[0];
        var (first, panel) = (type.Children[0], type.Children[1].Children[0]);
        Assert.Same(first, panel.Element);
        Assert.False(panel.IsElement); Assert.Null(panel.ElementGuid);
    }

    [Fact]
    public void Open_OneIndexUnderDifferentParents_ThrowsRevitLinks()
    {
        var ex = Assert.Throws<NotSupportedException>(() => Open([
            new("A", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 1)]),
            new("B", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 1)])], [G1]));
        Assert.StartsWith("Revit links are not supported yet", ex.Message);
    }

    [Fact]
    public void Open_ElementInASharedSubtree_ThrowsRevitLinks()
    {
        var shared = new TreeNode("S", "LcRevitCollection", Children: [new("E", "LcRevitInstance", X: 1)]);
        Assert.Throws<NotSupportedException>(() => Open([new("A", "LcRevitLayer", Children: [shared]), new("B", "LcRevitLayer", Children: [shared])], [G1]));
    }

    [Fact]   // design 6.2.1 made the outer node the element here; never seen in the corpus, so the plan rejects it (Decision 3)
    public void Open_OneIndexOnANodeAndItsDescendant_ThrowsRevitLinks() =>
        Assert.Throws<NotSupportedException>(() => Open([new("L", "LcRevitLayer", Children: [
            new("E", "LcRevitComposite", X: 1, Children: [new("P", "LcRevitInstance", X: 1)])])], [G1]));

    [Fact]   // 309 of the 334 real pairs have siblings between them; a pair takes two equal table entries, the next index is X + 2
    public void Open_SiblingPairWithASiblingBetween_StillPairs()
    {
        var doc = Open([new("Type", "LcRevitCollection", Children: [
            new("Grid", "LcRevitComposite", X: 1), new("Other", "LcRevitInstance", X: 3), new("Grid", "LcRevitComposite", X: 1)])], [G1, G1, G2]);
        var type = doc.RootNode!.Children[0];
        Assert.Same(type.Children[0], type.Children[2].Element);
        Assert.False(type.Children[2].IsElement);
        Assert.Equal(G2, type.Children[1].ElementGuid);
    }

    [Theory]
    [InlineData(2, "element index 3 is outside the GUID table of 2")]
    [InlineData(0, "element index 3 is outside the GUID table of 0")]
    public void Open_IndexOutsideTheTable_Throws(int guidCount, string rule)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Open([new("L", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 3)])], [.. new[] { G1, G2 }.Take(guidCount)]));
        Assert.StartsWith("LcOpNwdLogicalHierarchy at byte ", ex.Message);
        Assert.EndsWith(": " + rule, ex.Message);
    }

    [Theory]
    [InlineData("head", "LcOpGuidStore: head a is 2, not 1")]
    [InlineData("length", "LcOpGuidStore: 2 GUIDs do not fill 24 bytes")]
    public void Open_BrokenGuidStore_Throws(string defect, string message)
    {
        byte[] store = SyntheticNwcFile.GuidStore(G1);
        store = defect == "head" ? FormatVectors.Patched(store, 0, BitConverter.GetBytes(2)) : FormatVectors.Patched(store, 4, BitConverter.GetBytes(2));
        var file = SyntheticNwcFile.Build(SyntheticTree.Write(1, new TreeNode("L", "LcRevitLayer", Children: [])).Hierarchy, null, [], [], extraStreams: [("LcOpGuidStore", store)]);
        Assert.Equal(message, Assert.Throws<InvalidDataException>(() => NwcDocument.Open(new MemoryStream(file))).Message);
    }

    [Fact]
    public void Open_ReadGuidsOff_StillFillsElementGuid()
    {
        var doc = Open([new("L", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 1)])], [G1], new NwcReaderOptions { ReadGuids = false });
        Assert.Empty(doc.Guids);
        Assert.Equal(G1, doc.RootNode!.Children[0].Children[0].ElementGuid);
    }

    // RvtSceneReader.IfcGuidOf (RvtIO.Scene): the session GUID of a UniqueId XORed with its element id, in the IFC base-64 form.
    private static string RvtIfcGuidOf(string uniqueId)
    {
        const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";
        var episode = Guid.ParseExact(uniqueId.AsSpan(0, 36), "D");
        uint id = uint.Parse(uniqueId.AsSpan(37), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        UInt128 bits = BinaryPrimitives.ReadUInt128BigEndian(episode.ToByteArray(bigEndian: true)) ^ id;
        var text = new char[22];
        for (int i = 21; i >= 0; i--, bits >>= 6) text[i] = Alphabet[(int)(bits & 63)];
        return new string(text);
    }

    [Fact]
    public void Open_ElementWithoutIfcGuidParameter_UsesTheExportGuid()
    {
        var e = Open([new("L", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 1)])], [G1]).RootNode!.Children[0].Children[0];
        Assert.Equal("1x35fEForB6foERLzEFlcn", e.IfcGlobalId);
        Assert.Equal(RvtIfcGuidOf("7b0c5a4e-3f2d-4b1a-9c8e-6d5f4e3a2b10-0004d2a1"), e.IfcGlobalId);   // the UniqueId whose export GUID is G1
    }

    [Theory]
    [InlineData("LcRevitData_Element", "3Ax9$kQwz0Bf8cYp2LmN4r", "3Ax9$kQwz0Bf8cYp2LmN4r")]
    [InlineData("LcRevitData_Element", "4Ax9$kQwz0Bf8cYp2LmN4r", "1x35fEForB6foERLzEFlcn")]   // first character above 3
    [InlineData("LcRevitData_Element", "3Ax9$kQwz0Bf8cYp2LmN4", "1x35fEForB6foERLzEFlcn")]    // 21 characters
    [InlineData("LcRevitData_Parameter", "3Ax9$kQwz0Bf8cYp2LmN4r", "1x35fEForB6foERLzEFlcn")] // a referenced element's tab
    public void Open_IfcGuidParameter_WinsWhenValid(string tab, string stored, string expected) =>
        Assert.Equal(expected, IfcGlobalIdWith(tab, stored, null));

    [Theory]
    [InlineData("lcldrevit_parameter_-1019001", 4, "3Ax9$kQwz0Bf8cYp2LmN4r")]   // another parameter
    [InlineData("lcldrevit_parameter_-1019000", 1, 1.0)]                         // a number, not text
    [InlineData("lcldrevit_parameter_-1019000", 9, "3Ax9$kQwz0Bf8cYp2LmN4r")]   // valid text of value type 9, not 4
    [InlineData("lcldrevit_parameter_-1019000", 4, "3Ax9$kQwz0Bf8cYp2LmN4!")]   // 22 characters, the last outside the IFC alphabet
    public void Open_NoValidIfcGuidParameter_UsesTheExportGuid(string internalName, int type, object stored) =>
        Assert.Equal("1x35fEForB6foERLzEFlcn", IfcGlobalIdWith("LcRevitData_Element", stored, null, internalName, type));

    [Fact]
    public void Open_PropertiesOff_StillReadsTheIfcGuidParameter() =>
        Assert.Equal("3Ax9$kQwz0Bf8cYp2LmN4r", IfcGlobalIdWith("LcRevitData_Element", "3Ax9$kQwz0Bf8cYp2LmN4r", new NwcReaderOptions { ReadPropertyRecords = false }));

    private static string? IfcGlobalIdWith(string tab, object stored, NwcReaderOptions? options, string internalName = "lcldrevit_parameter_-1019000", int type = 4)
    {
        var t = new Tab(null, "Tab", tab, new Prop("IfcGUID", internalName, type, stored));
        var (h, p) = SyntheticTree.Write(1, new TreeNode("L", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 1, Slots: [new PTab(t)])]));
        var file = SyntheticNwcFile.Build(h, null, [], [], extraStreams: [("LcOpGuidStore", SyntheticNwcFile.GuidStore(G1))], partitionProps: p);
        return NwcDocument.Open(new MemoryStream(file), options).RootNode!.Children[0].Children[0].IfcGlobalId;
    }

    [Fact]
    public void Open_NonElementNodes_HaveNoIfcGlobalId() =>
        Assert.Null(Open([new("L", "LcRevitLayer", Children: [new("E", "LcRevitInstance", X: 1)])], [G1]).RootNode!.Children[0].IfcGlobalId);

    private static NwcDocument Open(TreeNode[] top, Guid[] guids, NwcReaderOptions? o = null) =>
        NwcDocument.Open(new MemoryStream(SyntheticNwcFile.Build(SyntheticTree.Write(1, top).Hierarchy, null, [], [],
            extraStreams: [("LcOpGuidStore", SyntheticNwcFile.GuidStore(guids))])), o);
}
