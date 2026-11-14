using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class AppearanceMaterialTests
{
    private static readonly Look Grey = new(128 / 255f, 64 / 255f, 32 / 255f, 0.5f, 0.25f);

    [Fact]
    public void Open_LeafWithClass31_GetsANamedDecodedMaterial()
    {
        var doc = Open([new("G", "LcRevitLayer", Children: [new("L", "LcRevitSolid", Slots: [new HMat("Concrete")])])], [Quad(2, Grey)]);
        var m = doc.Meshes[0].Material!;
        Assert.Equal(("Concrete", 0, (byte)128, (byte)64, (byte)32, (byte)191, 0.7f, 0.1f), (m.Name, m.Id, m.Color.RByte, m.Color.GByte, m.Color.BByte, m.Color.AByte, m.Roughness, m.Metallic));
        Assert.Same(m, Assert.Single(doc.Materials));
        Assert.Null(doc.Meshes[0].Node!.Material);
    }

    [Fact]
    public void Open_NameComesFromTheNearestClass31()
    {
        var doc = Open([new("G", "LcRevitLayer", Slots: [new HMat("Brick")], Children: [
            new("A", "LcRevitSolid"), new("B", "LcRevitSolid", Slots: [new HMat(null)]), new("C", "LcRevitSolid", Slots: [new HMat("Glass")])])],
            [Quad(2, Grey), Quad(3), Quad(4)]);
        Assert.Equal(["Brick", "Default", "Glass"], doc.Meshes.Select(m => m.Material!.Name));
        Assert.Equal([0, 1, 2], doc.Materials.Select(m => m.Id));
    }

    [Fact]
    public void Open_SameLookAndName_IsOneMaterial()
    {
        var doc = Open([new("G", "LcRevitLayer", Children: [new("A", "LcRevitSolid"), new("B", "LcRevitSolid")])], [Quad(2, Grey), Quad(3)]);
        Assert.Same(doc.Meshes[0].Material, doc.Meshes[1].Material);
        Assert.Equal("Default", doc.Meshes[0].Material!.Name);
    }

    [Theory]
    [InlineData(0.5f, 128)]   // 127.5 rounds away from zero
    [InlineData(0.7f, 77)]    // (1 - 0.699999988) * 255 = 76.5000030
    [InlineData(0.9f, 26)]
    public void Open_Alpha_IsOneMinusTransparency(float transparency, int alpha) =>
        Assert.Equal(alpha, Open([new("G", "LcRevitLayer", Children: [new("A", "LcRevitSolid")])], [Quad(2, new Look(1, 1, 1, 0, transparency))]).Meshes[0].Material!.Color.AByte);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Open_WithoutTreeOrMaterials_HasNoMaterials(bool tree, bool materials)
    {
        var doc = Open([new("G", "LcRevitLayer", Children: [new("A", "LcRevitSolid")])], [Quad(2, Grey)], new NwcReaderOptions { BuildSceneTree = tree, ReadMaterials = materials });
        Assert.Empty(doc.Materials);
        Assert.Null(doc.Meshes[0].Material);
    }

    [Fact]
    public void Open_StrictFile_DoesNotAddHeuristicPresets()
    {
        var tab = new Tab(null, "Material", "LcRevitMaterialProperties", new Prop("Name", "LcRevitPropertyElementName", 4, "Rebar"));
        Assert.Single(Open([new("G", "LcRevitLayer", Children: [new("A", "LcRevitSolid", Slots: [new HTab(tab)])])], [Quad(2, Grey)]).Materials);
    }

    [Theory]   // with or without the tree and ReadMaterials, also in a float the reader drops
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]   // NaN in the shininess
    public void Open_NonFiniteClass54Value_Throws(bool tree, bool materials, bool unusedFloat)
    {
        var look = unusedFloat ? new Look(0, 0, 0, float.NaN, 0) : new Look(float.NaN, 0, 0, 0, 0);
        var ex = Assert.Throws<InvalidDataException>(() => Open([new("G", "LcRevitLayer", Children: [new("A", "LcRevitSolid")])], [Quad(2, look)],
            new NwcReaderOptions { BuildSceneTree = tree, ReadMaterials = materials }));
        Assert.EndsWith(": class-54 value is not finite", ex.Message);
    }

    private static NwcDocument Open(TreeNode[] top, Frag[] frags, NwcReaderOptions? o = null) => NwcDocument.Open(
        new MemoryStream(SyntheticNwcFile.Build(SyntheticTree.Write(1, top).Hierarchy, SyntheticFragments.Build(frags), [FormatVectors.R1], [120])), o);

    private static Frag Quad(int seq, Look? look = null, int sharesLookOf = 0) => new(seq, Vector3d.Zero, 1, 2, 4, Rec: 1, Look: look, SharesLookOf: sharesLookOf);
}
