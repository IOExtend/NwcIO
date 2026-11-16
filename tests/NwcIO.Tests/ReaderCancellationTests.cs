using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class ReaderCancellationTests
{
    [Fact]
    public void Open_CanceledToken_ThrowsOperationCanceled()
    {
        using var stream = new MemoryStream(DocumentBytes());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            NwcDocument.Open(stream, null, null, null, cancellation.Token));
    }

    [Fact]
    public void Open_ReportsPhaseProgress()
    {
        var progress = new CollectingProgress();
        using var stream = new MemoryStream(DocumentBytes());

        _ = NwcDocument.Open(stream, null, null, progress, CancellationToken.None);

        Assert.Contains(progress.Reports, p => p.Phase == "read-container");
        Assert.Contains(progress.Reports, p => p.Phase == "read-geometry");
        Assert.Contains(progress.Reports, p => p.Phase == "open");
    }

    [Fact]
    public void LoadFromDirectory_CanceledToken_ThrowsOperationCanceled()
    {
        string directory = TempDirectory();
        try
        {
            NwcDocument.Create().Save(Path.Combine(directory, "a.nwc"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                NwcAssembly.LoadFromDirectory(directory, "*.nwc", null, null, cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadFromDirectory_ReportsPerModelProgress()
    {
        string directory = TempDirectory();
        try
        {
            NwcDocument.Create().Save(Path.Combine(directory, "a.nwc"));
            NwcDocument.Create().Save(Path.Combine(directory, "b.nwc"));
            var progress = new CollectingProgress();

            var assembly = NwcAssembly.LoadFromDirectory(directory, "*.nwc", null, progress, CancellationToken.None);

            Assert.Equal(2, assembly.Models.Count);
            Assert.Contains(progress.Reports, p => p.Phase == "load-models" && p.Completed == 2 && p.Total == 2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenNwf_CanceledToken_ThrowsOperationCanceled()
    {
        string path = Path.Combine(Path.GetTempPath(), "nwcio-cancel-" + Guid.NewGuid().ToString("N") + ".nwf");
        File.WriteAllBytes(path, []);
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                NwcAssembly.OpenNwf(path, null, null, cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenAsyncAndSaveJsonAsync_Roundtrip()
    {
        string nwc = Path.Combine(Path.GetTempPath(), "nwcio-async-" + Guid.NewGuid().ToString("N") + ".nwc");
        string json = Path.Combine(Path.GetTempPath(), "nwcio-async-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            NwcDocument.Create().Save(nwc);

            var document = await NwcDocument.OpenAsync(nwc);
            await document.SaveJsonAsync(json);

            Assert.True(File.Exists(json));
        }
        finally
        {
            if (File.Exists(nwc)) File.Delete(nwc);
            if (File.Exists(json)) File.Delete(json);
        }
    }

    private static byte[] DocumentBytes()
    {
        using var stream = new MemoryStream();
        NwcDocument.Create().Save(stream);
        return stream.ToArray();
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nwcio-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class CollectingProgress : IProgress<NwcProgress>
    {
        public List<NwcProgress> Reports { get; } = [];

        public void Report(NwcProgress value) => Reports.Add(value);
    }
}
