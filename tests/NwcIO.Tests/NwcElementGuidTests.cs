using NwcIO.Internal;
using NwcIO.Internal.Export;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class NwcElementGuidTests
{
    [Fact]
    public void ForMesh_SameElementWithSeveralFragments_ProducesUniqueIds()
    {
        var element = new NwcNode { Id = 1, Name = "Door", Class = "LcRevitData_Element", ElementGuid = Guid.NewGuid() };
        var leaf1 = new NwcNode { Id = 2, Name = "Panel", Class = "LcRevitData_Element", Element = element };
        var leaf2 = new NwcNode { Id = 3, Name = "Frame", Class = "LcRevitData_Element", Element = element };
        var mesh1 = new NwcMesh { Id = 10, Node = leaf1, Vertices = [], Indices = [] };
        var mesh2 = new NwcMesh { Id = 11, Node = leaf2, Vertices = [], Indices = [] };
        var doc = NwcDocument.Create();

        Assert.NotEqual(NwcElementGuid.ForMesh(mesh1, doc), NwcElementGuid.ForMesh(mesh2, doc));
    }

    [Fact]
    public void ForMesh_AuthoringGuid_IsPartOfTheKey_AndEmptyGuidFallsBackToTheMeshKey()
    {
        static NwcMesh MeshOf(Guid guid) => new()
        {
            Id = 10,
            Node = new NwcNode { Id = 2, Name = "Panel", Class = "LcRevitData_Element", ElementGuid = guid },
            Vertices = [],
            Indices = [],
        };

        var guid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal(IfcGuid.FromKey($"Model.rvt:element:{guid:D}:mesh:10"), NwcElementGuid.ForMesh(MeshOf(guid), "Model.rvt"));
        Assert.Equal(IfcGuid.FromKey("Model.rvt:mesh:10"), NwcElementGuid.ForMesh(MeshOf(Guid.Empty), "Model.rvt"));
    }

    [Fact]
    public void FindClashes_SameSourcePathAndMeshId_UsesDistinctGlobalIds()
    {
        static NwcMesh Mesh(double x) => new()
        {
            Id = 1,
            Vertices = [new(x, 0, 0), new(x + 1, 0, 0), new(x, 1, 0)],
            Indices = [0, 1, 2],
        };

        var first = NwcDocument.Create(fileInfo: new NwcFileInfo { SourcePath = "tower.rvt" }, meshes: [Mesh(0)]);
        var second = NwcDocument.Create(fileInfo: new NwcFileInfo { SourcePath = "tower.rvt" }, meshes: [Mesh(0.5)]);
        var clash = Assert.Single(NwcAssembly.Create("fed", first, second).FindClashes());

        Assert.NotEqual(clash.IfcGuidA, clash.IfcGuidB);
    }

    [Fact]
    public void ModelKeys_DistinguishEqualSourcePathsOnlyWhenNeeded()
    {
        var first = NwcDocument.Create(fileInfo: new NwcFileInfo { SourcePath = "first.rvt" });
        var second = NwcDocument.Create(fileInfo: new NwcFileInfo { SourcePath = "second.rvt" });
        Assert.Equal(["first.rvt", "second.rvt"], NwcElementGuid.ModelKeys([first, second]));

        var sameA = NwcDocument.Create(fileInfo: new NwcFileInfo { SourcePath = "tower.rvt" });
        var sameB = NwcDocument.Create(fileInfo: new NwcFileInfo { SourcePath = "tower.rvt" });
        var keys = NwcElementGuid.ModelKeys([sameA, sameB]);
        Assert.Equal(["tower.rvt#0", "tower.rvt#1"], keys);
        Assert.NotEqual(
            NwcElementGuid.ForMesh(new NwcMesh { Id = 5, Vertices = [], Indices = [] }, keys[0]),
            NwcElementGuid.ForMesh(new NwcMesh { Id = 5, Vertices = [], Indices = [] }, keys[1]));
    }
}

