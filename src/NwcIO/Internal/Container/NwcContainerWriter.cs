using System.IO.Compression;
using System.Text;

namespace NwcIO.Internal.Container;

/// <summary>
/// Writes the LcUStream container of NWC/NWD files:
/// ASCII section header line followed by back-to-back zlib compressed streams.
/// </summary>
internal static class NwcContainerWriter
{
    public const string DefaultHeader = "#LcUStream-V1.1 binary double release utf8 navisworks:448 lizlib\n";

    public static void Write(
        Stream output,
        IReadOnlyList<byte[]> decompressedStreams,
        string? header = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(decompressedStreams);
        cancellationToken.ThrowIfCancellationRequested();

        output.Write(EncodeHeader(header));

        for (int i = 0; i < decompressedStreams.Count; i++)
        {
            byte[] streamData = decompressedStreams[i];
            using var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
            int offset = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(64 * 1024, streamData.Length - offset);
                zlib.Write(streamData, offset, count);
                offset += count;
            }
            while (offset < streamData.Length);
        }

        cancellationToken.ThrowIfCancellationRequested();
        output.Flush();
    }

    public static async ValueTask WriteAsync(
        Stream output,
        IReadOnlyList<byte[]> decompressedStreams,
        string? header,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await output.WriteAsync(EncodeHeader(header), cancellationToken).ConfigureAwait(false);

        var destination = new CompressionOutput(output, cancellationToken);
        foreach (byte[] streamData in decompressedStreams)
        {
            var zlib = new ZLibStream(destination, CompressionLevel.Optimal, leaveOpen: true);
            await using (zlib.ConfigureAwait(false))
            {
                int offset = 0;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(64 * 1024, streamData.Length - offset);
                    await zlib.WriteAsync(streamData.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                    offset += count;
                }
                while (offset < streamData.Length);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static byte[] EncodeHeader(string? header)
    {
        ValidateHeader(header);
        header ??= DefaultHeader;
        return Encoding.ASCII.GetBytes(header.EndsWith('\n') ? header : header + '\n');
    }

    // ZLibStream.DisposeAsync writes the trailer without a token.
    private sealed class CompressionOutput(Stream output, CancellationToken operationToken) : Stream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            operationToken.ThrowIfCancellationRequested();
            return output.WriteAsync(buffer, operationToken);
        }

        public override bool CanWrite => output.CanWrite;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    internal static void ValidateHeader(string? header)
    {
        if (header is not null && !header.StartsWith("#LcUStream", StringComparison.Ordinal))
        {
            throw new ArgumentException("Header must start with '#LcUStream'.", nameof(header));
        }
    }

    public static byte[] Write(IReadOnlyList<byte[]> decompressedStreams, string? header = null)
    {
        using var ms = new MemoryStream();
        Write(ms, decompressedStreams, header);
        return ms.ToArray();
    }
}
