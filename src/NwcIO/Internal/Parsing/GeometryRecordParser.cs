using System.Buffers.Binary;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>A decoded geometry record: triangles (class 94) or lines (class 95).</summary>
internal sealed class GeometryRecord
{
    public required bool IsLines { get; init; }

    /// <summary>Render vertices: render vertex r sits at position VMap[r].</summary>
    public required Vector3d[] Vertices { get; init; }

    /// <summary>Triangles over <see cref="Vertices"/>, counter-clockwise; empty for lines.</summary>
    public required int[] Indices { get; init; }

    /// <summary>Segment pairs over <see cref="Vertices"/>; empty for triangles.</summary>
    public required int[] LineIndices { get; init; }

    /// <summary>One unit normal per render vertex, <see cref="Vector3d.Zero"/> for none; null for lines.</summary>
    public required Vector3d[]? Normals { get; init; }

    /// <summary>Axis-aligned bounds of the stored positions.</summary>
    public required BoundingBox3d Bounds { get; init; }

    public int PrimitiveCount => IsLines ? LineIndices.Length / 2 : Indices.Length / 3;
}

/// <summary>
/// Strict decoder of one <c>LcOpNwdGeometry</c> record. Every rule violation throws
/// <see cref="InvalidDataException"/> with the message "&lt;rule&gt; at +&lt;offset&gt;" (offset from the record start).
/// </summary>
internal static class GeometryRecordParser
{
    // Axis codes -1..-6 (leads D0..D5): +X, -X, +Y, -Y, +Z, -Z.
    private static readonly Vector3d[] Axes =
        [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    /// <param name="record">The record bytes, exactly its TOC length.</param>
    /// <param name="normalBits">Palette value width B: 16 in <c>.nwc</c>, 8 in <c>.nwd</c>.</param>
    public static GeometryRecord Parse(ReadOnlySpan<byte> record, int normalBits)
    {
        if (normalBits is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(normalBits), normalBits, "B is 16 or 8.");
        }

        int pos = 0;
        Expect(record, ref pos, 100, "record id is not 100");
        int at = pos;
        int cls = ReadInt32(record, ref pos);
        if (cls is not (94 or 95))
        {
            throw Fail("class is not 94 or 95", at);
        }

        bool lines = cls == 95;
        Expect(record, ref pos, 101, "position object id is not 101");
        Expect(record, ref pos, 60, "position class is not 60");
        at = pos;
        int p1 = ReadInt32(record, ref pos);
        if (p1 <= 0)
        {
            // Negative p1 means quantised positions, never seen.
            throw Fail("render vertex count is not positive", at);
        }

        CheckDensity(p1, record, "render vertex count is above the record length", at);
        int p2 = ReadInt32(record, ref pos);
        if (p2 <= 0 || p2 % 3 != 0)
        {
            throw Fail("position float count is not a positive multiple of 3", at + 4);
        }

        int npos = p2 / 3;
        if (p2 > (record.Length - pos) / 4)
        {
            throw Fail("positions pass the end", pos);
        }

        if (p1 < npos)
        {
            throw Fail("render vertex count is below the position count", at);
        }

        var positions = new Vector3d[npos];
        var bounds = BoundingBox3d.Empty;
        for (int i = 0; i < npos; i++)
        {
            var span = record[(pos + 12 * i)..];
            positions[i] = new Vector3d(
                BinaryPrimitives.ReadSingleLittleEndian(span),
                BinaryPrimitives.ReadSingleLittleEndian(span[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(span[8..]));
            bounds = bounds.Union(positions[i]);
        }

        pos += 4 * p2;
        var vertices = p1 == npos ? positions : Array.ConvertAll(Tokens(record, ref pos, p1, npos, false), i => positions[i]);

        int nextId = 102;
        at = pos;
        int id = ReadInt32(record, ref pos);
        bool hasUv = id != 0;
        if (hasUv)
        {
            ExpectId(id, ref nextId, at);
            Expect(record, ref pos, 61, "UV class is not 61");
            at = pos;
            int n = ReadInt32(record, ref pos);
            if (n == p1)
            {
                at = pos;
                int fc = ReadInt32(record, ref pos);
                if (fc <= 0 || fc % 2 != 0)
                {
                    throw Fail("UV float count is not positive and even", at);
                }

                Skip(record, ref pos, 4L * fc, "UV floats pass the end");
                if (fc / 2 != p1)
                {
                    Tokens(record, ref pos, p1, fc / 2, false);
                }
            }
            else if (n == -p1)
            {
                Skip(record, ref pos, 16, "UV range passes the end");
                at = pos;
                int w = ReadInt32(record, ref pos);
                int bu = w & 0xFF, bv = w >> 8;
                if (w >> 16 != 0 || bu == 0 || bv == 0)
                {
                    throw Fail("UV bit widths are not two non-zero bytes", at);
                }

                at = pos;
                int m = ReadInt32(record, ref pos);
                if (m <= 0 || m > p1)
                {
                    throw Fail("UV value count is not in 1..p1", at);
                }

                // UV values are not decoded: read for length only.
                SkipPacked(record, ref pos, m, bu + bv);
                if (m != p1)
                {
                    Tokens(record, ref pos, p1, m, false);
                }
            }
            else
            {
                throw Fail("UV count is neither p1 nor -p1", at);
            }
        }

        Vector3d[]? normals = null;
        at = pos;
        id = ReadInt32(record, ref pos);
        if (id != 0)
        {
            ExpectId(id, ref nextId, at);
            Expect(record, ref pos, 100, "normal class is not 100");
            Expect(record, ref pos, -p1, "normal count is not -p1");
            at = pos;
            int pal = ReadInt32(record, ref pos);
            if (pal < 0 || pal > int.MaxValue / 3)
            {
                throw Fail("normal palette size is out of range", at);
            }

            int[] raw = DecodePacked(record, ref pos, 3 * pal, normalBits);
            var palette = new Vector3d[pal];
            for (int k = 0; k < pal; k++)
            {
                palette[k] = DecodePaletteEntry(raw[3 * k], raw[(3 * k) + 1], raw[(3 * k) + 2], normalBits);
            }

            normals = pal == p1
                ? palette
                : Array.ConvertAll(Tokens(record, ref pos, p1, pal, true), k => k >= 0 ? palette[k] : Axes[-k - 1]);
        }

        at = pos;
        id = ReadInt32(record, ref pos);
        bool hasColour = id != 0;
        if (hasColour)
        {
            ExpectId(id, ref nextId, at);
            Expect(record, ref pos, 58, "colour class is not 58");
            Expect(record, ref pos, 5634, "colour flag is not 5634");
            Expect(record, ref pos, -p1, "colour count is not -p1");
            at = pos;
            int cpal = ReadInt32(record, ref pos);
            if (cpal <= 0)
            {
                throw Fail("colour palette size is not positive", at);
            }

            // Colour entries are 4 bytes whose channels are not decoded: read for length only.
            Skip(record, ref pos, 4L * cpal, "colour entries pass the end");
            if (cpal != p1)
            {
                Tokens(record, ref pos, p1, cpal, false);
            }
        }

        if (lines ? hasUv || normals is not null || !hasColour : normals is null || hasColour)
        {
            throw Fail("slot set does not match the class", pos);
        }

        at = pos;
        int count = ReadInt32(record, ref pos);
        if (count <= 0)
        {
            throw Fail("primitive count is not positive", at);
        }

        int bits = ReadInt32(record, ref pos);
        if (bits is < 1 or > 31)
        {
            throw Fail("primitive length width is not in 1..31", at + 4);
        }

        Expect(record, ref pos, count, "second primitive count differs");
        int lengthsAt = pos;
        int[] lengths = DecodePacked(record, ref pos, count, bits);
        long sum = 0;
        foreach (int length in lengths)
        {
            if (length < (lines ? 2 : 3))
            {
                throw Fail("primitive length is too short", lengthsAt);
            }

            sum += length;
        }

        int[] corners = [];
        if (!lines)
        {
            at = pos;
            int s = ReadInt32(record, ref pos);
            if (s < 0)
            {
                throw Fail("corner count is negative", at);
            }

            if (s > 0 && s <= p1)
            {
                throw Fail("corner count is not above p1", at);
            }

            int cornerCount = s == 0 ? p1 : s;
            if (3L * (cornerCount - (2L * count)) > Array.MaxLength)
            {
                throw Fail("triangle index count is above the array limit", at);
            }

            CheckDensity(cornerCount, record, "corner count is above the record length", at);
            corners = s == 0 ? Enumerable.Range(0, p1).ToArray() : Tokens(record, ref pos, s, p1, false);
            if (sum != corners.Length)
            {
                throw Fail("primitive lengths do not sum to the corner count", lengthsAt);
            }
        }
        else if (sum != p1)
        {
            throw Fail("primitive lengths do not sum to p1", lengthsAt);
        }
        else if (2L * (p1 - count) > Array.MaxLength)
        {
            throw Fail("line index count is above the array limit", at);
        }

        Pad(record, ref pos, 8);
        if (pos != record.Length)
        {
            throw Fail("record does not end at its TOC length", pos);
        }

        int[] indices = [], lineIndices = [];
        int start = 0;
        if (lines)
        {
            lineIndices = new int[2 * (p1 - count)];
            int k = 0;
            foreach (int length in lengths)
            {
                for (int r = start; r + 1 < start + length; r++)
                {
                    lineIndices[k++] = r;
                    lineIndices[k++] = r + 1;
                }

                start += length;
            }
        }
        else
        {
            indices = new int[3 * (corners.Length - (2 * count))];
            int k = 0;
            foreach (int length in lengths)
            {
                for (int i = 0; i + 2 < length; i++)
                {
                    // Odd strip triangles swap their first two corners so every triangle stays counter-clockwise.
                    int a = corners[start + i], b = corners[start + i + 1];
                    indices[k++] = i % 2 == 0 ? a : b;
                    indices[k++] = i % 2 == 0 ? b : a;
                    indices[k++] = corners[start + i + 2];
                }

                start += length;
            }
        }

        return new GeometryRecord
        {
            IsLines = lines,
            Vertices = vertices,
            Indices = indices,
            LineIndices = lineIndices,
            Normals = normals,
            Bounds = bounds,
        };
    }

    /// <summary>
    /// TOKENS(count, fresh, axis), without the pad4 that follows it in a record.
    /// Runs introduce 0..fresh-1 once each, in order; axis codes decode to -1..-6.
    /// </summary>
    internal static int[] DecodeTokens(ReadOnlySpan<byte> data, ref int pos, int count, int fresh, bool axisCodes)
    {
        // One lead byte yields at most 64 values.
        if (count < 0 || count > 64L * (data.Length - pos))
        {
            throw Fail("token count passes the end", pos);
        }

        // Grow with the decoded output, so a crafted count allocates nothing beyond what the bytes decode to.
        var values = new int[Math.Min(count, data.Length - pos)];
        int n = 0, next = 0;
        while (n < count)
        {
            int at = pos;
            int b = ReadByte(data, ref pos);
            int add = b <= 0x3F ? b + 1 : b is >= 0x80 and <= 0xBF ? b - 0x7F : 1;
            if (add > count - n)
            {
                throw Fail("token passes the count", at);
            }

            if (n + add > values.Length)
            {
                Array.Resize(ref values, (int)Math.Min(count, Math.Max(n + add, 2L * values.Length)));
            }

            if (b <= 0x3F)
            {
                for (int i = 0; i < add; i++)
                {
                    values[n++] = next++;
                }
            }
            else if (b <= 0x7F)
            {
                values[n] = values[n - Distance(b - 0x40, n, at)];
                n++;
            }
            else if (b <= 0xBF)
            {
                if (n == 0)
                {
                    throw Fail("repeat has no previous value", at);
                }

                values.AsSpan(n, add).Fill(values[n - 1]);
                n += add;
            }
            else if (axisCodes && b is >= 0xD0 and <= 0xD5)
            {
                values[n++] = 0xCF - b;
            }
            else if (b is >= 0xE0 and <= 0xEF)
            {
                int d = ((b & 0x0F) << 8) | ReadByte(data, ref pos);
                values[n] = values[n - Distance(d, n, at)];
                n++;
            }
            else if (b == 0xF0)
            {
                int literal = ReadByte(data, ref pos) << 8;
                literal |= ReadByte(data, ref pos);
                if (literal >= next)
                {
                    throw Fail("literal is not below the next fresh value", at);
                }

                values[n++] = literal;
            }
            else
            {
                throw Fail("lead byte is undefined", at);
            }
        }

        if (next != fresh)
        {
            throw Fail("tokens do not introduce exactly the fresh values", pos);
        }

        return values;
    }

    /// <summary>
    /// PACKED(count, width): stream bit j is bit 31 - j%32 of little-endian word j/32 (MSB first);
    /// whole words are consumed and their unused tail bits must be 0. <paramref name="width"/> is 1..31.
    /// </summary>
    internal static int[] DecodePacked(ReadOnlySpan<byte> data, ref int pos, int count, int width)
    {
        int start = pos;
        SkipPacked(data, ref pos, count, width);
        var values = new int[count];
        ulong mask = (1UL << width) - 1;
        for (int k = 0; k < count; k++)
        {
            long bit = (long)k * width;
            int word = start + (4 * (int)(bit >> 5));

            // A value of at most 31 bits spans at most two words: read them as one MSB-first 64-bit window.
            ulong window = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[word..]) << 32;
            if (word + 4 < pos)
            {
                window |= BinaryPrimitives.ReadUInt32LittleEndian(data[(word + 4)..]);
            }

            values[k] = (int)((window >> (64 - (int)(bit & 31) - width)) & mask);
        }

        return values;
    }

    /// <summary>Palette entry: three B-bit two's complement values, divided by 2^(B-1)-1, then normalised; all zero gives Zero.</summary>
    internal static Vector3d DecodePaletteEntry(int x, int y, int z, int bits)
    {
        int half = 1 << (bits - 1);
        double scale = half - 1;
        return new Vector3d(Signed(x) / scale, Signed(y) / scale, Signed(z) / scale).Normalize();

        int Signed(int v) => v >= half ? v - (2 * half) : v;
    }

    private static void SkipPacked(ReadOnlySpan<byte> data, ref int pos, long count, int width)
    {
        long bits = count * width;
        long words = (bits + 31) / 32;
        if (words > (data.Length - pos) / 4)
        {
            throw Fail("packed words pass the end", pos);
        }

        int last = pos + (4 * ((int)words - 1));
        int used = (int)(bits % 32);
        if (used != 0 && (BinaryPrimitives.ReadUInt32LittleEndian(data[last..]) & (uint.MaxValue >> used)) != 0)
        {
            throw Fail("unused packed bit is set", last);
        }

        pos += 4 * (int)words;
    }

    // shortcut: a record never yields more render vertices or corners than it has bytes (real records peak at 0.22 per byte); raise it if a real exporter writes denser records
    private static void CheckDensity(int count, ReadOnlySpan<byte> record, string rule, int at)
    {
        if (count > record.Length)
        {
            throw Fail(rule, at);
        }
    }

    private static int[] Tokens(ReadOnlySpan<byte> data, ref int pos, int count, int fresh, bool axisCodes)
    {
        int[] values = DecodeTokens(data, ref pos, count, fresh, axisCodes);
        Pad(data, ref pos, 4);
        return values;
    }

    private static int Distance(int d, int n, int at)
        => d >= 1 && d <= n ? d : throw Fail("back-reference distance is out of range", at);

    private static void ExpectId(int id, ref int nextId, int at)
    {
        // Slot objects take the next id in slot order; anything else (a back-reference included) never occurs.
        if (id != nextId++)
        {
            throw Fail("slot id is out of order", at);
        }
    }

    private static void Expect(ReadOnlySpan<byte> data, ref int pos, int expected, string rule)
    {
        int at = pos;
        if (ReadInt32(data, ref pos) != expected)
        {
            throw Fail(rule, at);
        }
    }

    private static void Pad(ReadOnlySpan<byte> data, ref int pos, int alignment)
    {
        while (pos % alignment != 0)
        {
            if (ReadByte(data, ref pos) != 0)
            {
                throw Fail("pad byte is not zero", pos - 1);
            }
        }
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int pos, long bytes, string rule)
    {
        if (bytes > data.Length - pos)
        {
            throw Fail(rule, pos);
        }

        pos += (int)bytes;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int pos)
    {
        if (data.Length - pos < 4)
        {
            throw Fail("unexpected end", pos);
        }

        int value = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
        pos += 4;
        return value;
    }

    private static int ReadByte(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos >= data.Length)
        {
            throw Fail("unexpected end", pos);
        }

        return data[pos++];
    }

    private static InvalidDataException Fail(string rule, int offset) => new($"{rule} at +{offset}");
}
