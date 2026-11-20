using NwcIO.Internal.Export;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class ObjExportTests
{
    [Theory]
    [InlineData("document")]
    [InlineData("async")]
    [InlineData("assembly")]
    public async Task SaveObj_WritesAndReferencesSiblingMaterialLibrary(string entryPoint)
    {
        var directory = Directory.CreateTempSubdirectory("nwcio-obj-materials-");
        try
        {
            var glass = new NwcMaterial { Id = 1, Name = "Shared\nName", Color = new(0.25f, 0.5f, 0.75f, 0.5f) };
            var paint = new NwcMaterial { Id = 1, Name = glass.Name, Color = new(1, 0.25f, 0) };
            var first = NwcDocument.Create(meshes: [Triangle(1, glass), Triangle(2, null), Triangle(3, glass)]);
            var second = NwcDocument.Create(meshes: [Triangle(4, paint)]);
            string path = Path.Combine(directory.FullName, "model with spaces.obj");
            var document = NwcDocument.Create(meshes: [.. first.Meshes, .. second.Meshes]);
            switch (entryPoint)
            {
                case "async": await document.SaveObjAsync(path); break;
                case "assembly": NwcAssembly.Create("Federation", first, second).SaveObj(path); break;
                default: document.SaveObj(path); break;
            }

            var obj = File.ReadAllLines(path);
            Assert.Equal("mtllib model with spaces.mtl", Assert.Single(obj, line => line.StartsWith("mtllib ", StringComparison.Ordinal)));
            Assert.Equal(["usemtl nwc_1", "f 1 2 3", "usemtl nwc_default", "f 4 5 6", "usemtl nwc_1", "f 7 8 9", "usemtl nwc_2", "f 10 11 12"],
                obj.Where(line => line.StartsWith("f ", StringComparison.Ordinal) || line.StartsWith("usemtl ", StringComparison.Ordinal)));
            var mtl = File.ReadAllLines(Path.ChangeExtension(path, ".mtl"));
            Assert.Equal(["newmtl nwc_1", "Kd 0.25 0.5 0.75", "d 0.5", "newmtl nwc_2", "Kd 1 0.25 0", "d 1", "newmtl nwc_default", "Kd 1 1 1", "d 1"],
                mtl.Where(line => line.Length != 0));

            using var writer = new StringWriter();
            document.SaveObj(writer);
            Assert.DoesNotContain("mtllib ", writer.ToString());
            Assert.DoesNotContain("usemtl ", writer.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveObj_FailedObjReplacement_RollsBackMaterialLibrary(bool existingLibrary)
    {
        var directory = Directory.CreateTempSubdirectory("nwcio-obj-rollback-");
        try
        {
            string path = Directory.CreateDirectory(Path.Combine(directory.FullName, "model.obj")).FullName;
            string library = Path.ChangeExtension(path, ".mtl");
            if (existingLibrary) File.WriteAllText(library, "original materials");
            var document = NwcDocument.Create(meshes: [Triangle(1, new NwcMaterial { Id = 1, Name = "Glass", Color = RgbaColor.Glass })]);

            var failure = Record.Exception(() => document.SaveObj(path));

            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.True(Directory.Exists(path));
            if (existingLibrary)
            {
                Assert.Equal(library, Assert.Single(Directory.GetFiles(directory.FullName)));
                Assert.Equal("original materials", File.ReadAllText(library));
            }
            else
            {
                Assert.Empty(Directory.GetFiles(directory.FullName));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SaveObj_RejectsLinkedMaterialPathAndPreservesExistingFiles()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("nwcio-obj-linked-material-");
        try
        {
            string path = Path.Combine(directory.FullName, "model.obj");
            string library = Path.ChangeExtension(path, ".mtl");
            string otherFile = Path.Combine(directory.FullName, "untouched.txt");
            File.WriteAllText(path, "original geometry");
            File.WriteAllText(otherFile, "original target");
            File.CreateSymbolicLink(library, otherFile);
            var document = NwcDocument.Create(meshes: [Triangle(1, new NwcMaterial { Id = 1, Name = "Glass", Color = RgbaColor.Glass })]);

            Assert.Throws<IOException>(() => document.SaveObj(path));

            Assert.Equal("original geometry", File.ReadAllText(path));
            Assert.Equal("original target", File.ReadAllText(otherFile));
            Assert.Equal(otherFile, new FileInfo(library).LinkTarget);
            Assert.Equal(3, Directory.GetFiles(directory.FullName).Length);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveObjAsync_ConcurrentSaves_KeepGeometryAndMaterialFromSameDocument()
    {
        var directory = Directory.CreateTempSubdirectory("nwcio-obj-concurrent-");
        try
        {
            string path = Path.Combine(directory.FullName, "model.obj");
            var black = NwcDocument.Create(meshes: [Triangle(1, new NwcMaterial { Id = 1, Name = "Black", Color = RgbaColor.Black })]);
            var white = NwcDocument.Create(meshes: [Triangle(2, new NwcMaterial { Id = 1, Name = "White", Color = RgbaColor.White })]);

            await Task.WhenAll(Enumerable.Range(0, 6).Select(i => (i % 2 == 0 ? black : white).SaveObjAsync(path)));

            string name = Assert.Single(File.ReadLines(path), line => line.StartsWith("o ", StringComparison.Ordinal));
            Assert.True(name is "o Mesh_1" or "o Mesh_2", name);
            string diffuse = name == "o Mesh_1" ? "Kd 0 0 0" : "Kd 1 1 1";
            Assert.Contains(diffuse, File.ReadAllLines(Path.ChangeExtension(path, ".mtl")));
            Assert.Equal(2, Directory.GetFiles(directory.FullName).Length);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static NwcMesh Triangle(int id, NwcMaterial? material) => new()
    {
        Id = id,
        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        Indices = [0, 1, 2],
        Material = material,
    };

    [Theory]
    [InlineData(true, "f 1 3 2", "f 3 4 2")]
    [InlineData(false, "f 1 2 3", "f 3 2 4")]
    public void Write_MirroredTransform_ReversesOnlyWorldFaces(bool world, string first, string second)
    {
        var mesh = TestMeshes.MirroredQuad();
        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: world);
        Assert.Contains(first, sw.ToString());
        Assert.Contains(second, sw.ToString());
        Assert.Equal([0, 1, 2, 2, 1, 3], mesh.Indices);
    }

    [Theory]
    [InlineData("A\nB", "o A_B")]
    [InlineData("A\u2028B", "o A_B")]
    [InlineData("A\u2029B", "o A_B")]
    [InlineData("   ", "o Mesh_4")]
    [InlineData("", "o Mesh_4")]
    public void Write_MeshName_IsSanitizedOrFallsBackToId(string name, string expected)
    {
        var mesh = new NwcMesh
        {
            Id = 4,
            Name = name,
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: false);

        Assert.Contains(expected + Environment.NewLine, sw.ToString());
    }

    [Fact]
    public void Write_SingleMesh_ProducesValidWavefrontObj()
    {
        var vertices = new[]
        {
            new Vector3d(0, 0, 0),
            new Vector3d(10, 0, 0),
            new Vector3d(0, 10, 0),
        };

        var mesh = new NwcMesh
        {
            Id = 0,
            Name = "Triangle",
            Vertices = vertices,
            Indices = [0, 1, 2],
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: false, sourceDocument: "test.rvt");
        string obj = sw.ToString();

        Assert.Contains("# Wavefront OBJ exported by NwcIO", obj);
        Assert.Contains("# Source: test.rvt", obj);
        Assert.Contains("o Triangle", obj);
        Assert.Contains("v 0.0000 0.0000 0.0000", obj);
        Assert.Contains("v 10.0000 0.0000 0.0000", obj);
        Assert.Contains("v 0.0000 10.0000 0.0000", obj);
        Assert.Contains("f 1 2 3", obj);
    }

    [Fact]
    public void Write_MultipleMeshes_UsesGlobal1BasedIndexOffsets()
    {
        var mesh1 = new NwcMesh
        {
            Id = 1,
            Name = "MeshA",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        var mesh2 = new NwcMesh
        {
            Id = 2,
            Name = "MeshB",
            Vertices = [new Vector3d(2, 0, 0), new Vector3d(3, 0, 0), new Vector3d(2, 1, 0)],
            Indices = [0, 1, 2],
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh1, mesh2], sw, worldCoordinates: false);
        string obj = sw.ToString();

        // MeshA has vertices 1..3, faces f 1 2 3
        Assert.Contains("o MeshA", obj);
        Assert.Contains("f 1 2 3", obj);

        // MeshB has vertices 4..6, faces f 4 5 6
        Assert.Contains("o MeshB", obj);
        Assert.Contains("f 4 5 6", obj);
    }

    [Fact]
    public void Write_WithWorldCoordinates_AppliesTransformMatrix()
    {
        var transform = new NwcTransform
        {
            Id = 1,
            Translation = new Vector3d(100, 200, 300),
            Rotation = Quaterniond.Identity,
        };

        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new Vector3d(5, 5, 5), new Vector3d(6, 5, 5), new Vector3d(5, 6, 5)],
            Indices = [0, 1, 2],
            Transform = transform,
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: true);
        string obj = sw.ToString();

        Assert.Contains("v 105.0000 205.0000 305.0000", obj);
    }

    [Fact]
    public void Write_LineMesh_ProducesLineElements()
    {
        var mesh = new NwcMesh
        {
            Id = 10,
            Name = "GridLine",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(10, 10, 0)],
            Indices = [],
            LineIndices = [0, 1, 1, 2],
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: false);
        string obj = sw.ToString();

        Assert.Contains("o GridLine", obj);
        Assert.Contains("v 0.0000 0.0000 0.0000", obj);
        Assert.Contains("v 10.0000 0.0000 0.0000", obj);
        Assert.Contains("v 10.0000 10.0000 0.0000", obj);
        Assert.Contains("l 1 2", obj);
        Assert.Contains("l 2 3", obj);
        Assert.DoesNotContain("f ", obj);
    }

    [Fact]
    public void Write_MixedMesh_ProducesBothFacesAndLines()
    {
        var mesh = new NwcMesh
        {
            Id = 11,
            Name = "MixedMesh",
            Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0)],
            Indices = [0, 1, 2],
            LineIndices = [0, 1],
        };

        using var sw = new StringWriter();
        ObjExporter.Write([mesh], sw, worldCoordinates: false);
        string obj = sw.ToString();

        Assert.Contains("o MixedMesh", obj);
        Assert.Contains("f 1 2 3", obj);
        Assert.Contains("l 1 2", obj);
    }
}
