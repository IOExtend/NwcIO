using System.Text;
using System.Text.Json;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class CliStdoutJsonTests
{
    private static string TempFile(string extension) =>
        Path.Combine(Path.GetTempPath(), $"nwcio-stdout-test-{Guid.NewGuid():N}{extension}");

    [Fact]
    public void ExportToStdout_ProducesPureValidJsonAndDirectsSummaryToStderr()
    {
        string nwcPath = TempFile(".nwc");
        try
        {
            var doc = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "TestModel.rvt" },
                meshes:
                [
                    new NwcMesh
                    {
                        Id = 1,
                        Vertices = [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)],
                        Indices = [0, 1, 2]
                    }
                ]);
            doc.Save(nwcPath);

            using var stdoutStream = new MemoryStream();
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            int exitCode = NwcIO.Cli.Program.Run([nwcPath, "-"], stdoutStream, outWriter, errWriter);

            Assert.Equal(0, exitCode);
            Assert.Empty(outWriter.ToString());

            string errText = errWriter.ToString();
            Assert.Contains("Exported 0 records", errText);
            Assert.Contains("to '-'", errText);

            byte[] jsonBytes = stdoutStream.ToArray();
            Assert.NotEmpty(jsonBytes);

            using var jsonDoc = JsonDocument.Parse(jsonBytes);
            Assert.Equal(JsonValueKind.Object, jsonDoc.RootElement.ValueKind);
            Assert.True(jsonDoc.RootElement.TryGetProperty("file", out var fileElem));
            Assert.Equal("TestModel.rvt", fileElem.GetProperty("sourcePath").GetString());
        }
        finally
        {
            if (File.Exists(nwcPath)) File.Delete(nwcPath);
        }
    }

    [Fact]
    public void ExportToStdout_WithDiffAndRaycast_PayloadIsPureJson()
    {
        string baselinePath = TempFile(".nwc");
        string revisionPath = TempFile(".nwc");
        try
        {
            var baseDoc = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "Base.rvt" },
                meshes:
                [
                    new NwcMesh
                    {
                        Id = 1,
                        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                        Indices = [0, 1, 2]
                    }
                ]);
            baseDoc.Save(baselinePath);

            var revDoc = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "Rev.rvt" },
                meshes:
                [
                    new NwcMesh
                    {
                        Id = 1,
                        Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)],
                        Indices = [0, 1, 2]
                    }
                ]);
            revDoc.Save(revisionPath);

            using var stdoutStream = new MemoryStream();
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            int exitCode = NwcIO.Cli.Program.Run(
                [revisionPath, "-", "--diff", baselinePath, "--raycast", "0,0,-1,0,0,1", "--viewpoints"],
                stdoutStream, outWriter, errWriter);

            Assert.Equal(0, exitCode);
            Assert.Empty(outWriter.ToString());

            string errText = errWriter.ToString();
            Assert.Contains("Model Revision Comparison", errText);
            Assert.Contains("Raycast", errText);
            Assert.Contains("Saved Viewpoints", errText);
            Assert.Contains("Exported 0 records", errText);

            byte[] jsonBytes = stdoutStream.ToArray();
            using var jsonDoc = JsonDocument.Parse(jsonBytes);
            Assert.Equal(JsonValueKind.Object, jsonDoc.RootElement.ValueKind);
        }
        finally
        {
            if (File.Exists(baselinePath)) File.Delete(baselinePath);
            if (File.Exists(revisionPath)) File.Delete(revisionPath);
        }
    }

    [Fact]
    public void ExportToFile_WritesSummaryToStdout()
    {
        string nwcPath = TempFile(".nwc");
        string jsonPath = TempFile(".json");
        try
        {
            var doc = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "TestModel.rvt" });
            doc.Save(nwcPath);

            using var stdoutStream = new MemoryStream();
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            int exitCode = NwcIO.Cli.Program.Run([nwcPath, jsonPath], stdoutStream, outWriter, errWriter);

            Assert.Equal(0, exitCode);
            Assert.Contains("Exported 0 records", outWriter.ToString());
            Assert.Empty(errWriter.ToString());
            Assert.True(File.Exists(jsonPath));
        }
        finally
        {
            if (File.Exists(nwcPath)) File.Delete(nwcPath);
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
        }
    }

    [Fact]
    public void ExportFolderToStdout_ProducesPureValidJsonAndDirectsSummaryToStderr()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nwcio-folder-stdout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var doc = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "M1.rvt" },
                meshes:
                [
                    new NwcMesh
                    {
                        Id = 1,
                        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                        Indices = [0, 1, 2]
                    }
                ]);
            doc.Save(Path.Combine(dir, "model1.nwc"));

            using var stdoutStream = new MemoryStream();
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            int exitCode = NwcIO.Cli.Program.Run([dir, "-"], stdoutStream, outWriter, errWriter);

            Assert.Equal(0, exitCode);
            Assert.Empty(outWriter.ToString());
            Assert.False(File.Exists(Path.Combine(dir, "-")));
            Assert.False(File.Exists("-"));

            string errText = errWriter.ToString();
            Assert.Contains("Assembly '", errText);
            Assert.Contains("exported to '-'", errText);

            byte[] jsonBytes = stdoutStream.ToArray();
            Assert.NotEmpty(jsonBytes);

            using var jsonDoc = JsonDocument.Parse(jsonBytes);
            Assert.Equal(JsonValueKind.Object, jsonDoc.RootElement.ValueKind);
            Assert.Equal(1, jsonDoc.RootElement.GetProperty("modelCount").GetInt32());
            Assert.Equal(1, jsonDoc.RootElement.GetProperty("totalMeshes").GetInt32());
        }
        finally
        {
            if (File.Exists("-")) File.Delete("-");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ExportNwfToStdout_ProducesPureValidJsonAndDirectsSummaryToStderr()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nwcio-nwf-stdout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var comp = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "Comp.rvt" },
                meshes:
                [
                    new NwcMesh
                    {
                        Id = 1,
                        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                        Indices = [0, 1, 2]
                    }
                ]);
            string compPath = Path.Combine(dir, "Comp.nwc");
            comp.Save(compPath);

            string nwfPath = Path.Combine(dir, "fed.nwf");
            string tableJson = "{\"Type\":\"XRefTable\",\"Version\":1,\"SavedFilename\":\"fed.nwf\",\"Table\":[{\"OriginalPath\":\"Comp.nwc\",\"RemappedPath\":\"\"}]}";
            byte[] xrefStream = Encoding.UTF8.GetBytes(tableJson);
            using (var fs = File.Create(nwfPath))
            {
                NwcIO.Internal.Container.NwcContainerWriter.Write(fs, [xrefStream]);
            }

            using var stdoutStream = new MemoryStream();
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            int exitCode = NwcIO.Cli.Program.Run([nwfPath, "-"], stdoutStream, outWriter, errWriter);

            Assert.Equal(0, exitCode);
            Assert.Empty(outWriter.ToString());
            Assert.False(File.Exists(Path.Combine(dir, "-")));
            Assert.False(File.Exists("-"));

            string errText = errWriter.ToString();
            Assert.Contains("NWF Assembly '", errText);
            Assert.Contains("exported to '-'", errText);

            byte[] jsonBytes = stdoutStream.ToArray();
            Assert.NotEmpty(jsonBytes);

            using var jsonDoc = JsonDocument.Parse(jsonBytes);
            Assert.Equal(JsonValueKind.Object, jsonDoc.RootElement.ValueKind);
            Assert.Equal(2, jsonDoc.RootElement.GetProperty("modelCount").GetInt32());
        }
        finally
        {
            if (File.Exists("-")) File.Delete("-");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
