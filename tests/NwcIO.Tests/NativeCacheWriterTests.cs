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
