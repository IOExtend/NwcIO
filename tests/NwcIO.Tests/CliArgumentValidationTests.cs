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
        WriteModel(path);
        return path;
    }

    private static void WriteModel(string path) =>
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
    public void Raycast_FolderAssemblyWithSharedSourcePath_ReportsModelIndexInLabel()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-val-ray-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var (file, offsetX) in new[] { ("A.nwc", 0.0), ("B.nwc", 20.0) })
            {
                new NwcDocumentBuilder().SetFileInfo("Arch.rvt")
                    .AddMesh(new NwcMesh
                    {
                        Id = 0,
                        Vertices = [new(offsetX, 0, 0), new(offsetX + 1, 0, 0), new(offsetX, 1, 0)],
                        Indices = [0, 1, 2],
                        BoundingBox = new BoundingBox3d(new(offsetX, 0, 0), new(offsetX + 1, 1, 0)),
                    })
                    .Build()
                    .Save(Path.Combine(dir, file));
            }

            var (exitA, outA, errA) = Run([dir, "--raycast", "0.25,0.25,1,0,0,-1"]);
            var (exitB, outB, errB) = Run([dir, "--raycast", "20.25,0.25,1,0,0,-1"]);

            Assert.True(exitA == 0, errA);
            Assert.True(exitB == 0, errB);
            Assert.Contains("Model=M1_Arch", outA);
            Assert.Contains("Model=M2_Arch", outB);
        }
        finally
        {
            Directory.Delete(dir, true);
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
    public void MultipleInputsWithoutOutput_Exits1()
    {
        string first = TempModel();
        string second = TempModel();
        try
        {
            var (exit, _, err) = Run(["-i", first, "-i", second]);
            Assert.Equal(1, exit);
            Assert.Contains("--output is required", err);
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
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
    public void Scene_WithMultipleInputs_Exits1()
    {
        string first = TempModel();
        string second = TempModel();
        string output = Path.Combine(Path.GetTempPath(), "cli-asm-" + Guid.NewGuid().ToString("N") + ".glb");
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (exit, _, err) = Run(["-i", first, "-i", second, "-o", output, "--scene", scene]);

            Assert.Equal(1, exit);
            Assert.Contains("only supported for a single", err);
            Assert.False(File.Exists(scene));
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void Scene_WithManifestInput_Exits1()
    {
        string manifest = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".nwf.xml");
        string scene = Path.Combine(Path.GetTempPath(), "cli-scene-" + Guid.NewGuid().ToString("N") + ".json");
        var (exit, _, err) = Run([manifest, "--scene", scene]);

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
    public void MultipleInputs_BuildOneAssembly()
    {
        string first = TempModel();
        string second = TempModel();
        string output = Path.Combine(Path.GetTempPath(), "cli-asm-" + Guid.NewGuid().ToString("N") + ".glb");
        try
        {
            var (exit, _, err) = Run(["-i", first, "-i", second, "-o", output, "--report"]);
            Assert.Equal(0, exit);
            Assert.True(File.Exists(output), err);
            Assert.Contains("Models:           2", err);
            Assert.Contains("Total Meshes:     2", err);
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
            if (File.Exists(output)) File.Delete(output);
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
            var (exit0, _, err0) = Run([dir, withMeshes]);
            Assert.True(exit0 == 0, err0);
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

    [Fact]
    public void MultipleInputs_SameFileTwice_IsLoadedOnce()
    {
        string model = TempModel();
        string output = Path.Combine(Path.GetTempPath(), "cli-asm-" + Guid.NewGuid().ToString("N") + ".glb");
        try
        {
            var (exit, _, err) = Run(["-i", model, "-i", Path.GetFullPath(model), "-o", output, "--report"]);
            Assert.Equal(0, exit);
            Assert.Contains("Models:           1", err);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void MultipleInputs_InvalidFile_NamesTheFile()
    {
        string good = TempModel();
        string bad = Path.Combine(Path.GetTempPath(), "cli-bad-" + Guid.NewGuid().ToString("N") + ".nwc");
        File.WriteAllText(bad, "not a navisworks file");
        string output = Path.Combine(Path.GetTempPath(), "cli-asm-" + Guid.NewGuid().ToString("N") + ".glb");
        try
        {
            var (exit, _, err) = Run(["-i", good, "-i", bad, "-o", output]);
            Assert.Equal(3, exit);
            Assert.Contains(bad, err);
        }
        finally
        {
            if (File.Exists(good)) File.Delete(good);
            if (File.Exists(bad)) File.Delete(bad);
        }
    }

    [Fact]
    public void RecursiveAndPattern_RequireFolderInput()
    {
        string model = TempModel();
        try
        {
            var (exit1, _, err1) = Run([model, "--recursive"]);
            Assert.Equal(1, exit1);
            Assert.Contains("only supported for folder input", err1);

            var (exit2, _, err2) = Run([model, "--pattern", "*.nwd"]);
            Assert.Equal(1, exit2);
            Assert.Contains("only supported for folder input", err2);
        }
        finally
        {
            if (File.Exists(model)) File.Delete(model);
        }
    }

    [Fact]
    public void FolderInput_EmptyPattern_Exits1()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-val-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (exit, _, err) = Run([dir, "--pattern", ";"]);
            Assert.Equal(1, exit);
            Assert.Contains("--pattern needs at least one", err);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FolderInput_RecursiveAndPattern_LoadSubfolderModels()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-val-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        string output = Path.Combine(dir, "out.glb");
        try
        {
            WriteModel(Path.Combine(dir, "a.nwc"));
            WriteModel(Path.Combine(dir, "sub", "b.nwc"));

            var (exit1, _, err1) = Run([dir, output, "--report"]);
            Assert.Equal(0, exit1);
            Assert.Contains("Models:           1", err1);

            var (exit2, _, err2) = Run([dir, output, "--recursive", "--report"]);
            Assert.Equal(0, exit2);
            Assert.Contains("Models:           2", err2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ManifestInput_IsAnAssembly()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-val-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WriteModel(Path.Combine(dir, "a.nwc"));
            WriteModel(Path.Combine(dir, "b.nwc"));
            string manifest = Path.Combine(dir, "fed.v2.nwf.xml");
            var m = new NwcIO.Model.Clash.NwcNwfManifest("Fed");
            m.ModelPaths.Add("a.nwc");
            m.ModelPaths.Add("b.nwc");
            m.ModelPaths.Add("missing.nwc");
            m.Save(manifest);

            // Assembly options are accepted, and the default output keeps the dotted name.
            var (exit, _, err) = Run([manifest, "--clashes"]);
            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(dir, "fed.v2.glb")), err);
            Assert.Contains("missing.nwc", err);

            var (exit2, _, err2) = Run([manifest, "--diff", "base.nwc"]);
            Assert.Equal(1, exit2);
            Assert.Contains("not supported for folder or .nwf assembly inputs", err2);

            var (exit3, _, err3) = Run([manifest, "--timeliner-simulate", "2026-06-15"]);
            Assert.Equal(1, exit3);
            Assert.Contains("not supported for folder or .nwf assembly inputs", err3);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void AppendTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        stream.Write(new byte[8]);
    }

    [Fact]
    public void Report_PrintsDocumentDiagnostics_OnlyWhenPresent()
    {
        string model = TempModel();
        string clean = Path.Combine(Path.GetTempPath(), "cli-diag-" + Guid.NewGuid().ToString("N") + ".json");
        string output = Path.Combine(Path.GetTempPath(), "cli-diag-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (cleanExit, _, cleanErr) = Run([model, clean, "--report"]);
            Assert.Equal(0, cleanExit);
            Assert.DoesNotContain("Diagnostics:", cleanErr);

            AppendTail(model);
            var (exit, _, err) = Run([model, output, "--report"]);

            Assert.Equal(0, exit);
            Assert.Contains("Diagnostics:      1", err);
            Assert.Contains("[Warning] ContainerTrailingGap", err);
        }
        finally
        {
            foreach (var f in new[] { model, clean, output })
            {
                if (File.Exists(f)) File.Delete(f);
            }
        }
    }

    [Fact]
    public void Report_PrintsAssemblyDiagnostics_ForFolderAndManifest()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string a = Path.Combine(dir, "a.nwc");
            WriteModel(a);
            AppendTail(a);
            WriteModel(Path.Combine(dir, "b.nwc"));

            var (folderExit, _, folderErr) = Run([dir, "--report"]);
            Assert.True(folderExit == 0, folderErr);
            Assert.Contains("Diagnostics:      1", folderErr);
            Assert.Contains("ContainerTrailingGap", folderErr);

            string manifest = Path.Combine(dir, "fed.nwf.xml");
            var m = new NwcIO.Model.Clash.NwcNwfManifest("Fed");
            m.ModelPaths.Add("a.nwc");
            m.ModelPaths.Add("b.nwc");
            m.Save(manifest);

            var (exit, _, err) = Run([manifest, "--report"]);
            Assert.True(exit == 0, err);
            Assert.Contains("Diagnostics:      1", err);
            Assert.Contains("ContainerTrailingGap", err);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Report_UnresolvedReference_IsReportedOnce()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cli-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            WriteModel(Path.Combine(dir, "a.nwc"));
            string manifest = Path.Combine(dir, "fed.nwf.xml");
            var m = new NwcIO.Model.Clash.NwcNwfManifest("Fed");
            m.ModelPaths.Add("a.nwc");
            m.ModelPaths.Add("missing.nwc");
            m.Save(manifest);

            var (exit, _, err) = Run([manifest, "--report"]);

            Assert.True(exit == 0, err);
            Assert.Equal(1, err.Split("missing.nwc").Length - 1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
