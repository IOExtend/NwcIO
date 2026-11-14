using IOScene;
using NwcIO;
using NwcIO.Model;
using NwcIO.Scene;
using NwcIO.Tests;
using Xunit;
using Vector3d = IOScene.Vector3d;
using RgbaColor = IOScene.RgbaColor;
using BoundingBox3d = IOScene.BoundingBox3d;

namespace NwcIO.Scene.Tests;

public sealed class NwcSceneReaderTests
{
    [Fact]
    public void Read_File_DescribesTheModel()
    {
        using var file = TempFile(RevitSample.Build(), "Tower.nwc");
        var result = Valid(NwcSceneReader.Read(file.Path));
        var model = Assert.Single(result.Document.Models);
        Assert.Equal(("Tower.nwc", "Tower.nwc", file.Path, "nwc", "ft", "lcldrevit", "2026-01-01T00:00:00Z"),
            (model.Key, model.Name, model.Path, model.SourceFormat, model.SourceLengthUnit, model.Application, model.CreatedAt));
        Assert.Matches(@"^NwcIO\.Scene \d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", model.Producer);
        Assert.Equal((NwcLengthUnit.Feet, 0.3048), (result.LengthUnit, result.MetresPerUnit));
    }

    [Fact]
    public void Read_ModelKeyOption_ReplacesTheFileName()
    {
        using var file = TempFile(RevitSample.Build(), "Tower.nwc");
        Assert.Equal("site/tower", Valid(NwcSceneReader.Read(file.Path, new() { ModelKey = "site/tower" })).Document.Models[0].Key);
    }

    [Fact]
    public void Read_EmptyModelKey_Throws()
    {
        using var file = TempFile(RevitSample.Build(), "Tower.nwc");
        Assert.Throws<ArgumentException>(() => NwcSceneReader.Read(file.Path, new() { ModelKey = "" }));
        Assert.Throws<ArgumentException>(() => NwcSceneReader.Read(Doc(RevitSample.Build()), new NwcSceneOptions()));
    }

    [Fact]
    public void Read_DocumentWithoutTree_Throws() =>
        Assert.Contains("BuildSceneTree", Assert.Throws<ArgumentException>(() =>
            NwcSceneReader.Read(Doc(RevitSample.Build(), new NwcReaderOptions { BuildSceneTree = false }), new() { ModelKey = "k" })).Message);

    [Fact]
    public void Read_PartitionTree_ThrowsNotSupportedWithTheAdapterReason()
    {
        // [k=1][0][root][n=1], a class-32 top-level node: [i32 0] + a class-53 body + 16 zero bytes (as HierarchyParserTests.PartitionHierarchy)
        var w = new LcGraphWriter().Int(1).Int(0);
        w.ImplicitRoot(); w.Int(1); w.New(32); w.Int(0).Str("P"); w.New(52); w.Str("P").Str("LcOaPartition");
        w.Int(0).Int(0).Int(0).Int(0).Int(0).Int(0).Int(0).Int(0).Int(1);
        using var file = TempFile(SyntheticNwcFile.Build(w.ToArray(), null, [], []), "Site.nwd");
        var ex = Assert.Throws<NotSupportedException>(() => NwcSceneReader.Read(file.Path));
        Assert.StartsWith("NwcIO.Scene needs the scene tree: .nwd partition trees and Revit links are not supported yet. ", ex.Message);
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Theory]
    [InlineData("LcNwcLoaderPlugin:lcldrevit", null, NwcLengthUnit.Feet, "ft")]
    [InlineData("LcNwcLoaderPlugin:mock", null, NwcLengthUnit.Unknown, null)]
    [InlineData("LcNwcLoaderPlugin:mock", NwcLengthUnit.Feet, NwcLengthUnit.Feet, "ft")]
    public void Read_Unit_ComesFromTheDocumentOrTheOption(string loader, NwcLengthUnit? option, NwcLengthUnit unit, string? symbol)
    {
        var result = Scene(RevitSample.Build(loader), new() { ModelKey = "k", LengthUnit = option });
        Assert.Equal((unit, symbol), (result.LengthUnit, result.Document.Models[0].SourceLengthUnit));
    }

    // Every converted document must pass Validate() without errors or warnings (design §2, §6.5).
    private static NwcSceneResult Valid(NwcSceneResult r)
    {
        Assert.Empty(r.Document.Validate());
        return r;
    }

    private static NwcDocument Doc(byte[] file, NwcReaderOptions? o = null) => NwcDocument.Open(new MemoryStream(file), o);

    private static NwcSceneResult Scene(byte[] file, NwcSceneOptions? o = null) =>
        Valid(NwcSceneReader.Read(Doc(file), o ?? new() { ModelKey = "Tower.nwc" }));

    private static TempDirectoryFile TempFile(byte[] bytes, string name) => new(bytes, name);

    // A file in a fresh directory under the temp path; Dispose deletes the directory.
    private sealed class TempDirectoryFile : IDisposable
    {
        private readonly string _directory;

        public TempDirectoryFile(byte[] bytes, string name)
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nwcio-scene-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, name);
            File.WriteAllBytes(Path, bytes);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
