using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses the internal binary database cache directory (<c>LcOpDBCache</c>).
/// </summary>
public static class DbCacheParser
{
    // name length + flags + offset + length + a + b, with no name bytes and no PAD8
    private const int MinEntrySize = 32;

    public static NwcDbCacheDirectory? Parse(LcRawStream stream)
    {
        return Parse(stream.Data);
    }

    public static NwcDbCacheDirectory? Parse(byte[] data)
    {
        return TryParse(data, out var directory, out _) ? directory : null;
    }

    internal static bool TryParse(
        ReadOnlySpan<byte> data,
        [NotNullWhen(true)] out NwcDbCacheDirectory? directory,
        [NotNullWhen(false)] out string? error)
    {
        directory = null;
        if (data.Length < 8)
        {
            error = "DbCache directory: shorter than its 8-byte head";
            return false;
        }

        int head = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (head != -1)
        {
            error = "DbCache directory: head is not -1";
            return false;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (count < 0 || count > (data.Length - 8) / MinEntrySize)
        {
            error = "DbCache directory: entry count is negative or exceeds the data";
            return false;
        }

        int pos = 8;
        var entries = new List<NwcDbCacheEntry>(count);
        for (int i = 0; i < count; i++)
        {
            if (data.Length - pos < MinEntrySize)
            {
                error = $"DbCache directory: entry {i} is truncated";
                return false;
            }

            int nameLen = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            pos += 4;
            int padded = (nameLen + 3) & ~3;
            if (nameLen < 0 || nameLen > data.Length - pos || padded > data.Length - pos)
            {
                error = $"DbCache directory: entry {i} name length is out of range";
                return false;
            }

            // Not validated as UTF-8: the prefix may hold arbitrary bytes, only the ASCII final segment matters.
            string name = Encoding.UTF8.GetString(data.Slice(pos, nameLen));
            if (data.Slice(pos + nameLen, padded - nameLen).IndexOfAnyExcept((byte)0) >= 0)
            {
                error = $"DbCache directory: entry {i} name padding is not zero";
                return false;
            }

            pos += padded;

            // flags (4) + PAD8 (0 or 4) + offset, length (16) + a, b (8)
            if (data.Length - pos < 28)
            {
                error = $"DbCache directory: entry {i} is truncated";
                return false;
            }

            int flags = BinaryPrimitives.ReadInt32LittleEndian(data[pos..]);
            pos += 4;
            if (pos % 8 == 4)
            {
                if (BinaryPrimitives.ReadInt32LittleEndian(data[pos..]) != 0)
                {
                    error = $"DbCache directory: entry {i} alignment pad is not zero";
                    return false;
                }

                pos += 4;
            }

            if (data.Length - pos < 24)
            {
                error = $"DbCache directory: entry {i} is truncated";
                return false;
            }

            long offset = BinaryPrimitives.ReadInt64LittleEndian(data[pos..]);
            long length = BinaryPrimitives.ReadInt64LittleEndian(data[(pos + 8)..]);
            int a = BinaryPrimitives.ReadInt32LittleEndian(data[(pos + 16)..]);
            int b = BinaryPrimitives.ReadInt32LittleEndian(data[(pos + 20)..]);
            pos += 24;
            if (offset < 0 || length < 0)
            {
                error = $"DbCache directory: entry {i} has a negative offset or length";
                return false;
            }

            entries.Add(new NwcDbCacheEntry(name, flags, offset, length, ((long)b << 32) | (uint)a));
        }

        if (pos != data.Length)
        {
            error = "DbCache directory: bytes remain after the last entry";
            return false;
        }

        string dbName = "";
        foreach (var entry in entries)
        {
            string name = entry.Name;
            if (name[(name.LastIndexOf('\\') + 1)..] == "LcOpDBCache")
            {
                dbName = name;
                break;
            }
        }

        directory = new NwcDbCacheDirectory(dbName, entries);
        error = null;
        return true;
    }
}
