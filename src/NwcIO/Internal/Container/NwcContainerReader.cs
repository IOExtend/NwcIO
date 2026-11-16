using System.Buffers.Binary;
using System.IO.Compression;

namespace NwcIO.Internal.Container;

/// <summary>
/// Reads the LcUStream container of NWC/NWD files:
/// ASCII section headers interleaved with back-to-back zlib streams
/// (with occasional non-zlib framing bytes between streams).
/// </summary>
public static class NwcContainerReader
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
                pos = header.FileOffset + header.HeaderLength;
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
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        return Read(ms.ToArray(), maxDecompressedStreamBytes, maxTotalDecompressedBytes);
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
            var feed = new ExactLengthFeed(data, offset);
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

            var compressedLength = (int)feed.BytesServed;
            var payload = output.ToArray();
            var a = 1u;
            var b = 0u;
            foreach (var value in payload)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }

            // EOF can end inflation without ZLibStream validating a missing trailer.
            if (compressedLength <= 4 ||
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + compressedLength - 4, 4)) != ((b << 16) | a))
            {
                throw new InvalidDataException($"Invalid or truncated zlib stream at offset {offset}.");
            }

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

    /// <summary>
    /// Serves the underlying buffer one byte at a time so that <see cref="ZLibStream"/>
    /// never reads past the end of the zlib stream; <see cref="BytesServed"/> then equals
    /// the exact compressed length. (Byte-wise serving is fast enough for NWC-sized files;
    /// TOC-driven exact sizes can replace this later.)
    /// </summary>
    private sealed class ExactLengthFeed : Stream
    {
        private readonly byte[] _data;
        private readonly int _start;
        private int _position;

        public ExactLengthFeed(byte[] data, int start)
        {
            _data = data;
            _start = start;
            _position = start;
        }

        public long BytesServed => _position - _start;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length)
            {
                return 0;
            }

            buffer[offset] = _data[_position++];
            return 1;
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
