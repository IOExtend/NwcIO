using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

/// <summary>
/// Exit-code contract of the CLI: 2 for missing input, 3 for data format errors —
/// never an unhandled exception — on the single-file, folder-assembly and NWF paths.
/// </summary>
public class CliExitCodeTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "nwcio-cli-exit-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EmptyFolder_Exits2WithoutThrowing()
    {
        string root = TempRoot();
        string dir = Path.Combine(root, "empty");
        Directory.CreateDirectory(dir);
        try
        {
            int exit = NwcIO.Cli.Program.Main([dir, Path.Combine(root, "out.glb")]);
            Assert.Equal(2, exit);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FolderWithCorruptNwc_Exits3WithoutThrowing()
    {
        string root = TempRoot();
        string dir = Path.Combine(root, "bad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.nwc"), "garbage");
        try
        {
            int exit = NwcIO.Cli.Program.Main([dir, Path.Combine(root, "out.glb")]);
            Assert.Equal(3, exit);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PartitionHierarchyNwc_OpensWithAndWithoutTree()
    {
        string root = TempRoot();
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "p.nwc");
        string json = Path.Combine(root, "out.json");
        File.WriteAllBytes(path, ModelGraphReaderTests.PartitionFile());
        using var error = new StringWriter();
        try
        {
            Assert.Equal(0, NwcIO.Cli.Program.Run([path, json], errorWriter: error));
            Assert.Contains("\"LcOaPartition\"", File.ReadAllText(json));

            Assert.Equal(0, NwcIO.Cli.Program.Main([path, json, "--no-tree"]));
            Assert.True(File.Exists(json));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaxPropertyRefs_AppliesToInputAndDiffBaseline(bool useBaseline)
    {
        string root = TempRoot();
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "input.nwd");
            string output = Path.Combine(root, "out.json");
            string baseline = Path.Combine(root, "baseline.nwd");
            string diff = Path.Combine(root, "diff.json");
            byte[] file = SyntheticNwcFile.Build(HierarchyParserTests.PropertyLeafHierarchy(), null, [], []);
            string[] args = [path, output, "--no-meshes", "--no-materials", "--no-transforms", "--report"];
            if (useBaseline)
            {
                var (empty, _) = SyntheticTree.Write(1);
                File.WriteAllBytes(path, SyntheticNwcFile.Build(empty, null, [], []));
                File.WriteAllBytes(baseline, file);
                args = [.. args, "--diff", baseline, "--diff-json", diff];
            }
            else
            {
                File.WriteAllBytes(path, file);
            }

            string expectedOutput = useBaseline ? diff : output;
            using var error = new StringWriter();
            Assert.Equal(3, NwcIO.Cli.Program.Run([.. args, "--max-property-refs", "3"], errorWriter: error));
            Assert.Contains("expanded tree exceeds 3 property references", error.ToString());
            Assert.False(File.Exists(expectedOutput));

            using var retryError = new StringWriter();
            int exit = NwcIO.Cli.Program.Run([.. args, "--max-property-refs", "4"], errorWriter: retryError);
            Assert.True(exit == 0, retryError.ToString());
            Assert.True(File.Exists(expectedOutput));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PartitionHierarchyFolder_OpensWithAndWithoutTree()
    {
        string root = TempRoot();
        string dir = Path.Combine(root, "models");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "p.nwc"), ModelGraphReaderTests.PartitionFile());
        string glb = Path.Combine(root, "out.glb");
        try
        {
            Assert.Equal(0, NwcIO.Cli.Program.Main([dir, glb]));
            Assert.Equal(0, NwcIO.Cli.Program.Main([dir, glb, "--no-tree"]));
            Assert.True(File.Exists(glb));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PartitionHierarchyDiffBaseline_OpensWithNoTree()
    {
        string root = TempRoot();
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "p.nwc");
        File.WriteAllBytes(path, ModelGraphReaderTests.PartitionFile());
        string diff = Path.Combine(root, "diff.json");
        try
        {
            Assert.Equal(0, NwcIO.Cli.Program.Main([path, Path.Combine(root, "out.json"), "--no-tree", "--diff", path, "--diff-json", diff]));
            Assert.True(File.Exists(diff));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CorruptNwfFile_Exits3WithoutThrowing()
    {
        string root = TempRoot();
        Directory.CreateDirectory(root);
        string nwf = Path.Combine(root, "x.nwf");
        File.WriteAllText(nwf, "garbage");
        try
        {
            int exit = NwcIO.Cli.Program.Main([nwf, Path.Combine(root, "out.glb")]);
            Assert.Equal(3, exit);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MalformedProfilerXml_Exits3WithoutThrowing()
    {
        string root = TempRoot();
        Directory.CreateDirectory(root);
        string model = Path.Combine(root, "m.nwc");
        new NwcDocumentBuilder().SetFileInfo("M.rvt")
            .AddMesh(new NwcMesh
            {
                Id = 1,
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            })
            .Build()
            .Save(model);
        string profiler = Path.Combine(root, "rules.xml");
        File.WriteAllText(profiler, "<appearance-rules><rule name='r' <broken>");

        try
        {
            int exit = NwcIO.Cli.Program.Main([model, "--profiler", profiler]);
            Assert.Equal(3, exit);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
