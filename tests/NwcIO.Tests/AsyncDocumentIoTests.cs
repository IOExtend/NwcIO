using System.Text;
using NwcIO.Internal;
using NwcIO.Internal.Container;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class AsyncDocumentIoTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenAsync_CancelsPendingRead(bool seekable)
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new AsyncOnlyStream(NwcDocument.Create().ToByteArray(), seekable) { BlockAt = 1 };

        var open = NwcDocument.OpenAsync(stream, cancellationToken: cancellation.Token);
        await AssertPendingCancellation(open, stream, cancellation);

        Assert.True(stream.CanRead);
        Assert.Equal(1, stream.Calls);
    }

    [Fact]
    public async Task OpenAsync_DeferredReadCanBeCancelledAfterContainerScan()
    {
        var payload = new byte[192 * 1024];
        new Random(111).NextBytes(payload);
        using var cancellation = new CancellationTokenSource();
        using var stream = new AsyncOnlyStream(NwcContainerWriter.Write([payload]));
        var progress = new CallbackProgress(report =>
        {
            if (report.Phase == "read-container")
            {
                stream.BlockAt = stream.Calls + 1;
            }
        });

        var open = NwcDocument.OpenAsync(stream, new NwcReaderOptions { KeepStreamCatalog = false },
            progress: progress, cancellationToken: cancellation.Token);
        await AssertPendingCancellation(open, stream, cancellation);

        Assert.True(stream.CanRead);
        Assert.Equal(stream.BlockAt, stream.Calls);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task OpenAsync_ReadsStrictGeometryFromAsyncOnlyStream(bool seekable, bool keepCatalog)
    {
        byte[] bytes = SyntheticNwcFile.Build(FormatVectors.H1, FormatVectors.F1,
            [FormatVectors.R1[..60], FormatVectors.R1[60..]], [120]);
        using var input = new AsyncOnlyStream(bytes, seekable);
        using var reference = new MemoryStream(bytes);
        var options = new NwcReaderOptions { KeepStreamCatalog = keepCatalog };
        var expected = NwcDocument.Open(reference, options);

        var actual = await NwcDocument.OpenAsync(input, options);

        Assert.Equal(2, actual.Meshes.Count);
        Assert.Equal(expected.ToJson(), actual.ToJson());
        Assert.True(input.CanRead);
        if (seekable)
        {
            Assert.Equal(input.Length, input.Position);
        }
    }

    [Fact]
    public async Task OpenAsync_EnforcesDecompressionLimits()
    {
        using var input = new AsyncOnlyStream(NwcContainerWriter.Write([new byte[1024]]));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NwcDocument.OpenAsync(input,
            new NwcReaderOptions { MaxDecompressedStreamBytes = 512 }));

        Assert.Contains("per-stream limit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_CancelsPendingWrite(bool json)
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new AsyncOnlyStream { BlockAt = 2 };
        var document = LargeDocument();
        var progress = new List<NwcProgress>();

        Task save = json
            ? document.SaveJsonAsync(stream, progress: new CallbackProgress(progress.Add), cancellationToken: cancellation.Token)
            : document.SaveAsync(stream, cancellationToken: cancellation.Token);
        await AssertPendingCancellation(save, stream, cancellation);

        Assert.True(stream.CanWrite);
        Assert.Equal(2, stream.Calls);
        Assert.True(stream.Length > 0);
        if (json)
        {
            Assert.Equal(0, Assert.Single(progress).Completed);
        }
    }

    [Fact]
    public async Task SaveAsync_CancelsWriteFromCompressionDisposal()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new AsyncOnlyStream { BlockAt = 2 };

        // A small payload fits within zlib's buffer. The second write comes from DisposeAsync.
        var save = NwcDocument.Create().SaveAsync(stream, cancellationToken: cancellation.Token);
        await AssertPendingCancellation(save, stream, cancellation);
    }

    [Fact]
    public async Task AsyncSaves_MatchSynchronousOutput()
    {
        var document = LargeDocument();
        using var nwc = new AsyncOnlyStream();
        using var json = new AsyncOnlyStream();

        await document.SaveAsync(nwc);
        await document.SaveJsonAsync(json);

        Assert.Equal(document.ToByteArray(), nwc.ToArray());
        Assert.Equal(Encoding.UTF8.GetBytes(document.ToJson()), json.ToArray());
        Assert.True(nwc.Flushed && json.Flushed);
        Assert.True(nwc.CanWrite && json.CanWrite);
    }

    [Fact]
    public async Task AtomicWriteAsync_CancellationPreservesDestinationAndRemovesTemporaryFile()
    {
        string directory = Directory.CreateTempSubdirectory("nwcio-async-").FullName;
        string path = Path.Combine(directory, "model.nwc");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            using var cancellation = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                AtomicFileWriter.WriteAsync(path, async output =>
                {
                    Assert.True(Assert.IsType<FileStream>(output).IsAsync);
                    await output.WriteAsync(new byte[] { 4, 5, 6 }, cancellation.Token);
                    cancellation.Cancel();
                }, cancellation.Token).AsTask());

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(directory)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertPendingCancellation(Task operation, AsyncOnlyStream stream, CancellationTokenSource cancellation)
    {
        try
        {
            var blocked = stream.Blocked.Task;
            Assert.Same(blocked, await Task.WhenAny(blocked, operation).WaitAsync(Timeout));
            Assert.False(operation.IsCompleted);
            Assert.Equal(cancellation.Token, await blocked);
        }
        finally
        {
            cancellation.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(Timeout));
        Assert.True(operation.IsCanceled);
    }

    private static NwcDocument LargeDocument() => NwcDocument.Create(transforms:
        Enumerable.Range(0, 4000).Select(i => new NwcTransform { Id = i, Translation = new(i, 2, 3) }).ToArray());

    private sealed class CallbackProgress(Action<NwcProgress> report) : IProgress<NwcProgress>
    {
        public void Report(NwcProgress value) => report(value);
    }

    private sealed class AsyncOnlyStream(byte[]? bytes = null, bool seekable = true) : Stream
    {
        private readonly MemoryStream _data = bytes is null ? new MemoryStream() : new MemoryStream(bytes);
        public int Calls { get; private set; }
        public int BlockAt { get; set; }
        public bool Flushed { get; private set; }
        public TaskCompletionSource<CancellationToken> Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public byte[] ToArray() => _data.ToArray();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await BeforeIo(cancellationToken);
            return _data.Read(buffer.Span[..Math.Min(buffer.Length, 8192)]);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await BeforeIo(cancellationToken);
            _data.Write(buffer.Span);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Flushed = true;
            return Task.CompletedTask;
        }

        private async ValueTask BeforeIo(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++Calls == BlockAt)
            {
                Blocked.SetResult(cancellationToken);
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        public override bool CanRead => _data.CanRead;
        public override bool CanSeek => seekable && _data.CanSeek;
        public override bool CanWrite => _data.CanWrite;
        public override long Length => seekable ? _data.Length : throw new NotSupportedException();
        public override long Position
        {
            get => seekable ? _data.Position : throw new NotSupportedException();
            set
            {
                if (!seekable) throw new NotSupportedException();
                _data.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous read");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous write");
        public override void Flush() => throw new NotSupportedException("Synchronous flush");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _data.Dispose();
            base.Dispose(disposing);
        }
    }
}
