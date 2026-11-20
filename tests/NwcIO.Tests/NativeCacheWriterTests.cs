using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class NativeCacheWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRoundTripKeepsTheOwnedTriangleAndItsPlacement(bool properties)
    {
        var owner = new NwcNode { Id = 2, Name = "Wall", Class = "LcRevitElement", IfcGlobalId = "0000000000000000000001" };
        var root = new NwcNode { Id = 1, Name = "model.rvt", Class = "LcRevitLayer" };
        root.Children.Add(owner);
        if (properties)
        {
            owner.Properties.Add(new() { FirstInt = 2, Tag = 52, Class = "Revit", Name = "Element ID", ValueType = 4, Value = "42" });
        }

        var mesh = new NwcMesh
        {
            Id = 1,
            Node = owner,
            Vertices = [new(0, 0, 0), new(0.3048, 0, 0), new(0, 0.3048, 0)],
            Indices = [0, 1, 2],
            Transform = new() { Id = 1, Translation = new(30.48, 0, 0) },
        };
        var document = NwcDocument.Create(
            fileInfo: new() { SourcePath = "model.rvt", LengthUnit = NwcLengthUnit.Meters },
            meshes: [mesh], rootNode: root);
        using var stream = new MemoryStream();
        NwcWriter.WriteNative(document, stream);
        stream.Position = 0;
        var reopened = NwcDocument.Open(stream, new() { Strict = true });
        Assert.Equal(NwcLengthUnit.Feet, reopened.LengthUnit);
        Assert.Equal("model.rvt", reopened.FileInfo.SourcePath);
        Assert.NotNull(reopened.DbCache?.Find("LcOpNwdGeometry"));
        var loaded = Assert.Single(reopened.Meshes);
        Assert.Equal(mesh.Indices, loaded.Indices);
        Assert.Equal(101, loaded.GetWorldVertices()[1].X, 9);
        Assert.Equal("Wall", loaded.Node?.Name);
        Assert.Equal(owner.IfcGlobalId, loaded.Node?.IfcGlobalId);
        if (properties)
        {
            Assert.Contains(loaded.Node!.Properties, p => p.InternalName == "LcRevitId" && p.Value == "42");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRoundTripKeepsLinePairsAndTheirOwner(bool withColors)
    {
        var root = new NwcNode { Id = 1, Name = "model.rvt", Class = "Model" };
        var owner = new NwcNode { Id = 2, Name = "Model Lines", Class = "LcRevitElement" };
        root.Children.Add(owner);
        var source = new NwcMesh
        {
            Id = 1,
            Node = owner,
            Vertices = [new(1, 0, 0), new(0, 2, 0), new(0, 0, 3)],
            Indices = [],
            LineIndices = Enumerable.Range(0, 17).SelectMany(i => new[] { i % 3, (i + 1) % 3 }).ToArray(),
            LineColors = withColors ? [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)] : null,
            Transform = new() { Id = 1, Translation = new(10, 20, 30) },
        };
        var document = NwcDocument.Create(
            fileInfo: new() { LengthUnit = NwcLengthUnit.Feet }, rootNode: root, meshes: [source]);
        using var stream = new MemoryStream();
        NwcWriter.WriteNative(document, stream);
        stream.Position = 0;
        var loaded = Assert.Single(NwcDocument.Open(stream, new() { Strict = true }).Meshes);
        Assert.Equal(owner.Name, loaded.Node?.Name);
        Assert.Empty(loaded.Indices);
        Assert.Equal(Enumerable.Range(0, 34), loaded.LineIndices);
        Assert.Equal(source.LineIndices.Select(i => source.Vertices[i] + new Vector3d(10, 20, 30)), loaded.GetWorldVertices());
        if (withColors)
            Assert.Equal(source.LineIndices.Select(i => source.LineColors![i]), loaded.LineColors);
        else
            Assert.Null(loaded.LineColors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidLineTopologyLeavesOutputEmpty(bool mixed)
    {
        var root = new NwcNode { Id = 1, Name = "model.rvt", Class = "Model" };
        var invalid = new NwcMesh
        {
            Id = 1,
            Node = root,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = mixed ? [0, 1, 2] : [],
            LineIndices = mixed ? [0, 1] : [0, 3],
        };
        var document = NwcDocument.Create(fileInfo: new() { LengthUnit = NwcLengthUnit.Feet }, rootNode: root, meshes: [invalid]);
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>(() => NwcWriter.WriteNative(document, stream));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void NativeRoundTripKeepsHiddenNodesAndTheirGeometry()
    {
        var root = new NwcNode { Id = 1, Name = "Model", Class = "Model" };
        var hidden = new NwcNode { Id = 2, Name = "Grid", Class = "LcRevitElement", IsHidden = true };
        var visible = new NwcNode { Id = 3, Name = "Model line", Class = "LcRevitElement" };
        root.Children.AddRange([hidden, visible]);
        var document = NwcDocument.Create(
            fileInfo: new() { LengthUnit = NwcLengthUnit.Feet },
            rootNode: root,
            meshes: [Line(1, hidden), Line(2, visible)]);
        using var stream = new MemoryStream();
        NwcWriter.WriteNative(document, stream);
        stream.Position = 0;

        var reopened = NwcDocument.Open(stream, new() { Strict = true });
        Assert.NotNull(reopened.DbCache?.Find("LcOpShadOverridesElement"));
        var meshes = reopened.Meshes;
        Assert.Equal(2, meshes.Count);
        Assert.True(Assert.Single(meshes, mesh => mesh.Node?.Name == "Grid").Node!.IsHidden);
        Assert.False(Assert.Single(meshes, mesh => mesh.Node?.Name == "Model line").Node!.IsHidden);
        Assert.All(meshes, mesh => Assert.Equal(2, mesh.LineIndices.Count));

        static NwcMesh Line(int id, NwcNode owner) => new()
        {
            Id = id,
            Node = owner,
            Vertices = [new(0, 0, 0), new(1, 0, 0)],
            Indices = [],
            LineIndices = [0, 1],
        };
    }

    [Fact]
    public void UnsupportedMeshLeavesOutputEmpty()
    {
        var root = new NwcNode { Id = 1, Name = "model.rvt", Class = "Model" };
        var invalid = new NwcMesh { Id = 1, Node = root, Vertices = [], Indices = [0, 1, 2] };
        var document = NwcDocument.Create(fileInfo: new() { LengthUnit = NwcLengthUnit.Feet }, rootNode: root, meshes: [invalid]);
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>(() => NwcWriter.WriteNative(document, stream));
        Assert.Equal(0, stream.Length);
    }
}
