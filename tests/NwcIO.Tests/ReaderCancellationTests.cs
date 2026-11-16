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
    public void Open_CanceledAfterReadContainer_StopsBeforeGeometry()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new CollectingProgress { OnReport = p => { if (p.Phase == "read-container") cancellation.Cancel(); } };
        using var stream = new MemoryStream(DocumentBytes());

        Assert.Throws<OperationCanceledException>(() =>
            NwcDocument.Open(stream, null, null, progress, cancellation.Token));

        Assert.Equal(["read-container"], progress.Reports.Select(p => p.Phase));
    }

    [Fact]
    public void LoadFromDirectory_CanceledBetweenModels_StopsAfterFirstModel()
    {
        string directory = TempDirectory();
        try
        {
            foreach (var name in new[] { "a.nwc", "b.nwc", "c.nwc" })
            {
                NwcDocument.Create().Save(Path.Combine(directory, name));
            }
            using var cancellation = new CancellationTokenSource();
            var progress = new CollectingProgress { OnReport = p => { if (p.Phase == "load-models") cancellation.Cancel(); } };

            Assert.Throws<OperationCanceledException>(() =>
                NwcAssembly.LoadFromDirectory(directory, "*.nwc", null, progress, cancellation.Token));

            Assert.Single(progress.Reports, p => p.Phase == "load-models");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenNwf_CanceledBetweenReferences_StopsBeforeNextReference()
    {
        string directory = TempDirectory();
        try
        {
            string nwf = Path.Combine(directory, "fed.nwf");
            WriteNwf(nwf, "missing.nwc", "a.nwc");
            NwcDocument.Create().Save(Path.Combine(directory, "a.nwc"));
            using var cancellation = new CancellationTokenSource();
            var progress = new CollectingProgress { OnReport = _ => cancellation.Cancel() };

            Assert.Throws<OperationCanceledException>(() =>
                NwcAssembly.OpenNwf(nwf, null, progress, cancellation.Token));

            // Only the primary document was reported: the unresolved reference was never visited.
            Assert.Single(progress.Reports);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenNwf_ProgressReachesTotalWhenReferencesAreNotLoaded()
    {
        string directory = TempDirectory();
        try
        {
            string nwf = Path.Combine(directory, "fed.nwf");
            WriteNwf(nwf, "a.nwc", "missing.nwc", "a.nwc", "tex.png");
            NwcDocument.Create().Save(Path.Combine(directory, "a.nwc"));
            File.WriteAllText(Path.Combine(directory, "tex.png"), "x");
            var progress = new CollectingProgress();

            var assembly = NwcAssembly.OpenNwf(nwf, null, progress, CancellationToken.None);

            Assert.Equal(2, assembly.Models.Count);
            Assert.Single(assembly.UnresolvedXRefs);
            Assert.Single(assembly.SkippedXRefs);
            Assert.Equal(
                [(1, 5), (2, 5), (3, 5), (4, 5), (5, 5)],
                progress.Reports.Where(p => p.Phase == "load-models").Select(p => (p.Completed, p.Total)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_CanceledToken_ReturnsCanceledTask()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var task = NwcDocument.OpenAsync(new MemoryStream(DocumentBytes()), cancellationToken: cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task OpenAsyncAndSaveAsync_Roundtrip()
    {
        string nwc = Path.Combine(Path.GetTempPath(), "nwcio-async-" + Guid.NewGuid().ToString("N") + ".nwc");
        string copy = Path.Combine(Path.GetTempPath(), "nwcio-async-" + Guid.NewGuid().ToString("N") + ".nwc");
        try
        {
            NwcDocument.Create().Save(nwc);

            var document = await NwcDocument.OpenAsync(nwc);
            await document.SaveAsync(copy);

            Assert.Equal(document.Meshes.Count, NwcDocument.Open(copy).Meshes.Count);
        }
        finally
        {
            if (File.Exists(nwc)) File.Delete(nwc);
            if (File.Exists(copy)) File.Delete(copy);
        }
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_LoadsModels()
    {
        string directory = TempDirectory();
        try
        {
            NwcDocument.Create().Save(Path.Combine(directory, "a.nwc"));

            var assembly = await NwcAssembly.LoadFromDirectoryAsync(directory);

            Assert.Single(assembly.Models);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OpenManifest_CanceledBetweenModels_StopsAfterFirstModel()
    {
        string directory = TempDirectory();
        try
        {
            NwcDocument.Create().Save(Path.Combine(directory, "a.nwc"));
            NwcDocument.Create().Save(Path.Combine(directory, "b.nwc"));
            var manifest = new NwcIO.Model.Clash.NwcNwfManifest("Fed");
            manifest.ModelPaths.Add("a.nwc");
            manifest.ModelPaths.Add("b.nwc");
            string path = Path.Combine(directory, "fed.nwf.xml");
            manifest.Save(path);
            using var cancellation = new CancellationTokenSource();
            var progress = new CollectingProgress { OnReport = _ => cancellation.Cancel() };

            Assert.Throws<OperationCanceledException>(() =>
                NwcAssembly.OpenManifest(path, null, progress, cancellation.Token));

            Assert.Single(progress.Reports);

            var complete = new CollectingProgress();
            Assert.Equal(2, NwcAssembly.OpenManifest(path, null, complete, CancellationToken.None).Models.Count);
            Assert.Equal((2, 2), (complete.Reports[^1].Completed, complete.Reports[^1].Total));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteNwf(string path, params string[] references)
    {
        string table = string.Join(",", references.Select(r => "{\"OriginalPath\":\"" + r + "\",\"RemappedPath\":\"\"}"));
        byte[] xrefStream = System.Text.Encoding.UTF8.GetBytes(
            "{\"Type\":\"XRefTable\",\"Version\":1,\"SavedFilename\":\"fed.nwf\",\"Table\":[" + table + "]}");
        using var stream = File.Create(path);
        NwcIO.Internal.Container.NwcContainerWriter.Write(stream, [xrefStream]);
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

        public Action<NwcProgress>? OnReport { get; init; }

        public void Report(NwcProgress value)
        {
            Reports.Add(value);
            OnReport?.Invoke(value);
        }
    }
}
