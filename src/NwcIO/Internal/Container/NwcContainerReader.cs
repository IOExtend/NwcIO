using System.Buffers.Binary;
using System.IO.Compression;

namespace NwcIO.Internal.Container;

/// <summary>
/// Reads the LcUStream container of NWC/NWD files:
/// ASCII section headers interleaved with back-to-back zlib streams
/// (with occasional non-zlib framing bytes between streams).
/// </summary>
internal static class NwcContainerReader
{
    private const string SectionMarker = "#LcUStream";

    /// <summary>Default cap on the decompressed size of one stream: 512 MiB.</summary>
    public const long DefaultMaxDecompressedStreamBytes = 512L * 1024 * 1024;

    /// <summary>Default cap on the decompressed size of all streams of one container: 2 GiB.</summary>
    public const long DefaultMaxTotalDecompressedBytes = 2L * 1024 * 1024 * 1024;

    public static NwcContainer Read(byte[] data) =>
        Read(data, DefaultMaxDecompressedStreamBytes, DefaultMaxTotalDecompressedBytes);

    /// <param name="maxDecompressedStreamBytes">Cap on one inflated stream; exceeding it throws <see cref="InvalidDataException"/>.</param>
    /// <param name="maxTotalDecompressedBytes">Cap on all inflated streams together; exceeding it throws <see cref="InvalidDataException"/>.</param>
    public static NwcContainer Read(byte[] data, long maxDecompressedStreamBytes, long maxTotalDecompressedBytes)
    {
        if (maxDecompressedStreamBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDecompressedStreamBytes), "The per-stream decompression limit must be positive.");
        }

        if (maxTotalDecompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTotalDecompressedBytes), "The total decompression limit must be positive.");
        }

        if (data.Length < 16 || !StartsWithAscii(data, 0, SectionMarker))
        {
            throw new InvalidDataException("Not an LcUStream container: missing '#LcUStream' header.");
        }

        var sections = new List<LcUStreamHeader>();
        var streams = new List<LcRawStream>();
        long totalDecompressed = 0;

        int sectionIndex = -1;
        int pos = 0;
        int rejectedOffset = -1; // zlib signature at this offset already failed to inflate
        while (pos < data.Length)
        {
            if (StartsWithAscii(data, pos, SectionMarker))
            {
                var header = LcUStreamHeader.Parse(data, pos);
                sections.Add(header);
                sectionIndex = sections.Count - 1;
                pos = (int)(header.FileOffset + header.HeaderLength);
                continue;
            }

            if (pos != rejectedOffset && IsZlibSignature(data, pos))
            {
                if (TryInflateAt(data, pos, maxDecompressedStreamBytes, maxTotalDecompressedBytes, ref totalDecompressed, out var stream))
                {
                    streams.Add(stream with
                    {
                        Index = streams.Count,
                        GapBefore = 0,
                        SectionIndex = sectionIndex,
                    });
                    pos = (int)(stream.FileOffset + stream.CompressedLength);
                    continue;
                }

                // False zlib signature: fall through to gap handling.
            }

            // Non-zlib bytes: framing blocks (hash + size words), section padding, tail.
            // Scan forward to the next zlib signature or section header.
            int scan = pos + 1;
            int gapEnd = -1;
            while (scan < data.Length)
            {
                if (IsZlibSignature(data, scan) || StartsWithAscii(data, scan, SectionMarker))
                {
                    gapEnd = scan;
                    break;
                }

                scan++;
            }

            if (gapEnd < 0)
            {
                break; // unrecognized tail
            }

            if (IsZlibSignature(data, gapEnd) &&
                TryInflateAt(data, gapEnd, maxDecompressedStreamBytes, maxTotalDecompressedBytes, ref totalDecompressed, out var gapStream))
            {
                streams.Add(gapStream with
                {
                    Index = streams.Count,
                    GapBefore = gapEnd - pos,
                    SectionIndex = sectionIndex,
                });
                pos = (int)(gapEnd + gapStream.CompressedLength);
            }
            else
            {
                rejectedOffset = IsZlibSignature(data, gapEnd) ? gapEnd : -1;
                pos = gapEnd;
            }
        }

        return new NwcContainer
        {
            Sections = sections,
            Streams = streams,
            FileLength = data.Length,
            TrailingGap = data.Length - pos,
        };
    }

    public static NwcContainer Read(Stream input) =>
        Read(input, DefaultMaxDecompressedStreamBytes, DefaultMaxTotalDecompressedBytes);

    public static NwcContainer Read(Stream input, long maxDecompressedStreamBytes, long maxTotalDecompressedBytes)
    {
        // Avoid copying a caller's own array-backed MemoryStream, but only from its current Position.
        if (input is MemoryStream memory &&
            memory.Position == 0 &&
            memory.TryGetBuffer(out ArraySegment<byte> segment) &&
            segment.Offset == 0 &&
            segment.Count == segment.Array!.Length)
        {
            return Read(segment.Array, maxDecompressedStreamBytes, maxTotalDecompressedBytes);
        }

        if (input.CanSeek)
        {
            long remaining = input.Length - input.Position;
            if (remaining > Array.MaxLength)
            {
                throw new IOException($"The container is {remaining} bytes; the in-memory reader supports at most {Array.MaxLength} bytes.");
            }

            var data = new byte[(int)remaining];
            input.ReadExactly(data);
            return Read(data, maxDecompressedStreamBytes, maxTotalDecompressedBytes);
        }

        using var copy = new MemoryStream();
        input.CopyTo(copy);
        return Read(copy.ToArray(), maxDecompressedStreamBytes, maxTotalDecompressedBytes);
    }

    public static NwcContainer ReadFile(string path) =>
        ReadFile(path, DefaultMaxDecompressedStreamBytes, DefaultMaxTotalDecompressedBytes);

    public static NwcContainer ReadFile(string path, long maxDecompressedStreamBytes, long maxTotalDecompressedBytes) =>
        Read(File.ReadAllBytes(path), maxDecompressedStreamBytes, maxTotalDecompressedBytes);

    private static bool IsZlibSignature(byte[] data, int pos) =>
        pos + 1 < data.Length && data[pos] == 0x78 && data[pos + 1] is 0x01 or 0x5E or 0x9C or 0xDA;

    private static bool StartsWithAscii(byte[] data, int pos, string prefix)
    {
        if (pos + prefix.Length > data.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (data[pos + i] != (byte)prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryInflateAt(
        byte[] data,
        int offset,
        long maxDecompressedStreamBytes,
        long maxTotalDecompressedBytes,
        ref long totalDecompressed,
        out LcRawStream stream)
    {
        try
        {
            var feed = new BlockFeed(data, offset);
            using var zlib = new ZLibStream(feed, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                int n = zlib.Read(buffer, 0, buffer.Length);
                if (n == 0)
                {
                    break;
                }

                if (output.Length + n > maxDecompressedStreamBytes)
                {
                    throw new InflateLimitException(
                        $"Decompressed stream at offset {offset} exceeds the per-stream limit of {maxDecompressedStreamBytes} bytes.");
                }

                if (totalDecompressed + n > maxTotalDecompressedBytes)
                {
                    throw new InflateLimitException(
                        $"Decompressed streams exceed the total limit of {maxTotalDecompressedBytes} bytes at stream offset {offset}.");
                }

                output.Write(buffer, 0, n);
                totalDecompressed += n; // charged even if the stream is later rejected (bad trailer), so crafted garbage cannot dodge the total cap
            }

            var payload = output.ToArray();
            uint adler = ComputeAdler32(payload);

            // The block feed may have read past the trailer. Re-inflate once more, serving blocks up to
            // the start of the final block and single bytes from there, so BytesServed is the exact
            // compressed length. This is deterministic and does not rely on finding Adler-32 in data.
            var exactFeed = new ExactTailFeed(data, offset, feed.LastStart);
            long exactLength = 0;
            using (var exact = new ZLibStream(exactFeed, CompressionMode.Decompress))
            {
                var sink = new byte[64 * 1024];
                while (exact.Read(sink, 0, sink.Length) > 0)
                {
                }

                exactLength = exactFeed.BytesServed;
            }

            // EOF can end inflation without ZLibStream validating a missing trailer.
            if (exactLength <= 4 ||
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + (int)exactLength - 4, 4)) != adler)
            {
                throw new InvalidDataException($"Invalid or truncated zlib stream at offset {offset}.");
            }

            long compressedLength = exactLength;

            stream = new LcRawStream
            {
                Index = -1,
                FileOffset = offset,
                CompressedLength = compressedLength,
                GapBefore = 0,
                Data = payload,
                SectionIndex = -1,
            };
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

    /// <summary>Signals that inflation crossed a configured resource limit; never treated as a false zlib signature.</summary>
    private sealed class InflateLimitException : Exception
    {
        public InflateLimitException(string message) : base(message) { }
    }

    private static uint ComputeAdler32(ReadOnlySpan<byte> data)
    {
        const uint Mod = 65521;
        const int Block = 5552; // largest n with 255n(n+1)/2 + (n+1)(Mod-1) inside uint32
        uint a = 1;
        uint b = 0;
        int i = 0;
        while (i < data.Length)
        {
            int count = Math.Min(Block, data.Length - i);
            for (int end = i + count; i < end; i++)
            {
                a += data[i];
                b += a;
            }

            a %= Mod;
            b %= Mod;
        }

        return (b << 16) | a;
    }

    /// <summary>
    /// Serves the underlying buffer in blocks so the inflater runs at native speed, while recording the
    /// last block it handed out. <see cref="ExactTailFeed"/> uses that block start to measure the exact
    /// compressed length without limiting every read to one byte.
    /// </summary>
    private sealed class BlockFeed : Stream
    {
        private const int BlockSize = 1024;

        private readonly byte[] _data;
        private readonly int _start;
        private int _position;

        public BlockFeed(byte[] data, int start)
        {
            _data = data;
            _start = start;
            _position = start;
        }

        public long BytesServed => _position - _start;

        /// <summary>Absolute index of the first byte of the last block served.</summary>
        public int LastStart { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length || count <= 0)
            {
                return 0;
            }

            int n = Math.Min(Math.Min(count, BlockSize), _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, n);
            LastStart = _position;
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length - _start;
        public override long Position { get => BytesServed; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Serves the stream in blocks up to <c>switchAt</c> and one byte at a time from there, so that the
    /// inflater cannot read past the exact end of the zlib stream and <see cref="BytesServed"/> is precise.
    /// </summary>
    private sealed class ExactTailFeed : Stream
    {
        private const int BlockSize = 1024;

        private readonly byte[] _data;
        private readonly int _start;
        private readonly int _switchAt;
        private int _position;

        public ExactTailFeed(byte[] data, int start, int switchAt)
        {
            _data = data;
            _start = start;
            _position = start;
            _switchAt = Math.Clamp(switchAt, start, data.Length);
        }

        public long BytesServed => _position - _start;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length || count <= 0)
            {
                return 0;
            }

            int n = _position < _switchAt
                ? Math.Min(Math.Min(count, BlockSize), _switchAt - _position)
                : 1;
            n = Math.Min(n, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length - _start;
        public override long Position { get => BytesServed; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
