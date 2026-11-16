using NwcIO.Internal.Container;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class AssemblyPipelineTests
{
    [Fact]
    public void FolderAndNwf_UseTheSameReportPipeline()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nwcio-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string model = Path.Combine(directory, "a.nwc");
            NwcDocument.Create().Save(model);

            var folder = Run([directory, Path.Combine(directory, "folder.json"), "--report", "--bvh-stats"]);
            Assert.Equal(0, folder.ExitCode);
            Assert.Contains("=== NwcIO Assembly Report ===", folder.StdErr);
            Assert.Contains("Assembly:", folder.StdErr);
            Assert.Contains("Total Materials:", folder.StdErr);
            Assert.Contains("BVH Nodes:", folder.StdErr);

            string nwf = Path.Combine(directory, "fed.nwf");
            WriteNwfWithXRefs(nwf, model);

            var nwfRun = Run([nwf, Path.Combine(directory, "nwf.json"), "--report", "--bvh-stats"]);
            Assert.Equal(0, nwfRun.ExitCode);
            Assert.Contains("=== NwcIO NWF Assembly Report ===", nwfRun.StdErr);
            Assert.Contains("Loaded Models:", nwfRun.StdErr);
            Assert.Contains("Total Materials:", nwfRun.StdErr);
            Assert.Contains("BVH Nodes:", nwfRun.StdErr);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
