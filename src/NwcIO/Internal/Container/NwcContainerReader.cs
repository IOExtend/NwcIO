using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace NwcIO.Internal.Container;

/// <summary>
/// Reads the LcUStream container of NWC/NWD files:
/// ASCII section headers interleaved with back-to-back zlib streams
/// (with occasional non-zlib framing bytes between streams).
/// </summary>
internal static class NwcContainerReader
{
    private static ReadOnlySpan<byte> SectionMarker => "#LcUStream"u8;
    private const int InflateBufferSize = 64 * 1024;
    private const int MaxDeflateExpansion = 258 * 8; // At least one bit per length code and at most 258 bytes per match.

    /// <summary>Default cap on the decompressed size of one stream: 512 MiB.</summary>
    public const long DefaultMaxDecompressedStreamBytes = 512L * 1024 * 1024;

    /// <summary>Default cap on the decompressed size of all streams of one container: 2 GiB.</summary>
    public const long DefaultMaxTotalDecompressedBytes = 2L * 1024 * 1024 * 1024;

    public static NwcContainer Read(byte[] data) =>
        Read(data, DefaultMaxDecompressedStreamBytes, DefaultMaxTotalDecompressedBytes);

    public static NwcContainer Read(byte[] data, long maxDecompressedStreamBytes, long maxTotalDecompressedBytes)
    {
        using var input = new MemoryStream(data, 0, data.Length, writable: false, publiclyVisible: true);
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
        var inflateBuffer = new byte[InflateBufferSize];
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
            long streamLimit = Math.Min(maxDecompressedStreamBytes, Array.MaxLength);
            long remaining = Math.Min(streamLimit, maxTotalDecompressedBytes - totalDecompressed);
            int storedLength = 0;
            long storedEnd = source.Length;
            bool stored = source.IsMemoryBacked && TryGetStoredStreamEnd(source, offset, remaining, out storedLength, out storedEnd);
            // Replay avoids staging large stored prefixes when even the worst-case decoded tail fits within the prefix size.
            bool replay = !stored && !deferPayload && storedLength >= InflateBufferSize
                && source.Length - storedEnd <= storedLength / MaxDeflateExpansion;
            var feed = new InflateFeed(source, offset, end: stored ? storedEnd : source.Length);
            using var zlib = new ZLibStream(feed, CompressionMode.Decompress);
            byte[]? payload = stored && !deferPayload ? GC.AllocateUninitializedArray<byte>(storedLength) : null;
            using var output = !stored && !replay && !deferPayload ? new InflateOutput() : null;
            uint adlerA = 1, adlerB = 0;
            int length = 0;
            while (true)
            {
                source.CheckCancellation();
                var target = payload is not null && length < payload.Length
                    ? payload.AsSpan(length, Math.Min(buffer.Length, payload.Length - length))
                    : output is not null ? output.GetSpan() : buffer.AsSpan();
                int count = zlib.Read(target);
                if (count == 0)
                {
                    break;
                }

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
                if (!stored && !replay)
                {
                    var bytes = target[..count];
                    UpdateAdler32(bytes, ref adlerA, ref adlerB);
                    output?.Advance(count);
                }
            }

            long compressedLength;
            if (stored)
            {
                compressedLength = storedEnd - offset;
                if (length != storedLength || feed.BytesServed != compressedLength)
                {
                    throw new InvalidDataException($"Zlib stream at offset {offset} changed after scanning.");
                }
            }
            else if (replay)
            {
                payload = GC.AllocateUninitializedArray<byte>(length);
                compressedLength = ReplayToBoundary(source, offset, feed.LastStart, buffer, payload);
            }
            else
            {
                compressedLength = FindCompressedLength(source, offset, feed, (adlerB << 16) | adlerA, buffer);
                payload = output?.ToArray();
            }

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

    private static bool TryGetStoredStreamEnd(BufferedInput source, long offset, long limit, out int length, out long end)
    {
        length = 0;
        long block = offset + 2; // The container's zlib signatures have no preset dictionary.
        while (source.Length - block >= 9) // Stored block header and zlib trailer.
        {
            source.CheckCancellation();
            var header = source.Peek(block, 5);
            if ((header[0] & 6) != 0)
            {
                break;
            }

            int size = BinaryPrimitives.ReadUInt16LittleEndian(header[1..]);
            int complement = BinaryPrimitives.ReadUInt16LittleEndian(header[3..]);
            if ((size ^ complement) != ushort.MaxValue || size > limit - length || size > source.Length - block - 9)
            {
                break;
            }

            length += size;
            block += 5 + size;
            if ((header[0] & 1) != 0)
            {
                end = block + 4;
                return true;
            }
        }

        end = block; // The first block after the stored prefix.
        return false;
    }

    private static long FindCompressedLength(BufferedInput source, long offset, InflateFeed feed, uint adler, byte[] buffer)
    {
        // A successful inflate validated the trailer. Its last byte arrived in the last read,
        // so only this span can contain it, including up to three bytes from the preceding read.
        long start = Math.Max(offset, feed.LastStart - 3);
        int length = (int)(offset + feed.BytesServed - start);
        var tail = source.Peek(start, length)[..length];
        Span<byte> trailer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(trailer, adler);
        int match = tail.IndexOf(trailer);
        if (match >= 0 && tail[(match + 1)..].IndexOf(trailer) < 0)
        {
            return start + match + trailer.Length - offset;
        }

        // Repeated checksums can occur in compressed data or framing. Measure the boundary byte by byte in that case.
        return ReplayToBoundary(source, offset, feed.LastStart, buffer);
    }

    private static long ReplayToBoundary(BufferedInput source, long offset, long lastBlockStart, byte[] buffer, byte[]? payload = null)
    {
        var exactFeed = new InflateFeed(source, offset, lastBlockStart);
        using var exact = new ZLibStream(exactFeed, CompressionMode.Decompress);
        if (payload is not null)
        {
            ReadPayload(exact, payload, source, offset);
        }
        else
        {
            while (exact.Read(buffer, 0, buffer.Length) != 0)
            {
                source.CheckCancellation();
            }
        }

        return exactFeed.BytesServed;
    }

    internal static void UpdateAdler32(ReadOnlySpan<byte> data, ref uint a, ref uint b)
    {
        const uint modulus = 65521;
        if (Vector128.IsHardwareAccelerated)
        {
            const int batchSize = 256;
            var lowWeights = Vector128.Create((ushort)16, 15, 14, 13, 12, 11, 10, 9);
            var highWeights = Vector128.Create((ushort)8, 7, 6, 5, 4, 3, 2, 1);
            while (data.Length >= batchSize)
            {
                var low = Vector128<ushort>.Zero;
                var high = Vector128<ushort>.Zero;
                var lowPrefix = Vector128<ushort>.Zero;
                var highPrefix = Vector128<ushort>.Zero;
                ref byte bytes = ref MemoryMarshal.GetReference(data);
                for (int offset = 0; offset < batchSize; offset += 16)
                {
                    lowPrefix += low;
                    highPrefix += high;
                    var (left, right) = Vector128.Widen(Vector128.LoadUnsafe(ref bytes, (nuint)offset));
                    low += left;
                    high += right;
                }

                // Each vector's bytes add their weights within that vector, plus 16 for each vector after it.
                b += (batchSize * a) + (16 * (SumWidened(lowPrefix) + SumWidened(highPrefix)))
                    + SumWidened(low * lowWeights) + SumWidened(high * highWeights);
                a += Vector128.Sum(low + high);
                a %= modulus;
                b %= modulus;
                data = data[batchSize..];
            }
        }

        while (!data.IsEmpty)
        {
            int count = Math.Min(data.Length, 5552); // B stays within uint even for all-255 input.
            foreach (byte value in data[..count])
            {
                a += value;
                b += a;
            }

            a %= modulus;
            b %= modulus;
            data = data[count..];
        }
    }

    private static uint SumWidened(Vector128<ushort> values)
    {
        var (low, high) = Vector128.Widen(values);
        return Vector128.Sum(low + high);
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

    private sealed class InflateOutput : IDisposable
    {
        private readonly List<byte[]> _blocks = [];
        private int _length;

        public Span<byte> GetSpan()
        {
            int block = _length / InflateBufferSize;
            if (block == _blocks.Count)
            {
                _blocks.Add(ArrayPool<byte>.Shared.Rent(InflateBufferSize));
            }

            int used = _length % InflateBufferSize;
            return _blocks[block].AsSpan(used, InflateBufferSize - used);
        }

        public void Advance(int count) => _length += count;

        public byte[] ToArray()
        {
            var payload = GC.AllocateUninitializedArray<byte>(_length);
            int offset = 0;
            foreach (byte[] block in _blocks)
            {
                int count = Math.Min(InflateBufferSize, payload.Length - offset);
                block.AsSpan(0, count).CopyTo(payload.AsSpan(offset));
                offset += count;
            }

            return payload;
        }

        public void Dispose()
        {
            foreach (byte[] block in _blocks)
            {
                ArrayPool<byte>.Shared.Return(block);
            }
        }
    }

    private sealed class BufferedInput(Stream input, CancellationToken cancellationToken)
    {
        private readonly long _origin = input.Position;
        private readonly byte[] _buffer = new byte[64 * 1024];
        private readonly ArraySegment<byte> _memory =
            input.GetType() == typeof(MemoryStream) && ((MemoryStream)input).TryGetBuffer(out var segment) ? segment : default;
        private long _start;
        private int _count;

        public long EndPosition { get; } = input.Length;
        public long Length => Math.Max(0, EndPosition - _origin);
        public bool IsMemoryBacked => _memory.Array is not null;

        public void CheckCancellation() => cancellationToken.ThrowIfCancellationRequested();

        public ReadOnlySpan<byte> Peek(long pos, int minimum = 1)
        {
            if (pos >= Length)
            {
                return [];
            }

            if (_memory.Array is not null)
            {
                return _memory.AsSpan((int)(_origin + pos));
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
