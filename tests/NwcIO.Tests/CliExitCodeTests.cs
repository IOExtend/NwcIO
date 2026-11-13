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
    public void PartitionHierarchyNwc_Exits3WithoutNoTreeAndOpensWithIt()
    {
        string root = TempRoot();
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "p.nwc");
        File.WriteAllBytes(path, ModelGraphReaderTests.PartitionFile());
        try
        {
            Assert.Equal(3, NwcIO.Cli.Program.Main([path, Path.Combine(root, "out.json")]));
            Assert.Equal(0, NwcIO.Cli.Program.Main([path, Path.Combine(root, "out.json"), "--no-tree"]));
            Assert.True(File.Exists(Path.Combine(root, "out.json")));
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
