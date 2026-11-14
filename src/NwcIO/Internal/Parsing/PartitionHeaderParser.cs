using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses the partition-0 header stream (kind <see cref="NwcStreamKind.PartitionHeader"/>):
/// [int64 7][len-prefixed source path][zero pad to 4][int32 -1][zero pad to 8 from the stream start]
/// [int64 timestamp][int64 source size][len-prefixed loader plugin][zero pad to 4]
/// followed by export options (partially decoded). It stops at the first unexpected field.
/// </summary>
public static class PartitionHeaderParser
{
    public static NwcFileInfo Parse(LcRawStream stream)
    {
        var info = new NwcFileInfo();
        var data = stream.Data;
        int pos = 8; // skip int64 marker

        if (!TryReadLengthPrefixedString(data, ref pos, out var path, out bool padded))
        {
            return info;
        }

        info.SourcePath = path;
        if (!padded || pos + 4 > data.Length || BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)) != -1)
        {
            return info;
        }

        pos += 4;
        if (pos % 8 == 4)
        {
            if (pos + 4 > data.Length || BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos)) != 0)
            {
                return info;
            }

            pos += 4;
        }

        // [int64 timestamp][int64 source size]
        if (pos + 16 > data.Length)
        {
            return info;
        }

        long seconds = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(pos));
        if (seconds is > 0 and <= 253402300799)
        {
            info.SourceTimestampUtc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }

        pos += 16;

        if (!TryReadLengthPrefixedString(data, ref pos, out var plugin, out padded))
        {
            return info;
        }

        info.LoaderPlugin = plugin!.TrimEnd('\0');
        if (padded)
        {
            info.Options = ScanOptionStrings(data, pos);
        }

        return info;
    }

    /// <summary>
    /// Collects length-prefixed UTF-8 strings after the plugin name.
    /// The option record layout is not fully decoded yet; the raw string sequence
    /// preserves names and values in order until the scanner loses the pattern.
    /// </summary>
    private static List<string> ScanOptionStrings(byte[] data, int pos)
    {
        var result = new List<string>();
        while (pos + 4 <= data.Length)
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
            if (len is <= 0 or > 4096 || pos + 4 + len > data.Length)
            {
                break;
            }

            var candidate = data.AsSpan(pos + 4, len);
            bool printable = true;
            foreach (var b in candidate)
            {
                if (b < 0x20 && b != 0x0A)
                {
                    printable = false;
                    break;
                }
            }

            if (!printable)
            {
                break;
            }

            result.Add(Encoding.UTF8.GetString(candidate));
            pos += 4 + len;
        }

        return result;
    }

    /// <summary>Reads [i32 len][len bytes] and steps over the zero pad to 4; <paramref name="padded"/> is false when the pad runs past the end or is not zero.</summary>
    private static bool TryReadLengthPrefixedString(byte[] data, ref int pos, out string? value, out bool padded)
    {
        value = null;
        padded = false;
        if (pos + 4 > data.Length)
        {
            return false;
        }

        int len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
        if (len is <= 0 or > 8192 || pos + 4 + len > data.Length)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(data, pos + 4, len);
        int end = pos + 4 + len;
        pos += 4 + ((len + 3) & ~3);
        padded = pos <= data.Length && !data.AsSpan(end, pos - end).ContainsAnyExcept((byte)0);
        return true;
    }
}
