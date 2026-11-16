using NwcIO.Internal.Container;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class AssemblyPipelineTests
{
    [Fact]
    public void Folder_ReportsFolderLabelsAndWritesDefaultOutput()
    {
        using var dir = new TempDir();
        NwcDocument.Create().Save(Path.Combine(dir.Path, "a.nwc"));

        var report = Run([dir.Path, Path.Combine(dir.Path, "folder.json"), "--report", "--bvh-stats"]);
        Assert.Equal(0, report.ExitCode);
        Assert.Contains("=== NwcIO Assembly Report ===", report.StdErr);
        Assert.DoesNotContain("NWF", report.StdErr);
        Assert.Contains("Models:           1", report.StdErr);
        Assert.Contains("Total Materials:", report.StdErr);
        Assert.Contains("BVH Nodes:", report.StdErr);

        var summary = Run([dir.Path]);
        Assert.Equal(0, summary.ExitCode);
        Assert.StartsWith("Assembly '", summary.StdOut);
        string expected = Path.Combine(dir.Path, Path.GetFileName(dir.Path) + "_assembly.glb");
        Assert.Contains($"exported to '{expected}'", summary.StdOut);
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void Nwf_ReportsNwfLabelsResolvesComponentsAndWritesDefaultOutput()
    {
        using var dir = new TempDir();
        string model = Path.Combine(dir.Path, "a.nwc");
        NwcDocument.Create().Save(model);
        string nwf = Path.Combine(dir.Path, "fed.nwf");
        WriteNwfWithXRefs(nwf, model);

        var report = Run([nwf, Path.Combine(dir.Path, "nwf.json"), "--report", "--bvh-stats"]);
        Assert.Equal(0, report.ExitCode);
        Assert.Contains("=== NwcIO NWF Assembly Report ===", report.StdErr);
        Assert.Contains("Loaded Models:    2 (component files: 1)", report.StdErr);
        Assert.DoesNotContain("Warning: unresolved reference", report.StdErr);
        Assert.Contains("BVH Nodes:", report.StdErr);

        var summary = Run([nwf]);
        Assert.Equal(0, summary.ExitCode);
        Assert.StartsWith("NWF Assembly '", summary.StdOut);
        string expected = Path.ChangeExtension(nwf, ".glb");
        Assert.Contains($"exported to '{expected}'", summary.StdOut);
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void Nwf_WithMissingComponent_WarnsInReport()
    {
        using var dir = new TempDir();
        string missing = Path.Combine(dir.Path, "gone.nwc");
        string nwf = Path.Combine(dir.Path, "fed.nwf");
        WriteNwfWithXRefs(nwf, missing);

        var report = Run([nwf, Path.Combine(dir.Path, "nwf.json"), "--report"]);
        Assert.Equal(0, report.ExitCode);
        Assert.Contains("Loaded Models:    1 (component files: 0)", report.StdErr);
        Assert.Contains("Warning: unresolved reference, component model missing: '" + missing.Replace('\\', '/') + "'", report.StdErr);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nwcio-pipeline-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        int exit = NwcIO.Cli.Program.Run(args, stdoutStream: null, outWriter: stdout, errorWriter: stderr);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private static void WriteNwfWithXRefs(string nwfPath, params string[] effectivePaths)
    {
        string table = string.Join(",", effectivePaths.Select(p =>
            "{\"OriginalPath\":\"" + p.Replace('\\', '/') + "\",\"RemappedPath\":\"\"}"));
        string json = "{\"Type\":\"XRefTable\",\"Version\":1,\"SavedFilename\":\"fed.nwf\",\"Table\":[" + table + "]}";
        byte[] xrefStream = System.Text.Encoding.UTF8.GetBytes(json);
        using var fs = File.Create(nwfPath);
        NwcContainerWriter.Write(fs, [xrefStream]);
    }
}
