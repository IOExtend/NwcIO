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
    public void Scene_WithFolderInput_Exits1()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (exit, _, err) = Run([dir, "--scene", Path.Combine(dir, "out.scene.json")]);
            Assert.Equal(1, exit);
            Assert.Contains("only supported for a single", err);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Scene_WithReaderFlags_Exits1()
    {
        string model = TempModel();
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exit, _, err) = Run([model, "--scene", scene, "--no-tree"]);
            Assert.Equal(1, exit);
            Assert.Contains("cannot be combined with --no-meshes", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            if (File.Exists(scene)) File.Delete(scene);
        }
    }

    [Theory]
    [InlineData("ft", "ft")]
    [InlineData("m", "m")]
    [InlineData("cm", "cm")]
    [InlineData("mm", "mm")]
    [InlineData("Meters", "m")]
    public void Scene_LengthUnitSymbols_WriteTheScene(string unit, string expectedSymbol)
    {
        string model = TempModel();
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exit, _, err) = Run([model, "--scene", scene, "--scene-length-unit", unit]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(scene));
            Assert.DoesNotContain("Invalid scene length unit", err);
            Assert.DoesNotContain("unit-unknown", err);
            Assert.Matches(@"(?i)""sourceLengthUnit""\s*:\s*""" + expectedSymbol + @"""", File.ReadAllText(scene));
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            if (File.Exists(scene)) File.Delete(scene);
        }
    }

    [Fact]
    public void Scene_WithoutLengthUnit_UnknownUnitIsReportedAndSceneIsStillWritten()
    {
        string model = TempModel();
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exit, _, err) = Run([model, "--scene", scene]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(scene));
            Assert.Contains("Scene Warning: unit-unknown", err);
            Assert.DoesNotContain("Error writing scene", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            if (File.Exists(scene)) File.Delete(scene);
        }
    }

    [Fact]
    public void Scene_DiagnosticOutputFailure_IsNotReportedAsSceneWriteError()
    {
        string model = TempModel();
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using var swOut = new StringWriter();
            var err = new ThrowingOnSceneDiagnosticWriter();

            // The scene is already on disk when diagnostics are printed; a failing stderr must not turn into "Error writing scene" / exit 3.
            Assert.Throws<IOException>(() => NwcIO.Cli.Program.Run([model, "--scene", scene], stdoutStream: null, outWriter: swOut, errorWriter: err));

            Assert.True(File.Exists(scene));
            Assert.DoesNotContain("Error writing scene", err.Written.ToString());
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            if (File.Exists(scene)) File.Delete(scene);
        }
    }

    private sealed class ThrowingOnSceneDiagnosticWriter : StringWriter
    {
        public StringWriter Written { get; } = new();

        public override void WriteLine(string? value)
        {
            if (value is not null && value.StartsWith("Scene ", StringComparison.Ordinal))
                throw new IOException("stderr closed");
            Written.WriteLine(value);
        }
    }

    [Fact]
    public void Scene_WithNwfInput_Exits1()
    {
        string nwf = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".nwf");
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        var (exit, _, err) = Run([nwf, "--scene", scene]);

        Assert.Equal(1, exit);
        Assert.Contains("only supported for a single", err);
        Assert.False(File.Exists(scene));
    }

    [Fact]
    public void Scene_WhenTheTargetCannotBeWritten_ReportsTheErrorAndExits3()
    {
        string model = TempModel();
        string target = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);   // a directory where the scene file should go
        try
        {
            var (exit, _, err) = Run([model, "--scene", target, "--scene-length-unit", "m"]);

            Assert.Equal(3, exit);
            Assert.Contains("Error writing scene", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            Directory.Delete(target, true);
        }
    }

    [Fact]
    public void Scene_InvalidLengthUnit_Exits1()
    {
        string model = TempModel();
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exit, _, err) = Run([model, "--scene", scene, "--scene-length-unit", "99"]);

            Assert.Equal(1, exit);
            Assert.Contains("Invalid scene length unit '99'", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            if (File.Exists(scene)) File.Delete(scene);
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
}
