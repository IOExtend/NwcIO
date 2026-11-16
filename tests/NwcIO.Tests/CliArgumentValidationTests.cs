using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class CliArgumentValidationTests
{
    private static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        using var swOut = new StringWriter();
        using var swErr = new StringWriter();
        int exit = NwcIO.Cli.Program.Run(args, stdoutStream: null, outWriter: swOut, errorWriter: swErr);
        return (exit, swOut.ToString(), swErr.ToString());
    }

    private static string TempModel()
    {
        string path = Path.Combine(Path.GetTempPath(), "cli-val-" + Guid.NewGuid().ToString("N") + ".nwc");
        new NwcDocumentBuilder().SetFileInfo("test.rvt")
            .AddMesh(new NwcMesh
            {
                Id = 1,
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            })
            .Build()
            .Save(path);
        return path;
    }

    [Fact]
    public void NoArguments_PrintsUsageAndExits1()
    {
        var (exit, stdout, stderr) = Run([]);
        Assert.Equal(1, exit);
        Assert.Contains("Usage: nwcio", stdout);
    }

    [Fact]
    public void HelpOption_PrintsUsageAndExits0()
    {
        var (exit1, out1, _) = Run(["--help"]);
        Assert.Equal(0, exit1);
        Assert.Contains("Usage: nwcio", out1);

        var (exit2, out2, _) = Run(["-h"]);
        Assert.Equal(0, exit2);
        Assert.Contains("Usage: nwcio", out2);
    }

    [Fact]
    public void VersionOption_PrintsVersionAndExits0()
    {
        var (exit1, out1, _) = Run(["--version"]);
        Assert.Equal(0, exit1);
        Assert.StartsWith("nwcio", out1.Trim());

        var (exit2, out2, _) = Run(["-v"]);
        Assert.Equal(0, exit2);
        Assert.StartsWith("nwcio", out2.Trim());
    }

    [Fact]
    public void ValueOption_ConsumingFlagOrOption_Exits1WithError()
    {
        string model = TempModel();
        try
        {
            // --nwc followed by -v should fail with missing argument for --nwc, NOT print version
            var (exit1, out1, err1) = Run([model, "--nwc", "-v"]);
            Assert.Equal(1, exit1);
            Assert.DoesNotContain("nwcio", out1);
            Assert.Contains("Error: Missing argument for --nwc", err1);

            // --nwc followed by --glb should fail with missing argument for --nwc
            var (exit2, _, err2) = Run([model, "--nwc", "--glb"]);
            Assert.Equal(1, exit2);
            Assert.Contains("Error: Missing argument for --nwc", err2);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Fact]
    public void ClashGroup_TypoOrInvalidMode_Exits1()
    {
        string model = TempModel();
        try
        {
            var (exit, _, err) = Run([model, "--clash-group", "proximty"]);
            Assert.Equal(1, exit);
            Assert.Contains("Error: Invalid clash group mode 'proximty'", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Fact]
    public void Raycast_MalformedCoordinates_Exits1()
    {
        string model = TempModel();
        try
        {
            var (exit, _, err) = Run([model, "--raycast", "1,2,3"]);
            Assert.Equal(1, exit);
            Assert.Contains("Error: Invalid raycast format '1,2,3'", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Fact]
    public void Raycast_WithNegativeFirstCoordinate_ExecutesSuccessfullyAndReportsHit()
    {
        string model = TempModel();
        string json = Path.Combine(Path.GetTempPath(), "cli-val-ray-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exit, stdout, stderr) = Run([model, json, "--raycast", "-1,0.25,1,1,0,-1"]);
            Assert.Equal(0, exit);
            Assert.Contains("Raycast HIT", stdout);
            Assert.True(File.Exists(json));
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            if (File.Exists(json)) File.Delete(json);
        }
    }

    [Fact]
    public void TimeLinerSimulate_UnparseableDate_Exits1()
    {
        string model = TempModel();
        try
        {
            var (exit, _, err) = Run([model, "--timeliner-simulate", "15/06/2026x"]);
            Assert.Equal(1, exit);
            Assert.Contains("Error: Unable to parse simulation date '15/06/2026x'", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Theory]
    [InlineData("--clash-tolerance", "-1")]
    [InlineData("--clash-tolerance", "NaN")]
    [InlineData("--clash-tolerance", "Infinity")]
    [InlineData("--clash-radius", "-1")]
    [InlineData("--clash-radius", "0")]
    [InlineData("--clash-radius", "NaN")]
    [InlineData("--diff-tolerance", "-0.5")]
    [InlineData("--diff-tolerance", "NaN")]
    public void NumericOptions_InvalidOrOutOfRange_Exits1(string option, string value)
    {
        string model = TempModel();
        try
        {
            var (exit, _, err) = Run([model, option, value]);
            Assert.Equal(1, exit);
            Assert.Contains("Error: Invalid value for " + option, err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Fact]
    public void DiffJsonOrBcf_WithoutDiffBaseline_Exits1()
    {
        string model = TempModel();
        try
        {
            var (exit1, _, err1) = Run([model, "--diff-json", "diff.json"]);
            Assert.Equal(1, exit1);
            Assert.Contains("require --diff <baseline>", err1);

            var (exit2, _, err2) = Run([model, "--diff-bcf", "diff.bcf"]);
            Assert.Equal(1, exit2);
            Assert.Contains("require --diff <baseline>", err2);

            var (exit3, _, err3) = Run([model, "--diff-tolerance", "0.01"]);
            Assert.Equal(1, exit3);
            Assert.Contains("require --diff <baseline>", err3);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Fact]
    public void FolderOrNwfAssembly_WithUnsupportedOptions_Exits1()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-val-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (exit1, _, err1) = Run([dir, "--diff", "base.nwc"]);
            Assert.Equal(1, exit1);
            Assert.Contains("not supported for folder or .nwf assembly inputs", err1);

            var (exit2, _, err2) = Run([dir, "--timeliner-simulate", "2026-06-15"]);
            Assert.Equal(1, exit2);
            Assert.Contains("not supported for folder or .nwf assembly inputs", err2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void IfcSchema_InvalidVersion_Exits1()
    {
        string model = TempModel();
        try
        {
            var (exit, _, err) = Run([model, "--ifc-schema", "IFC5"]);
            Assert.Equal(1, exit);
            Assert.Contains("Error: Invalid IFC schema 'IFC5'", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Fact]
    public void SingleFile_AssemblyOnlyOptions_AreRejected()
    {
        string model = TempModel();
        try
        {
            var (exit1, _, err1) = Run([model, "--nwf-xml", model + ".nwf.xml"]);
            Assert.Equal(1, exit1);
            Assert.Contains("--nwf-xml is only supported for folder and .nwf assembly inputs", err1);

            var (exit2, _, err2) = Run([model, "--clashes"]);
            Assert.Equal(1, exit2);
            Assert.Contains("--clashes is only supported for folder and .nwf assembly inputs", err2);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    private static void SaveModel(string path)
    {
        new NwcDocumentBuilder().SetFileInfo("test.rvt")
            .AddMesh(new NwcMesh
            {
                Id = 1,
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            })
            .Build()
            .Save(path);
    }

    [Theory]
    [InlineData("--sets-xml")]
    [InlineData("--timeliner-csv")]
    [InlineData("--grids")]
    [InlineData("--export-grids")]
    public void Assembly_SingleFileOnlyOptions_AreRejected(string flag)
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-val-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SaveModel(Path.Combine(dir, "a.nwc"));
            string nwf = Path.Combine(dir, "fed.nwf");
            File.WriteAllBytes(nwf, [0]);

            foreach (string input in new[] { dir, nwf })
            {
                var (exit, _, err) = Run([input, Path.Combine(dir, "out.json"), flag, Path.Combine(dir, "x.dat")]);
                Assert.Equal(1, exit);
                Assert.Contains(flag + " is only supported for single-file input", err);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Folder_ReaderOptions_AreApplied()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-val-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SaveModel(Path.Combine(dir, "a.nwc"));

            string withMeshes = Path.Combine(dir, "with.json");
            Assert.Equal(0, Run([dir, withMeshes])[0 ] is int e1 ? e1 : -1);
            Assert.Contains("\"totalMeshes\":1", File.ReadAllText(withMeshes).Replace(" ", ""));

            string noMeshes = Path.Combine(dir, "without.json");
            var (exit, _, err) = Run([dir, noMeshes, "--no-meshes"]);
            Assert.True(exit == 0, err);
            Assert.Contains("\"totalMeshes\":0", File.ReadAllText(noMeshes).Replace(" ", ""));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
