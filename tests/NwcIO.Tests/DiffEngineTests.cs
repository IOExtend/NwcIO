using System.IO.Compression;
using System.Text.Json;
using NwcIO.Model;
using NwcIO.Model.Diff;
using Xunit;

namespace NwcIO.Tests;

public sealed class DiffEngineTests
{
    private static NwcMesh CreateBoxMesh(int id, string name, Vector3d offset, double size = 1.0)
    {
        var verts = new List<Vector3d>
        {
            offset + new Vector3d(0, 0, 0),
            offset + new Vector3d(size, 0, 0),
            offset + new Vector3d(size, size, 0),
            offset + new Vector3d(0, size, 0),
            offset + new Vector3d(0, 0, size),
            offset + new Vector3d(size, 0, size),
            offset + new Vector3d(size, size, size),
            offset + new Vector3d(0, size, size),
        };

        var indices = new List<int>
        {
            // Bottom
            0, 1, 2,  0, 2, 3,
            // Top
            4, 6, 5,  4, 7, 6,
            // Front
            0, 5, 1,  0, 4, 5,
            // Back
            2, 7, 3,  2, 6, 7,
            // Left
            0, 3, 7,  0, 7, 4,
            // Right
            1, 5, 6,  1, 6, 2,
        };

        return new NwcMesh
        {
            Id = id,
            Name = name,
            Vertices = verts,
            Indices = indices,
            BoundingBox = new BoundingBox3d(offset, offset + new Vector3d(size, size, size)),
        };
    }

    [Fact]
    public void Compare_DetectsAddedDeletedAndUnchangedElements()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var guid3 = Guid.NewGuid();

        var rootBase = new NwcNode { Id = 1, Name = "Root", Class = "Root" };
        var node1Base = new NwcNode
        {
            Id = 2,
            Name = "Column-1",
            Class = "LcRevitData_Element",
            ElementGuid = guid1,
            Mesh = CreateBoxMesh(10, "Col1", new Vector3d(0, 0, 0)),
        };
        var node2Base = new NwcNode
        {
            Id = 3,
            Name = "Wall-Deleted",
            Class = "LcRevitData_Element",
            ElementGuid = guid2,
            Mesh = CreateBoxMesh(20, "WallDel", new Vector3d(10, 0, 0)),
        };
        rootBase.Children.Add(node1Base);
        rootBase.Children.Add(node2Base);

        var docBase = NwcDocument.Create(rootNode: rootBase, meshes: [node1Base.Mesh!, node2Base.Mesh!]);

        var rootRev = new NwcNode { Id = 1, Name = "Root", Class = "Root" };
        var node1Rev = new NwcNode
        {
            Id = 2,
            Name = "Column-1",
            Class = "LcRevitData_Element",
            ElementGuid = guid1,
            Mesh = CreateBoxMesh(10, "Col1", new Vector3d(0, 0, 0)),
        };
        var node3Rev = new NwcNode
        {
            Id = 4,
            Name = "Beam-Added",
            Class = "LcRevitData_Element",
            ElementGuid = guid3,
            Mesh = CreateBoxMesh(30, "BeamAdd", new Vector3d(5, 5, 3)),
        };
        rootRev.Children.Add(node1Rev);
        rootRev.Children.Add(node3Rev);

        var docRev = NwcDocument.Create(rootNode: rootRev, meshes: [node1Rev.Mesh!, node3Rev.Mesh!]);

        var report = docBase.Compare(docRev);

        Assert.Single(report.Added);
        Assert.Equal("Beam-Added", report.Added[0].Name);
        Assert.Equal(guid3, report.Added[0].ElementGuid);

        Assert.Single(report.Deleted);
        Assert.Equal("Wall-Deleted", report.Deleted[0].Name);
        Assert.Equal(guid2, report.Deleted[0].ElementGuid);

