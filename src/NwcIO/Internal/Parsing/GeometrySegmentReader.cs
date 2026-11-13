using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using NwcIO.Internal.Container;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Strict reader of the <c>LcOpNwdGeometry</c> segment: checks the TOC against the inflated chunk streams,
/// cuts the records and decodes them under the one normal width B every record accepts (RS §1.5, §8.2).
/// </summary>
internal static class GeometrySegmentReader
{
    /// <param name="chunks">The segment's inflated chunk streams, in file order.</param>
    /// <param name="toc">The inflated TOC stream: [N][ch][ch x size][N x len], zero padded to a multiple of 8.</param>
    /// <returns>The records in TOC order.</returns>
    public static GeometryRecord[] Read(IReadOnlyList<LcRawStream> chunks, LcRawStream toc)
    {
        var records = Cut(chunks, ReadToc(chunks, toc.Data));

        // 16 wins when both widths parse: a file without palettes, or a palette valid under both (RS §8.2).
        if (TryParseAll(records, 16, out var parsed, out int wideAt, out var wideError)
            || TryParseAll(records, 8, out parsed, out int narrowAt, out var narrowError))
        {
            return parsed;
        }

        var (index, inner) = narrowAt > wideAt ? (narrowAt, narrowError) : (wideAt, wideError);
        throw new InvalidDataException($"LcOpNwdGeometry record {index}: {inner.Message}", inner);
    }

    private static int[] ReadToc(IReadOnlyList<LcRawStream> chunks, byte[] toc)
    {
        if (toc.Length < 8)
        {
            throw Toc("header passes the end at +0");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(toc);
        int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(toc.AsSpan(4));
        if (count < 0)
        {
            throw Toc("record count is negative at +0");
        }

        if (chunkCount != chunks.Count)
        {
            throw Toc("chunk count does not match the chunk streams at +4");
        }

        long body = 8 + (4L * chunkCount) + (4L * count);
        if (toc.Length != (body + 7) / 8 * 8)
        {
            throw Toc("length is not 8 + 4ch + 4N rounded up to 8");
        }

        int pad = toc.AsSpan((int)body).IndexOfAnyExcept((byte)0);
        if (pad >= 0)
        {
            throw Toc($"pad byte is not zero at +{body + pad}");
        }

        for (int i = 0; i < chunkCount; i++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(toc.AsSpan(8 + (4 * i))) != chunks[i].CompressedLength)
            {
                throw Toc($"chunk size does not match the compressed length at +{8 + (4 * i)}");
            }
        }

        var lengths = new int[count];
        long sum = 0;
        for (int k = 0; k < count; k++)
        {
            int at = 8 + (4 * chunkCount) + (4 * k);
            lengths[k] = BinaryPrimitives.ReadInt32LittleEndian(toc.AsSpan(at));
            if (lengths[k] <= 0 || lengths[k] % 8 != 0)
            {
                throw Toc($"record length is not a positive multiple of 8 at +{at}");
            }

            sum += lengths[k];
        }

        long block = 0;
        foreach (var chunk in chunks)
        {
            block += chunk.Data.Length;
        }

        if (sum != block)
        {
            throw Toc("record lengths do not sum to the inflated chunk length");
        }

        return lengths;
    }

    // A record inside one chunk stays a slice of it; a crossing record is copied once. There is no concatenated block.
    private static ReadOnlyMemory<byte>[] Cut(IReadOnlyList<LcRawStream> chunks, int[] lengths)
    {
        var records = new ReadOnlyMemory<byte>[lengths.Length];
        int c = 0, offset = 0;
        for (int k = 0; k < lengths.Length; k++)
        {
            // The sum check guarantees the remaining chunks hold every remaining record byte.
            while (offset == chunks[c].Data.Length)
            {
                c++;
                offset = 0;
            }

            int length = lengths[k];
            byte[] data = chunks[c].Data;
            if (data.Length - offset >= length)
            {
                records[k] = data.AsMemory(offset, length);
                offset += length;
                continue;
            }

            var copy = new byte[length];
            int done = data.Length - offset;
            data.AsSpan(offset).CopyTo(copy);
            while (done < length)
            {
                c++;
                offset = Math.Min(length - done, chunks[c].Data.Length);
                chunks[c].Data.AsSpan(0, offset).CopyTo(copy.AsSpan(done));
                done += offset;
            }

            records[k] = copy;
        }

        return records;
    }

    // Stops at the first record that does not parse under this width.
    private static bool TryParseAll(
        ReadOnlyMemory<byte>[] records,
        int normalBits,
        [NotNullWhen(true)] out GeometryRecord[]? parsed,
        out int failedAt,
        [NotNullWhen(false)] out InvalidDataException? error)
    {
        var result = new GeometryRecord[records.Length];
        for (int k = 0; k < records.Length; k++)
        {
            try
            {
                result[k] = GeometryRecordParser.Parse(records[k].Span, normalBits);
            }
            catch (InvalidDataException ex)
            {
                (parsed, failedAt, error) = (null, k, ex);
                return false;
            }
        }

        (parsed, failedAt, error) = (result, -1, null);
        return true;
    }

    private static InvalidDataException Toc(string rule) => new($"LcOpNwdGeometry TOC: {rule}");
}
