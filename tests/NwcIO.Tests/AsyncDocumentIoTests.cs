using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NwcIO.Internal;
using NwcIO.Internal.Container;
using NwcIO.Model;
using NwcIO.Model.Viewer;
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

    [Theory]
    [InlineData("obj")]
    [InlineData("glb")]
    [InlineData("ifc")]
    [InlineData("bcf")]
    [InlineData("xml")]
    [InlineData("html")]
    public async Task CompatibilityExports_CancelWhileAwaitingDestinationWrite(string format)
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new AsyncOnlyStream(seekable: false) { BlockAt = 1 };
        var document = CompatibilityDocument();

        await AssertPendingCancellation(ExportToStream(document, format, output, cancellation.Token), output, cancellation);

        Assert.True(output.CanWrite);
        Assert.Equal(1, output.Calls);
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("glb")]
    [InlineData("ifc")]
    [InlineData("bcf")]
    [InlineData("xml")]
    [InlineData("html")]
    public async Task CompatibilityExports_PreCancelled_PreserveExistingOutput(string format)
    {
        var directory = Directory.CreateTempSubdirectory("nwcio-cancel-export-");
        string path = Path.Combine(directory.FullName, "model." + format);
        try
        {
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, original);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using var output = new AsyncOnlyStream();
            var document = CompatibilityDocument();

            var file = ExportToPath(document, format, path, cancellation.Token);
            var stream = ExportToStream(document, format, output, cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => file);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream);

            Assert.True(file.IsCanceled && stream.IsCanceled);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(directory.FullName)));
            Assert.Equal(0, output.Calls);
            Assert.True(output.CanWrite);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledGeometryExport_PreservesExistingDestination(bool glb)
    {
        var directory = Directory.CreateTempSubdirectory("nwcio-cancel-geometry-");
        string path = Path.Combine(directory.FullName, glb ? "model.glb" : "model.obj");
        try
        {
            byte[] original = [1, 2, 3];
            File.WriteAllBytes(path, original);
            using var cancellation = new CancellationTokenSource();
            var vertices = new CancellingVertices(cancellation);
            var doc = NwcDocument.Create(meshes: [new NwcMesh { Id = 1, Vertices = vertices, Indices = [0, 1, 2] }]);

            Task save = glb
                ? doc.SaveGlbAsync(path, worldCoordinates: false, cancellationToken: cancellation.Token)
                : doc.SaveObjAsync(path, worldCoordinates: false, cancellationToken: cancellation.Token);
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save.WaitAsync(Timeout));

            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.True(save.IsCanceled);
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(directory.FullName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("glb")]
    [InlineData("ifc")]
    [InlineData("bcf")]
    [InlineData("xml")]
    [InlineData("html")]
    public async Task CompatibilityExports_ProduceEquivalentPathAndStreamOutput(string format)
    {
        var directory = Directory.CreateTempSubdirectory("nwcio-async-export-");
        string path = Path.Combine(directory.FullName, "model." + format);
        try
        {
            var document = CompatibilityDocument();
            switch (format)
            {
                case "obj": document.SaveObj(path, worldCoordinates: false); break;
                case "glb": document.SaveGlb(path, worldCoordinates: false); break;
                case "ifc": document.SaveIfc(path); break;
                case "bcf": document.SaveViewpointsBcf(path); break;
                case "xml": document.SaveViewpointsXml(path, units: "ft"); break;
                case "html": document.SaveHtmlViewer(path, new NwcHtmlViewerOptions { Title = "Async viewer" }); break;
            }

            byte[] expected = File.ReadAllBytes(path);
            using var output = new AsyncOnlyStream(seekable: false);
            await ExportToPath(document, format, path, default);
            await ExportToStream(document, format, output, default);

            byte[] actual = File.ReadAllBytes(path);
            switch (format)
            {
                case "bcf":
                    foreach (byte[] bytes in new[] { expected, actual, output.ToArray() })
                    {
                        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
                        BcfSchemaAssert.ValidArchive(archive, 1);
                    }
                    break;
                case "ifc":
                    Assert.Contains("FILE_NAME('model.ifc'", Encoding.UTF8.GetString(actual), StringComparison.Ordinal);
                    Assert.Equal(NormalizeIfc(expected), NormalizeIfc(actual));
                    Assert.Equal(NormalizeIfc(expected), NormalizeIfc(output.ToArray()));
                    break;
                default:
                    Assert.Equal(expected, actual);
                    Assert.Equal(expected, output.ToArray());
                    break;
            }

            Assert.True(output.CanWrite && output.Flushed);
            Assert.Equal(path, Assert.Single(Directory.GetFiles(directory.FullName)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextCompatibilityExports_UseAsyncWriterAndLeaveItOpen(bool ifc)
    {
        var doc = CompatibilityDocument();
        using var writer = new AsyncOnlyTextWriter { NewLine = "\n" };
        using var expected = new StringWriter { NewLine = "\n" };
        if (ifc)
        {
            doc.SaveIfc(expected);
            await doc.SaveIfcAsync(writer);
            Assert.Equal(NormalizeIfc(Encoding.UTF8.GetBytes(expected.ToString())),
                NormalizeIfc(Encoding.UTF8.GetBytes(writer.ToString())));
        }
        else
        {
            doc.SaveObj(expected);
            await doc.SaveObjAsync(writer);
            Assert.Equal(expected.ToString(), writer.ToString());
        }

        Assert.True(writer.Flushed);
        Assert.False(writer.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextCompatibilityExports_CancelWhileAwaitingWrite(bool ifc)
    {
        var doc = CompatibilityDocument();
        using var cancellation = new CancellationTokenSource();
        using var writer = new AsyncOnlyTextWriter { BlockWrites = true };

        var save = ifc
            ? doc.SaveIfcAsync(writer, cancellationToken: cancellation.Token)
            : doc.SaveObjAsync(writer, cancellationToken: cancellation.Token);
        await AssertPendingCancellation(save, writer.Blocked.Task, cancellation);

        Assert.False(writer.Disposed);
        Assert.False(writer.Flushed);
    }

    private static string NormalizeIfc(byte[] bytes)
    {
        // STEP headers include the target filename and a clock timestamp, and owner history includes an epoch timestamp.
        var text = Encoding.UTF8.GetString(bytes);
        text = Regex.Replace(text, "^FILE_NAME.*$", "FILE_NAME", RegexOptions.Multiline);
        return Regex.Replace(text, @"^([^\r\n]*=IFCOWNERHISTORY\(.*),[0-9]+\);", "$1,0);", RegexOptions.Multiline);
    }

    private static NwcDocument CompatibilityDocument() => NwcDocument.Create(
        fileInfo: new NwcFileInfo { SourcePath = "model.nwc" },
        meshes:
        [
            new NwcMesh
            {
                Id = 1,
                Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)],
                Indices = [0, 1, 2],
                Transform = new NwcTransform { Id = 1, Translation = new(1, 2, 3) },
            },
        ],
        savedViewpoints: [NwcSavedViewpoint.CreateDefault(new BoundingBox3d(new(0, 0, 0), new(3, 3, 3)), "Coordination")]);

    private static Task ExportToStream(NwcDocument document, string format, Stream stream, CancellationToken token) => format switch
    {
        "obj" => document.SaveObjAsync(stream, worldCoordinates: false, cancellationToken: token),
        "glb" => document.SaveGlbAsync(stream, worldCoordinates: false, cancellationToken: token),
        "ifc" => document.SaveIfcAsync(stream, cancellationToken: token),
        "bcf" => document.SaveViewpointsBcfAsync(stream, token),
        "xml" => document.SaveViewpointsXmlAsync(stream, units: "ft", cancellationToken: token),
        "html" => document.SaveHtmlViewerAsync(stream, new NwcHtmlViewerOptions { Title = "Async viewer" }, token),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static Task ExportToPath(NwcDocument document, string format, string path, CancellationToken token) => format switch
    {
        "obj" => document.SaveObjAsync(path, worldCoordinates: false, cancellationToken: token),
        "glb" => document.SaveGlbAsync(path, worldCoordinates: false, cancellationToken: token),
        "ifc" => document.SaveIfcAsync(path, cancellationToken: token),
        "bcf" => document.SaveViewpointsBcfAsync(path, token),
        "xml" => document.SaveViewpointsXmlAsync(path, units: "ft", cancellationToken: token),
        "html" => document.SaveHtmlViewerAsync(path, new NwcHtmlViewerOptions { Title = "Async viewer" }, token),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private sealed class CancellingVertices(CancellationTokenSource cancellation) : IReadOnlyList<Vector3d>
    {
        public int Count => 8192;

        public Vector3d this[int index]
        {
            get
            {
                if (index == Count - 2) cancellation.Cancel();
                return new Vector3d(index, index % 3, 0);
            }
        }

        public IEnumerator<Vector3d> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class AsyncOnlyTextWriter : TextWriter
    {
        private readonly StringBuilder _text = new();
        public override Encoding Encoding => Encoding.UTF8;
        public bool Flushed { get; private set; }
        public bool Disposed { get; private set; }
        public bool BlockWrites { get; init; }
        public TaskCompletionSource<CancellationToken> Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Write(char value) => throw new NotSupportedException("Synchronous write");
        public override void Flush() => throw new NotSupportedException("Synchronous flush");

        public override async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BlockWrites)
            {
                Blocked.SetResult(cancellationToken);
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            }

            _text.Append(buffer.Span);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Flushed = true;
            return Task.CompletedTask;
        }

        public override string ToString() => _text.ToString();

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    private static Task AssertPendingCancellation(Task operation, AsyncOnlyStream stream, CancellationTokenSource cancellation) =>
        AssertPendingCancellation(operation, stream.Blocked.Task, cancellation);

    private static async Task AssertPendingCancellation(Task operation, Task<CancellationToken> blocked, CancellationTokenSource cancellation)
    {
        try
        {
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
