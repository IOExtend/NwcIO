using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class ExportCancellationTests
{
    [Fact]
    public void DocumentExports_CanceledToken_ThrowOperationCanceled()
    {
        var document = Document();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            document.SaveGlb(Path.GetTempFileName(), true, null, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() =>
            document.SaveJson(Path.GetTempFileName(), null, null, cancellation.Token));
    }

    [Fact]
    public void DocumentExports_ReportProgress()
    {
        var document = Document();
        var json = new CollectingProgress();
        using var jsonStream = new MemoryStream();
        document.SaveJson(jsonStream, null, json, CancellationToken.None);
        Assert.Contains(json.Reports, p => p.Phase == "export-json" && p.Completed == 1 && p.Total == 1);

        var glb = new CollectingProgress();
        using var glbStream = new MemoryStream();
        document.SaveGlb(glbStream, true, glb, CancellationToken.None);
        Assert.Contains(glb.Reports, p => p.Phase == "export-glb" && p.Completed == 1 && p.Total == 1);
    }

    [Fact]
    public void AssemblyExports_CancelAndReportProgress()
    {
        var document = Document();
        var assembly = NwcAssembly.Create("Fed", document);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            assembly.SaveGlb(Path.GetTempFileName(), true, null, cancellation.Token));

        var progress = new CollectingProgress();
        using var stream = new MemoryStream();
        assembly.SaveJson(stream, null, progress, CancellationToken.None);
        Assert.Contains(progress.Reports, p => p.Phase == "export-json" && p.Completed == 1 && p.Total == 1);
    }

    [Fact]
    public void AssemblySaveJson_CanceledToken_DoesNotTruncateExistingFile()
    {
        var assembly = NwcAssembly.Create("Fed", Document());
        string path = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "keep me");
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                assembly.SaveJson(path, null, null, cancellation.Token));

            Assert.Equal("keep me", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AssemblySaveJson_ProgressCanCancelMidExport()
    {
        var assembly = NwcAssembly.Create("Fed", Document());
        string path = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "keep me");
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(cancellation);
        try
        {
            Assert.Throws<OperationCanceledException>(() =>
                assembly.SaveJson(path, null, progress, cancellation.Token));

            Assert.Equal("keep me", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AssemblySaveJson_ProgressCancelsBeforeWrite_WritesNothing()
    {
        var assembly = NwcAssembly.Create("Fed", Document());
        using var cancellation = new CancellationTokenSource();
        using var stream = new MemoryStream();

        Assert.Throws<OperationCanceledException>(() =>
            assembly.SaveJson(stream, null, new CancellingProgress(cancellation), cancellation.Token));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void AssemblyBcfStream_ProgressCancelsBeforeWrite_WritesNothing()
    {
        var assembly = NwcAssembly.Create("Fed", Document());
        using var cancellation = new CancellationTokenSource();
        using var stream = new MemoryStream();

        Assert.Throws<OperationCanceledException>(() =>
            assembly.SaveBcf(stream, [], new CancellingProgress(cancellation), cancellation.Token));

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void AssemblyBcfAndHtmlFiles_ProgressCancelsBeforeWrite_CreateNoFile()
    {
        var assembly = NwcAssembly.Create("Fed", Document());
        string bcf = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.bcfzip");
        string html = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.html");
        try
        {
            using (var cancellation = new CancellationTokenSource())
            {
                Assert.Throws<OperationCanceledException>(() =>
                    assembly.SaveBcf(bcf, [], new CancellingProgress(cancellation), cancellation.Token));
            }

            using (var cancellation = new CancellationTokenSource())
            {
                Assert.Throws<OperationCanceledException>(() =>
                    assembly.SaveClashReportHtml(html, [], new CancellingProgress(cancellation), cancellation.Token));
            }

            Assert.False(File.Exists(bcf));
            Assert.False(File.Exists(html));
        }
        finally
        {
            if (File.Exists(bcf)) File.Delete(bcf);
            if (File.Exists(html)) File.Delete(html);
        }
    }

    [Fact]
    public async Task SaveJsonAsync_CanceledWhileWriting_IsCanceled()
    {
        var document = Document();
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancelOnWriteStream(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            document.SaveJsonAsync(stream, cancellationToken: cancellation.Token));

        Assert.True(stream.Written);
    }

    private static NwcDocument Document() => NwcDocument.Create(meshes:
    [
        new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
        },
    ]);

    private sealed class CollectingProgress : IProgress<NwcProgress>
    {
        public List<NwcProgress> Reports { get; } = [];

        public void Report(NwcProgress value) => Reports.Add(value);
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<NwcProgress>
    {
        public void Report(NwcProgress value) => cancellation.Cancel();
    }

    private sealed class CancelOnWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public bool Written { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Written = true;
            cancellation.Cancel();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Written = true;
            cancellation.Cancel();
            base.Write(buffer);
        }
    }
}
