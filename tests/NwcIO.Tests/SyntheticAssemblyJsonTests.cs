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
        // Creation order differs from the expected case-insensitive order (a, B, c), and Ordinal would give B, a, c.
        SaveModel("c.nwc");
        SaveModel("B.nwc");
        SaveModel("a.nwc");

        var assembly = NwcAssembly.LoadFromDirectory(_root);

        Assert.Equal(Path.GetFileName(_root), assembly.Name);
        Assert.Equal(
            "a.nwc,B.nwc,c.nwc",
            string.Join(",", assembly.Models.Select(m => Path.GetFileName(m.FileInfo.SourcePath))));
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
        SaveModel("a.nwc");

        Assert.Throws<FileNotFoundException>(
            () => NwcAssembly.LoadFromDirectory(_root, "*.nwd"));
    }

    [Fact]
    public void LoadFromDirectory_SearchPattern_SelectsOnlyMatchingFiles()
    {
        SaveModel("a.nwc");
        string nwd = SaveModel("b.nwd");

        var assembly = NwcAssembly.LoadFromDirectory(_root, "*.nwd");

        var model = Assert.Single(assembly.Models);
        Assert.Equal(nwd, model.FileInfo.SourcePath, StringComparer.OrdinalIgnoreCase);
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
        string first = SaveModel("first.nwc");
        string second = SaveModel("second.nwc");
        var assembly = NwcAssembly.LoadFromDirectory(_root);
        // Manifest in a sibling folder: paths must be relative to the manifest folder, not bare file names.
        string outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(outDir);
        string nwf = Path.Combine(outDir, "federation.nwf");

        assembly.SaveNwf(nwf);
        var manifest = NwcNwfManifest.Open(nwf);

        Assert.Equal(2, manifest.ModelPaths.Count);
        Assert.All(manifest.ModelPaths, path => Assert.False(Path.IsPathRooted(path)));
        Assert.All(manifest.ModelPaths, p => Assert.StartsWith("..", p, StringComparison.Ordinal));
        var resolved = manifest.ModelPaths
            .Select(p => Path.GetFullPath(Path.Combine(outDir, p)))
            .ToArray();
        Assert.Contains(Path.GetFullPath(first), resolved, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(second), resolved, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] SectionKeys = ["transforms", "sceneTree", "meshes", "materials", "container"];

    private static NwcJsonExportOptions AllSectionsOn() => new()
    {
        Indented = false,
        IncludeTransforms = true,
        IncludeSceneTree = true,
        IncludeMeshes = true,
        IncludeMaterials = true,
        IncludeStreamCatalog = true,
    };

    private static JsonDocument Export(NwcJsonExportOptions options) =>
        JsonDocument.Parse(CreateDocument("model.rvt").ToJson(options));

    [Fact]
    public void ToJson_AllSwitchesOn_WritesEverySectionWithContent()
    {
        using var json = Export(AllSectionsOn());
        var root = json.RootElement;

        foreach (string key in SectionKeys)
        {
            Assert.True(root.TryGetProperty(key, out _), key);
        }
        Assert.Equal(1, root.GetProperty("meshes").GetArrayLength());
        Assert.Equal(1, root.GetProperty("materials").GetArrayLength());
        Assert.Equal(1, root.GetProperty("transforms").GetArrayLength());
    }

    [Theory]
    [InlineData(nameof(NwcJsonExportOptions.IncludeTransforms), "transforms")]
    [InlineData(nameof(NwcJsonExportOptions.IncludeSceneTree), "sceneTree")]
    [InlineData(nameof(NwcJsonExportOptions.IncludeMeshes), "meshes")]
    [InlineData(nameof(NwcJsonExportOptions.IncludeMaterials), "materials")]
    [InlineData(nameof(NwcJsonExportOptions.IncludeStreamCatalog), "container")]
    public void ToJson_SwitchingOffOneOption_RemovesOnlyItsSection(string option, string key)
    {
        var options = AllSectionsOn();
        typeof(NwcJsonExportOptions).GetProperty(option)!.SetValue(options, false);

        using var json = Export(options);
        var root = json.RootElement;

        Assert.False(root.TryGetProperty(key, out _), key);
        foreach (string other in SectionKeys.Where(k => k != key))
        {
            Assert.True(root.TryGetProperty(other, out _), other);
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
