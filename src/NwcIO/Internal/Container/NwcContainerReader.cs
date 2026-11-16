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

    public static NwcContainer Read(byte[] data)
    {
        if (data.Length < 16 || !StartsWithAscii(data, 0, SectionMarker))
        {
            throw new InvalidDataException("Not an LcUStream container: missing '#LcUStream' header.");
        }

        var sections = new List<LcUStreamHeader>();
        var streams = new List<LcRawStream>();

        int sectionIndex = -1;
        int pos = 0;
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

            if (IsZlibSignature(data, pos))
            {
                if (TryInflateAt(data, pos, out var stream))
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

            if (IsZlibSignature(data, gapEnd) && TryInflateAt(data, gapEnd, out var gapStream))
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

    public static NwcContainer Read(Stream input)
    {
        // Avoid copying a caller's own array-backed MemoryStream.
        if (input is MemoryStream memory &&
            memory.TryGetBuffer(out ArraySegment<byte> segment) &&
            segment.Offset == 0 &&
            segment.Count == segment.Array!.Length)
        {
            return Read(segment.Array);
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
            return Read(data);
        }

        using var copy = new MemoryStream();
        input.CopyTo(copy);
        return Read(copy.ToArray());
    }

    public static NwcContainer ReadFile(string path) => Read(File.ReadAllBytes(path));

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

    private static bool TryInflateAt(byte[] data, int offset, out LcRawStream stream)
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

                output.Write(buffer, 0, n);
            }

            var payload = output.ToArray();
            uint adler = ComputeAdler32(payload);

            // The feed may have read a block past the trailer into the next stream. The last served
            // range still contains the big-endian Adler-32 of the payload; that occurrence marks the
            // exact end of this zlib stream.
            if (!feed.TryFindTrailer(adler, out long compressedLength))
            {
                throw new InvalidDataException($"Invalid or truncated zlib stream at offset {offset}.");
            }

            // EOF can end inflation without ZLibStream validating a missing trailer.
            if (compressedLength <= 4 ||
                BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + (int)compressedLength - 4, 4)) != adler)
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
        catch (InvalidDataException)
        {
            stream = null!;
            return false;
        }
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
    /// last block it handed out. The exact compressed length is then found from the Adler-32 trailer inside
    /// that block instead of limiting every read to one byte.
    /// </summary>
    private sealed class BlockFeed : Stream
    {
        private const int BlockSize = 64 * 1024;

        private readonly byte[] _data;
        private readonly int _start;
        private int _position;
        private int _lastStart;
        private int _lastLength;

        public BlockFeed(byte[] data, int start)
        {
            _data = data;
            _start = start;
            _position = start;
        }

        public long BytesServed => _position - _start;

        /// <summary>
        /// Finds the last (boundary-adjacent where possible) occurrence of the big-endian trailer in the
        /// final served block and returns the compressed length that ends right after it.
        /// </summary>
        public bool TryFindTrailer(uint adler, out long compressedLength)
        {
            compressedLength = 0;
            if (_lastLength < 4)
            {
                return false;
            }

            byte b0 = (byte)(adler >> 24);
            byte b1 = (byte)(adler >> 16);
            byte b2 = (byte)(adler >> 8);
            byte b3 = (byte)adler;

            int lastMatch = -1;
            int boundaryMatch = -1;
            for (int i = 0; i <= _lastLength - 4; i++)
            {
                int at = _lastStart + i;
                if (_data[at] != b0 || _data[at + 1] != b1 || _data[at + 2] != b2 || _data[at + 3] != b3)
                {
                    continue;
                }

                lastMatch = i;
                int end = at + 4;
                if (end == _data.Length ||
                    (end + 1 < _data.Length && IsZlibAt(end)) ||
                    (end + SectionMarker.Length <= _data.Length && IsMarkerAt(end)))
                {
                    boundaryMatch = i;
                }
            }

            int match = boundaryMatch >= 0 ? boundaryMatch : lastMatch;
            if (match < 0)
            {
                return false;
            }

            compressedLength = _lastStart + match + 4L - _start;
            return true;
        }

        private bool IsZlibAt(int at) =>
            _data[at] == 0x78 && _data[at + 1] is 0x01 or 0x5E or 0x9C or 0xDA;

        private bool IsMarkerAt(int at)
        {
            for (int i = 0; i < SectionMarker.Length; i++)
            {
                if (_data[at + i] != (byte)SectionMarker[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length || count <= 0)
            {
                return 0;
            }

            int n = Math.Min(Math.Min(count, BlockSize), _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, n);
            _lastStart = _position;
            _lastLength = n;
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
