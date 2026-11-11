using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses the internal binary database cache directory (<c>LcOpDBCache</c>).
/// </summary>
public static class DbCacheParser
{
    public static NwcDbCacheDirectory? Parse(LcRawStream stream)
    {
        return Parse(stream.Data);
    }

    public static NwcDbCacheDirectory? Parse(byte[] data)
    {
        if (data.Length < 24)
        {
            return null;
        }

        int pos = 0;
        int magic = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        if (magic != -1 && magic != 0 && magic != 100)
        {
            // Try scanning for "LcOpDBCache" within the first 64 bytes
            int sigIndex = IndexOfAscii(data, 0, Math.Min(data.Length, 64), "LcOpDBCache");
            if (sigIndex >= 12)
            {
                pos = sigIndex - 8;
            }
            else
            {
                return null;
            }
        }

        if (pos + 8 > data.Length)
        {
            return null;
        }

        int entryCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        if (entryCount <= 0 || entryCount > 1000)
        {
            return null;
        }

        int nameLen = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        pos += 4;
        if (nameLen <= 0 || pos + nameLen > data.Length)
        {
            return null;
        }

        string dbName = Encoding.ASCII.GetString(data, pos, nameLen).TrimEnd('\0');
        pos += (nameLen + 3) & ~3; // 4-byte padding

        // Skip 32 bytes of header metadata (4 x int64)
        pos += 32;

        var entries = new List<NwcDbCacheEntry>(entryCount);
        for (int i = 0; i < entryCount && pos + 4 <= data.Length; i++)
        {
            int entryNameLen = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
            pos += 4;
            if (entryNameLen <= 0 || entryNameLen > 1024 || pos + entryNameLen > data.Length)
            {
                break;
            }

            string entryName = Encoding.ASCII.GetString(data, pos, entryNameLen).TrimEnd('\0');
            pos += (entryNameLen + 3) & ~3;

            if (pos + 28 > data.Length)
            {
                break;
            }

            int flags = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
            pos += 4;

            long offset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pos));
            pos += 8;

            long length = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pos));
            pos += 8;

            long extra = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pos));
            pos += 8;

            entries.Add(new NwcDbCacheEntry(entryName, flags, offset, length, extra));
        }

        return new NwcDbCacheDirectory(dbName, entries);
    }

    private static int IndexOfAscii(byte[] data, int start, int length, string needle)
    {
        int end = Math.Min(data.Length, start + length) - needle.Length;
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
                return i;
            }
        }

        return -1;
    }
}
