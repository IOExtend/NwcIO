using System.IO.Compression;

namespace NwcIO.Internal.Container;

/// <summary>
/// Reads the LcUStream container of NWC/NWD files:
/// ASCII section headers interleaved with back-to-back zlib streams
/// (with occasional non-zlib framing bytes between streams).
/// </summary>
internal static class NwcContainerReader
{
    private static ReadOnlySpan<byte> SectionMarker => "#LcUStream"u8;

    /// <summary>Default cap on the decompressed size of one stream: 512 MiB.</summary>
    public const long DefaultMaxDecompressedStreamBytes = 512L * 1024 * 1024;

    /// <summary>Default cap on the decompressed size of all streams of one container: 2 GiB.</summary>
    public const long DefaultMaxTotalDecompressedBytes = 2L * 1024 * 1024 * 1024;

    public static NwcContainer Read(byte[] data) =>
        Read(data, DefaultMaxDecompressedStreamBytes, DefaultMaxTotalDecompressedBytes);

    public static NwcContainer Read(byte[] data, long maxDecompressedStreamBytes, long maxTotalDecompressedBytes)
    {
        using var input = new MemoryStream(data, writable: false);
        return Read(input, maxDecompressedStreamBytes, maxTotalDecompressedBytes);
    }

    public static NwcContainer Read(Stream input) =>
        Read(input, DefaultMaxDecompressedStreamBytes, DefaultMaxTotalDecompressedBytes);

    public static NwcContainer Read(
        Stream input,
        long maxDecompressedStreamBytes,
        long maxTotalDecompressedBytes,
        CancellationToken cancellationToken = default) =>
        Read(input, maxDecompressedStreamBytes, maxTotalDecompressedBytes,
            deferPayloads: false, static container => container, cancellationToken);

