using System.Buffers.Binary;
using NwcIO.Internal.Container;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Assigns a <see cref="NwcStreamKind"/> to a decompressed stream based on its leading bytes.
/// Classification is best-effort: unrecognized streams stay <see cref="NwcStreamKind.Unknown"/>
/// rather than being guessed into a wrong category.
/// </summary>
public static class StreamClassifier
{
    public static void ClassifyAll(IReadOnlyList<LcRawStream> streams)
    {
        for (int i = 0; i < streams.Count; i++)
        {
            streams[i].Kind = Classify(streams[i]);
        }

        // Post-pass: TOC catalogs preceding geometry chunks in Section 0
        var toc = streams.FirstOrDefault(s => s.Kind == NwcStreamKind.Toc);
        if (toc is not null && toc.Data.Length >= 8)
        {
            int chunkCount = ReadInt32(toc.Data, 4);
            int start = Math.Max(0, toc.Index - chunkCount);
            for (int i = start; i < toc.Index; i++)
            {
                if (streams[i].SectionIndex == toc.SectionIndex && streams[i].Kind == NwcStreamKind.Unknown)
                {
                    streams[i].Kind = NwcStreamKind.Geometry;
                }
            }
        }
    }

    public static NwcStreamKind Classify(LcRawStream stream)
    {
        var data = stream.Data;
        if (data.Length < 8)
        {
            return NwcStreamKind.Unknown;
        }

        // Partition header: starts with int64 7, then a length-prefixed source path.
        if (BinaryPrimitives.ReadInt64LittleEndian(data) == 7 && LooksLikeLengthPrefixedText(data, 8))
        {
            return NwcStreamKind.PartitionHeader;
        }

        // Scene tree / property records: starts with [100][86] or contains LcRevit signatures.
        if ((ReadInt32(data, 0) == 100 && ReadInt32(data, 4) == 86) ||
            ContainsAscii(data, 0, Math.Min(data.Length, 256), "LcRevit"))
        {
            return NwcStreamKind.SceneTree;
        }

        // Transforms: [n][100][13].
        if (data.Length >= 12 && ReadInt32(data, 4) == 100 && ReadInt32(data, 8) == 13)
        {
            return NwcStreamKind.Transforms;
        }

        // Mesh record IDs are arbitrary. Recognize a vertex buffer before the GUID heuristic.
        if (data.Length >= 24 && ReadInt32(data, 4) is 94 or 95 && ReadInt32(data, 12) == 60)
        {
            var p1 = ReadInt32(data, 16);
            var p2 = ReadInt32(data, 20);
            var vertexCount = p2 > 0 && p2 % 3 == 0 ? p2 / 3 : p1;
            if (vertexCount >= 3 && 24L + 12L * vertexCount <= data.Length)
            {
                return NwcStreamKind.Geometry;
            }
        }

        // TOC: [int32 recCount][int32 chunkCount][chunkCount x int32 sizes][recCount x int32 lengths]
        // Its size and chunk count can also satisfy the GUID-table heuristic.
        if (data.Length >= 16)
        {
            var recCount = ReadInt32(data, 0);
            var chunkCount = ReadInt32(data, 4);
            if (recCount > 0 && recCount < 1_000_000 && chunkCount > 0 && chunkCount < 5000)
            {
                var expected = 8 + 4 * chunkCount + 4 * recCount;
                if (data.Length == expected || data.Length == expected + 4)
                {
                    // GUID bytes can also match the header; catalog sizes and lengths must be positive.
                    var validEntries = true;
                    for (var offset = 8; offset < expected; offset += 4)
                    {
                        if (ReadInt32(data, offset) <= 0)
                        {
                            validEntries = false;
                            break;
                        }
                    }
                    if (validEntries)
                    {
                        return NwcStreamKind.Toc;
                    }
                }
            }
        }

        // GUID table: [a][count] followed by exactly count 16-byte GUIDs.
        if (data.Length >= 8 && (data.Length - 8) % 16 == 0)
        {
            int count = ReadInt32(data, 4);
            if (count > 0 && count == (data.Length - 8) / 16)
            {
                return NwcStreamKind.Guids;
            }
        }

        // XRefTable JSON: a length-prefixed JSON document.
        if (ContainsAscii(data, 0, Math.Min(data.Length, 128), "{\"Type\":"))
        {
            return NwcStreamKind.XRefJson;
        }

        if (ContainsAscii(data, 0, Math.Min(data.Length, 64), "LcOpDBCache"))
        {
            return NwcStreamKind.DbCache;
        }

        // Geometry mesh records starting with [100][94] or [100][95]
        if (data.Length >= 8 && ReadInt32(data, 0) == 100 && (ReadInt32(data, 4) is 94 or 95))
        {
            return NwcStreamKind.Geometry;
        }

        // Saved viewpoints and cameras (starts with tag 517 + camera quaternion)
        if (data.Length >= 120 && IsSavedViewsStream(data))
        {
            return NwcStreamKind.SavedViews;
        }

        // Selection sets / search sets (tag 100 + 105 or XML containing <selectionsets>)
        if ((ReadInt32(data, 0) == 100 && ReadInt32(data, 4) == 105) ||
            ContainsAscii(data, 0, Math.Min(data.Length, 256), "<selectionsets>"))
        {
            return NwcStreamKind.SelectionSets;
        }

        // 4D TimeLiner schedules (tag 100 + 106 or CSV header)
        if ((ReadInt32(data, 0) == 100 && ReadInt32(data, 4) == 106) ||
            ContainsAscii(data, 0, Math.Min(data.Length, 128), "Task ID,Task Name"))
        {
            return NwcStreamKind.TimeLiner;
        }

        return NwcStreamKind.Unknown;
    }

    private static bool IsSavedViewsStream(byte[] data)
    {
        int limit = Math.Min(data.Length - 96, 512);
        for (int i = 0; i <= limit; i += 4)
        {
            if (ReadInt32(data, i) == 517 && ReadInt32(data, i + 4) == 1)
            {
                // Check if quaternion at i + 36 is approximately normalized
                double qx = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(i + 36));
                double qy = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(i + 44));
                double qz = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(i + 52));
                double qw = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(i + 60));
                double norm = qx * qx + qy * qy + qz * qz + qw * qw;
                // W = 0 is a 180° turn, so the camera position, not W, rules out a chance match
                if (Math.Abs(norm - 1.0) < 0.005 && HasFinitePosition(data, i))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasFinitePosition(byte[] data, int offset) =>
        double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset + 12))) &&
        double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset + 20))) &&
        double.IsFinite(BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(offset + 28)));

    private static int ReadInt32(byte[] data, int offset) =>
        offset + 4 <= data.Length ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)) : 0;

    private static bool LooksLikeLengthPrefixedText(byte[] data, int offset)
    {
        if (offset + 4 > data.Length)
        {
            return false;
        }

        int len = ReadInt32(data, offset);
        if (len is <= 0 or > 8192 || offset + 4 + len > data.Length)
        {
            return false;
        }

        // Path-ish text: UTF-8, mostly printable.
        for (int i = 0; i < Math.Min(len, 64); i++)
        {
            byte b = data[offset + 4 + i];
            if (b < 0x20 && b != 0x0A)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAscii(byte[] data, int start, int end, string needle)
    {
        if (end + needle.Length > data.Length)
        {
            end = data.Length - needle.Length;
        }

        for (int i = start; i <= end; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (data[i + j] != (byte)needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