        Assert.Single(report.Unchanged);
        Assert.Equal("Column-1", report.Unchanged[0].Name);
        Assert.Empty(report.Modified);
        Assert.Equal(2, report.TotalChangesCount);
    }

    [Fact]
    public void Compare_DetectsGeometricShiftAndPropertyModifications()
    {
        var guid = Guid.NewGuid();

        var nodeBase = new NwcNode
        {
            Id = 10,
            Name = "Equipment-101",
            Class = "LcRevitData_Element",
            ElementGuid = guid,
            Mesh = CreateBoxMesh(1, "Eq1", new Vector3d(0, 0, 0)),
        };
        nodeBase.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Name = "Cost", Class = "Identity Data", Value = "100" });
        nodeBase.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 2, Name = "Material", Class = "Materials", Value = "Steel" });

        var docBase = NwcDocument.Create(rootNode: nodeBase, meshes: [nodeBase.Mesh!]);

        // Revision shifted by 0.5m in X and property changes
        var nodeRev = new NwcNode
        {
            Id = 10,
            Name = "Equipment-101",
            Class = "LcRevitData_Element",
            ElementGuid = guid,
            Mesh = CreateBoxMesh(1, "Eq1", new Vector3d(0.5, 0, 0)),
        };
        nodeRev.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Name = "Cost", Class = "Identity Data", Value = "150" }); // modified
        nodeRev.Properties.Add(new NwcPropertyRecord { FirstInt = 3, Tag = 3, Name = "Supplier", Class = "Identity Data", Value = "Acme" }); // added, Material removed

        var docRev = NwcDocument.Create(rootNode: nodeRev, meshes: [nodeRev.Mesh!]);

        var report = docBase.Compare(docRev);

        Assert.Single(report.Modified);
        var mod = report.Modified[0];
        Assert.True(mod.GeometryChanged);
        Assert.True(mod.TranslationDelta.X > 0.4);
        Assert.Equal(3, mod.PropertyChanges.Count);

        var costChange = mod.PropertyChanges.First(p => p.PropertyName == "Cost");
        Assert.Equal("100", costChange.OldValue);
        Assert.Equal("150", costChange.NewValue);
        Assert.Equal("Modified", costChange.ChangeType);

        var matChange = mod.PropertyChanges.First(p => p.PropertyName == "Material");
        Assert.Equal("Removed", matChange.ChangeType);

        var supChange = mod.PropertyChanges.First(p => p.PropertyName == "Supplier");
        Assert.Equal("Added", supChange.ChangeType);
    }

    [Fact]
    public void SaveJson_SerializesCorrectSchema()
    {
        var guid = Guid.NewGuid();
        var report = new NwcDiffReport("RevA", "RevB");
        report.Added.Add(new NwcElementDiff
        {
            NodeId = 1,
            Name = "NewPipe",
            ClassName = "LcPipe",
            ElementGuid = guid,
            ChangeKind = NwcChangeKind.Added,
        });

        string json = report.ToJson(indented: true);
        Assert.Contains("RevA", json);
        Assert.Contains("RevB", json);
        Assert.Contains("NewPipe", json);
        Assert.Contains(guid.ToString("D"), json);

        using var jdoc = JsonDocument.Parse(json);
        Assert.Equal(1, jdoc.RootElement.GetProperty("summary").GetProperty("added").GetInt32());
    }

    [Fact]
    public void SaveBcf_ProducesValidBcf21Archive()
    {
        var guid = Guid.NewGuid();
        var report = new NwcDiffReport("ModelA", "ModelB");
        report.Modified.Add(new NwcElementDiff
        {
            NodeId = 42,
            Name = "AirHandlingUnit",
            ClassName = "LcRevitData_Element",
            ElementGuid = guid,
            ChangeKind = NwcChangeKind.Modified,
            GeometryChanged = true,
            TranslationDelta = new Vector3d(0.2, 0, 0),
        });

        using var ms = new MemoryStream();
        report.SaveBcf(ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("bcf.version"));
        Assert.NotNull(zip.GetEntry("project.bcfp"));

        var markupEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("markup.bcf", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(markupEntry);

        using var reader = new StreamReader(markupEntry.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("AirHandlingUnit", xml);
        Assert.Contains("NwcDiffEngine", xml);

        var vpEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("viewpoint.bcfv", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(vpEntry);
    }

    [Fact]
    public void FederatedAssembly_CompareProducesReport()
    {
        var mesh1 = CreateBoxMesh(1, "Mesh1", new Vector3d(0, 0, 0));
        var doc1 = NwcDocument.Create(meshes: [mesh1]);
        var assemA = NwcAssembly.Create("FederationA", doc1);

        var mesh2 = CreateBoxMesh(1, "Mesh1", new Vector3d(0, 0, 0));
        var mesh3 = CreateBoxMesh(2, "MeshNew", new Vector3d(10, 10, 10));
        var doc2 = NwcDocument.Create(meshes: [mesh2, mesh3]);
        var assemB = NwcAssembly.Create("FederationB", doc2);

        var report = assemA.Compare(assemB);
        Assert.Single(report.Added);
        Assert.Equal("MeshNew", report.Added[0].Name);
        Assert.Single(report.Unchanged);
    }

    private static NwcDocument CreateSharedGuidDocument(Guid guid, int count)
    {
        var root = new NwcNode { Id = 1, Name = "Root", Class = "Root" };
        for (int i = 0; i < count; i++)
        {
            root.Children.Add(new NwcNode { Id = 2 + i, Name = "Part", Class = "LcRevitData_Element", ElementGuid = guid });
        }

        return NwcDocument.Create(rootNode: root);
    }

    private static NwcDocument CreateWallDocument(string sourcePath, double x, Guid? guid)
    {
        var mesh = CreateBoxMesh(1, "Wall", new Vector3d(x, 0, 0));
        return NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = sourcePath },
            meshes: [mesh],
            rootNode: guid.HasValue
                ? new NwcNode { Id = 1, Name = "Wall", Class = "LcRevitData_Element", ElementGuid = guid, Mesh = mesh }
                : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_FirstAssemblyModelRemoved_MatchesContributingModel(bool sharedGuid)
    {
        Guid? guid = sharedGuid ? Guid.NewGuid() : null;
        var docA = CreateWallDocument("A.rvt", 0, guid);
        var docB = CreateWallDocument("B.rvt", 100, guid);
        var baseline = NwcAssembly.Create("Baseline", docA, docB);
        var revision = NwcAssembly.Create("Revision", CreateWallDocument("B.rvt", 100, guid));

        var report = baseline.Compare(revision);

        Assert.Empty(report.Added);
        Assert.Empty(report.Modified);
        Assert.Same(docB.Meshes[0], Assert.Single(report.Unchanged).BaselineMesh);
        Assert.Same(docA.Meshes[0], Assert.Single(report.Deleted).BaselineMesh);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compare_AssemblyModelsReordered_MatchesContributingModel(bool sharedGuid)
    {
        Guid? guid = sharedGuid ? Guid.NewGuid() : null;
        var docA = CreateWallDocument("A.rvt", 0, guid);
        var docB = CreateWallDocument("B.rvt", 100, guid);
        var baseline = NwcAssembly.Create("Baseline", docA, docB);
        var revision = NwcAssembly.Create("Revision",
            CreateWallDocument("B.rvt", 100, guid), CreateWallDocument("A.rvt", 0, guid));

        var report = baseline.Compare(revision);

        Assert.Empty(report.Added);
        Assert.Empty(report.Deleted);
        Assert.Empty(report.Modified);
        Assert.Equal(2, report.Unchanged.Count);
        Assert.Same(docB.Meshes[0], report.Unchanged[0].BaselineMesh);
        Assert.Same(docA.Meshes[0], report.Unchanged[1].BaselineMesh);
    }

    [Fact]
    public void Compare_DuplicateGuidsInDocument_MatchesEachElementOnce()
    {
        var guid = Guid.NewGuid();

        var report = CreateSharedGuidDocument(guid, 3).Compare(CreateSharedGuidDocument(guid, 3));

        Assert.Empty(report.Added);
        Assert.Empty(report.Deleted);
        Assert.Empty(report.Modified);
        Assert.Equal(3, report.Unchanged.Count);
        Assert.Equal([2, 3, 4], report.Unchanged.Select(d => d.BaselineNode!.Id).Order());
    }

    [Fact]
    public void Compare_DuplicateGuidRemovedFromRevision_ReportsOneDeletion()
    {
        var guid = Guid.NewGuid();

        var report = CreateSharedGuidDocument(guid, 3).Compare(CreateSharedGuidDocument(guid, 2));

        Assert.Empty(report.Added);
        Assert.Single(report.Deleted);
        Assert.Equal(2, report.Unchanged.Count);
    }

    [Fact]
    public void Compare_GuidSharedAcrossAssemblyModels_MatchesEachElementOnce()
    {
        var guid = Guid.NewGuid();
        var baseline = NwcAssembly.Create("A", CreateSharedGuidDocument(guid, 1), CreateSharedGuidDocument(guid, 1));
        var revision = NwcAssembly.Create("B", CreateSharedGuidDocument(guid, 1), CreateSharedGuidDocument(guid, 1));

        var report = baseline.Compare(revision);

        Assert.Empty(report.Added);
        Assert.Empty(report.Deleted);
        Assert.Equal(2, report.Unchanged.Count);
    }

    [Fact]
    public void Compare_SameMeshNameAcrossAssemblyModels_MatchesEachElementOnce()
    {
        var baseline = NwcAssembly.Create("A",
            NwcDocument.Create(meshes: [CreateBoxMesh(1, "Wall", new Vector3d(0, 0, 0))]),
            NwcDocument.Create(meshes: [CreateBoxMesh(1, "Wall", new Vector3d(0, 0, 0))]));
        var revision = NwcAssembly.Create("B",
            NwcDocument.Create(meshes: [CreateBoxMesh(1, "Wall", new Vector3d(0, 0, 0))]),
            NwcDocument.Create(meshes: [CreateBoxMesh(1, "Wall", new Vector3d(0, 0, 0))]));

        var report = baseline.Compare(revision);

        Assert.Empty(report.Added);
        Assert.Empty(report.Deleted);
        Assert.Equal(2, report.Unchanged.Count);
    }
}