    internal static T Read<T>(
        Stream input,
        long maxDecompressedStreamBytes,
        long maxTotalDecompressedBytes,
        bool deferPayloads,
        Func<NwcContainer, T> parse,
        CancellationToken cancellationToken)
    {
        if (maxDecompressedStreamBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDecompressedStreamBytes), "The per-stream decompression limit must be positive.");
        }

        if (maxTotalDecompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalDecompressedBytes), "The total decompression limit must be positive.");
        }

        if (input.CanSeek)
        {
            return ReadSeekable(input, maxDecompressedStreamBytes, maxTotalDecompressedBytes, deferPayloads, parse, cancellationToken);
        }

        string path = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.tmp");
        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var copy = new FileStream(path, fileOptions);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = input.Read(buffer, 0, buffer.Length);
            if (count == 0)
            {
                break;
            }

            copy.Write(buffer, 0, count);
        }

        copy.Position = 0;
        return ReadSeekable(copy, maxDecompressedStreamBytes, maxTotalDecompressedBytes, deferPayloads, parse, cancellationToken);
    }

    public static NwcContainer ReadFile(string path) =>
        ReadFile(path, DefaultMaxDecompressedStreamBytes, DefaultMaxTotalDecompressedBytes);

    public static NwcContainer ReadFile(string path, long maxDecompressedStreamBytes, long maxTotalDecompressedBytes)
    {
        using var input = File.OpenRead(path);
        return Read(input, maxDecompressedStreamBytes, maxTotalDecompressedBytes);
    }

    private static T ReadSeekable<T>(
        Stream input,
        long maxDecompressedStreamBytes,
        long maxTotalDecompressedBytes,
        bool deferPayloads,
        Func<NwcContainer, T> parse,
        CancellationToken cancellationToken)
    {
        var source = new BufferedInput(input, cancellationToken);
        if (source.Length < 16 || !source.Peek(0, SectionMarker.Length).StartsWith(SectionMarker))
        {
            throw new InvalidDataException("Not an LcUStream container: missing '#LcUStream' header.");
        }

        var sections = new List<LcUStreamHeader>();
        var streams = new List<LcRawStream>();
        var inflateBuffer = new byte[64 * 1024];
        long totalDecompressed = 0;
        long pos = 0;
        while (pos < source.Length)
        {
            long candidate = pos;
            while (candidate < source.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = source.Peek(candidate, SectionMarker.Length);
                if (bytes.StartsWith(SectionMarker))
                {
                    var header = ReadHeader(source, candidate);
                    sections.Add(header);
                    pos = candidate + header.HeaderLength;
                    break;
                }

                if (IsZlibSignature(bytes) &&
                    TryInflateAt(source, candidate, inflateBuffer, maxDecompressedStreamBytes, maxTotalDecompressedBytes, deferPayloads,
                        ref totalDecompressed, out var stream))
                {
                    streams.Add(stream with
                    {
                        Index = streams.Count,
                        GapBefore = candidate - pos,
                        SectionIndex = sections.Count - 1,
                    });
                    pos = candidate + stream.CompressedLength;
                    break;
                }

                // Only these two bytes can start a header or a compressed stream.
                bytes = source.Peek(candidate + 1);
                int next = bytes.IndexOfAny((byte)'#', (byte)0x78);
                candidate += 1 + (next < 0 ? bytes.Length : next);
            }

            if (candidate >= source.Length)
            {
                break;
            }
        }

        input.Position = source.EndPosition;
        var result = parse(new NwcContainer
        {
            Sections = sections,
            Streams = streams,
            FileLength = source.Length,
            TrailingGap = source.Length - pos,
        });
        if (deferPayloads)
        {
            input.Position = source.EndPosition;
        }

        return result;
    }

    private static bool IsZlibSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2 && bytes[0] == 0x78 && bytes[1] is 0x01 or 0x5E or 0x9C or 0xDA;

    private static LcUStreamHeader ReadHeader(BufferedInput source, long offset)
    {
        var bytes = source.Peek(offset);
        if (bytes.Contains((byte)'\n'))
        {
            return LcUStreamHeader.Parse(bytes, offset);
        }

        using var line = new MemoryStream();
        long pos = offset;
        while (!bytes.IsEmpty)
        {
            int end = bytes.IndexOf((byte)'\n');
            if (end >= 0)
            {
                line.Write(bytes[..(end + 1)]);
                break;
            }

            line.Write(bytes);
            pos += bytes.Length;
            bytes = source.Peek(pos);
        }

        return LcUStreamHeader.Parse(line.GetBuffer().AsSpan(0, (int)line.Length), offset);
    }

    private static bool TryInflateAt(
        BufferedInput source,
        long offset,
        byte[] buffer,
        long maxDecompressedStreamBytes,
        long maxTotalDecompressedBytes,
        bool deferPayload,
        ref long totalDecompressed,
        out LcRawStream stream)
    {
        try
        {
            var feed = new InflateFeed(source, offset);
            using var zlib = new ZLibStream(feed, CompressionMode.Decompress);
            int length = 0;
            int count;
            while ((count = zlib.Read(buffer, 0, buffer.Length)) != 0)
            {
                source.CheckCancellation();
                long streamLimit = Math.Min(maxDecompressedStreamBytes, Array.MaxLength);
                if (count > streamLimit - length)
                {
                    throw new InflateLimitException(
                        $"Decompressed stream at offset {offset} exceeds the per-stream limit of {streamLimit} bytes.");
                }

                if (count > maxTotalDecompressedBytes - totalDecompressed)
                {
                    throw new InflateLimitException(
                        $"Decompressed streams exceed the total limit of {maxTotalDecompressedBytes} bytes at stream offset {offset}.");
                }

                length += count;
                totalDecompressed += count; // Charge rejected streams too.
            }

            // Re-inflate with single-byte reads from the last block to find the exact end.
            var exactFeed = new InflateFeed(source, offset, feed.LastStart);
            byte[]? payload = deferPayload ? null : GC.AllocateUninitializedArray<byte>(length);
            using (var exact = new ZLibStream(exactFeed, CompressionMode.Decompress))
            {
                if (payload is null)
                {
                    while (exact.Read(buffer, 0, buffer.Length) != 0)
                    {
                        source.CheckCancellation();
                    }
                }
                else
                {
                    ReadPayload(exact, payload, source, offset);
                }
            }

            long compressedLength = exactFeed.BytesServed;
            stream = new LcRawStream
            {
                Index = -1,
                FileOffset = offset,
                CompressedLength = compressedLength,
                GapBefore = 0,
                SectionIndex = -1,
            };
            stream = payload is null
                ? stream with
                {
                    DeferredLength = length,
                    ReadPayload = () => ReadPayload(source, offset, compressedLength, length),
                }
                : stream with { Data = payload };
            return true;
        }
        catch (InflateLimitException ex)
        {
            throw new InvalidDataException(ex.Message);
        }
        catch (InvalidDataException)
        {
            stream = null!;
            return false;
        }
    }

    private static byte[] ReadPayload(BufferedInput source, long offset, long compressedLength, int length)
    {
        var feed = new InflateFeed(source, offset, end: offset + compressedLength);
        using var zlib = new ZLibStream(feed, CompressionMode.Decompress);
        var payload = GC.AllocateUninitializedArray<byte>(length);
        ReadPayload(zlib, payload, source, offset);
        return payload;
    }

    private static void ReadPayload(ZLibStream zlib, byte[] payload, BufferedInput source, long offset)
    {
        for (int pos = 0; pos < payload.Length;)
        {
            source.CheckCancellation();
            int count = Math.Min(64 * 1024, payload.Length - pos);
            if (zlib.ReadAtLeast(payload.AsSpan(pos, count), count, throwOnEndOfStream: false) != count)
            {
                throw new InvalidDataException($"Zlib stream at offset {offset} changed after scanning.");
            }

            pos += count;
        }

        if (zlib.ReadByte() != -1)
        {
            throw new InvalidDataException($"Zlib stream at offset {offset} changed after scanning.");
        }
    }

    private sealed class InflateLimitException(string message) : Exception(message);

    private sealed class BufferedInput(Stream input, CancellationToken cancellationToken)
    {
        private readonly long _origin = input.Position;
        private readonly byte[] _buffer = new byte[64 * 1024];
        private long _start;
        private int _count;

        public long EndPosition { get; } = input.Length;
        public long Length => Math.Max(0, EndPosition - _origin);

        public void CheckCancellation() => cancellationToken.ThrowIfCancellationRequested();

        public ReadOnlySpan<byte> Peek(long pos, int minimum = 1)
        {
            if (pos >= Length)
            {
                return [];
            }

            int required = (int)Math.Min(minimum, Length - pos);
            long index = pos - _start;
            if (index < 0 || index > _count - required)
            {
                CheckCancellation();
                _count = (int)Math.Min(_buffer.Length, Length - pos);
                input.Position = _origin + pos;
                input.ReadExactly(_buffer.AsSpan(0, _count));
                _start = pos;
                index = 0;
            }

            return _buffer.AsSpan((int)index, _count - (int)index);
        }
    }

    private sealed class InflateFeed : Stream
    {
        private const int BlockSize = 8 * 1024;
        private readonly BufferedInput _source;
        private readonly long _start;
        private readonly long _tailStart;
        private readonly long _end;
        private long _position;

        public InflateFeed(BufferedInput source, long start, long tailStart = long.MaxValue, long end = long.MaxValue)
        {
            _source = source;
            _start = start;
            _tailStart = tailStart;
            _end = Math.Min(end, source.Length);
            _position = start;
        }

        public long LastStart { get; private set; }
        public long BytesServed => _position - _start;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
            {
                return 0;
            }

            _source.CheckCancellation();
            int limit = _position < _tailStart ? (int)Math.Min(BlockSize, _tailStart - _position) : 1;
            int length = (int)Math.Min(Math.Min(count, limit), _end - _position);
            if (length == 0)
            {
                // ZLibStream can treat EOF as success before validating the trailer.
                throw new InvalidDataException($"Truncated zlib stream at offset {_start}.");
            }

            var bytes = _source.Peek(_position, length);
            bytes[..length].CopyTo(buffer.AsSpan(offset, length));
            LastStart = _position;
            _position += length;
            return length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _end - _start;
        public override long Position { get => BytesServed; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
