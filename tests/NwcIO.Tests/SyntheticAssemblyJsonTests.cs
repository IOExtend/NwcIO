using System.Text.Json;
using NwcIO.Model;
using NwcIO.Model.Clash;
using Xunit;

namespace NwcIO.Tests;

public sealed class SyntheticAssemblyJsonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"nwcio-synthetic-{Guid.NewGuid():N}");

    public SyntheticAssemblyJsonTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static NwcDocument CreateDocument(string sourcePath)
    {
        var material = new NwcMaterial { Id = 1, Name = "Concrete", Color = new RgbaColor(0.7f, 0.7f, 0.7f, 1f) };
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Wall",
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            Material = material,
            Transform = new NwcTransform { Id = 5, Translation = new Vector3d(1, 2, 3) },
        };
        var root = new NwcNode { Id = 0, Name = "Model", Class = "Model" };
        var node = new NwcNode { Id = 1, Name = "Wall", Class = "LcRevitData_Element", Mesh = mesh, Parent = root };
        mesh.Node = node;
        root.Children.Add(node);
        return NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = sourcePath, LoaderPlugin = "LcNwcLoaderPlugin:lcldrevit" },
            meshes: [mesh],
            materials: [material],
            transforms: [mesh.Transform],
            rootNode: root);
    }

    private string SaveModel(string name)
    {
        string path = Path.Combine(_root, name);
        CreateDocument(path).Save(path);
        return path;
    }

    [Fact]
    public void LoadFromDirectory_SortsModelsAndNamesTheAssemblyAfterTheFolder()
    {
        SaveModel("b.nwc");
        SaveModel("A.nwc");

        var assembly = NwcAssembly.LoadFromDirectory(_root);

        Assert.Equal(Path.GetFileName(_root), assembly.Name);
        Assert.Equal(2, assembly.Models.Count);
        Assert.EndsWith("A.nwc", assembly.Models[0].FileInfo.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("b.nwc", assembly.Models[1].FileInfo.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromDirectory_MissingFolder_ThrowsDirectoryNotFound()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => NwcAssembly.LoadFromDirectory(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void LoadFromDirectory_NoMatchingFiles_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(
            () => NwcAssembly.LoadFromDirectory(_root, "*.nwd"));
    }

    [Fact]
    public void OpenNwf_MissingFile_ThrowsFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(
            () => NwcAssembly.OpenNwf(Path.Combine(_root, "missing.nwf")));
    }

    [Fact]
    public void OpenNwf_ContainerWithoutXRefs_ReturnsOneModelNamedAfterTheFile()
    {
        string nwc = SaveModel("model.nwc");
        string nwf = Path.Combine(_root, "federation.nwf");
        File.Copy(nwc, nwf);

        var assembly = NwcAssembly.OpenNwf(nwf);

        Assert.Single(assembly.Models);
        Assert.Equal("federation", assembly.Name);
        Assert.Empty(assembly.UnresolvedXRefs);
        Assert.Empty(assembly.SkippedXRefs);
    }

    [Fact]
    public void SaveNwf_WritesARelativeManifestThatReopens()
    {
        SaveModel("first.nwc");
        SaveModel("second.nwc");
        var assembly = NwcAssembly.LoadFromDirectory(_root);
        string nwf = Path.Combine(_root, "federation.nwf");

        assembly.SaveNwf(nwf);
        var manifest = NwcNwfManifest.Open(nwf);

        Assert.Equal(2, manifest.ModelPaths.Count);
        Assert.All(manifest.ModelPaths, path => Assert.False(Path.IsPathRooted(path)));
        Assert.Contains("first.nwc", manifest.ModelPaths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("second.nwc", manifest.ModelPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveJson_Document_WritesEverySectionAndHonoursSwitches()
    {
        var doc = CreateDocument("model.rvt");

        string full = doc.ToJson(new NwcJsonExportOptions
        {
            Indented = false,
            IncludeTransforms = true,
            IncludeSceneTree = true,
            IncludeMeshes = true,
            IncludeMaterials = true,
            IncludeStreamCatalog = true,
        });
        using (var json = JsonDocument.Parse(full))
        {
            var root = json.RootElement;
            Assert.True(root.TryGetProperty("transforms", out _));
            Assert.True(root.TryGetProperty("sceneTree", out _));
            Assert.True(root.TryGetProperty("meshes", out _));
            Assert.True(root.TryGetProperty("materials", out _));
            Assert.True(root.TryGetProperty("container", out _));
        }

        string minimal = doc.ToJson(new NwcJsonExportOptions
        {
            Indented = false,
            IncludeTransforms = false,
            IncludeSceneTree = false,
            IncludeMeshes = false,
            IncludeMaterials = false,
            IncludeStreamCatalog = false,
        });
        using (var json = JsonDocument.Parse(minimal))
        {
            var root = json.RootElement;
            Assert.False(root.TryGetProperty("transforms", out _));
            Assert.False(root.TryGetProperty("sceneTree", out _));
            Assert.False(root.TryGetProperty("meshes", out _));
            Assert.False(root.TryGetProperty("materials", out _));
            Assert.False(root.TryGetProperty("container", out _));
        }
    }

    [Fact]
    public void SaveJson_Assembly_WritesAssemblySummary()
    {
        SaveModel("first.nwc");
        SaveModel("second.nwc");
        var assembly = NwcAssembly.LoadFromDirectory(_root);
        using var stream = new MemoryStream();

        assembly.SaveJson(stream, new NwcJsonExportOptions { Indented = false });

        using var json = JsonDocument.Parse(stream.ToArray());
        var root = json.RootElement;
        Assert.Equal(assembly.Name, root.GetProperty("assemblyName").GetString());
        Assert.Equal(2, root.GetProperty("modelCount").GetInt32());
        Assert.Equal(assembly.AllMeshes.Count, root.GetProperty("totalMeshes").GetInt32());
    }

    [Fact]
    public void Cli_DirectoryInput_WritesValidJson()
    {
        SaveModel("model.nwc");
        string output = Path.Combine(_root, "cli.json");

        int exitCode = NwcIO.Cli.Program.Main([_root, output]);

        Assert.Equal(0, exitCode);
        using var json = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.True(json.RootElement.TryGetProperty("assemblyName", out _));
    }
}
