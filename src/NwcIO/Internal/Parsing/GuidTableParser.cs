using System.Buffers.Binary;
using NwcIO.Internal.Container;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses GUID table streams: [int32 a][int32 count][count x 16-byte little-endian GUIDs].
/// </summary>
internal static class GuidTableParser
{
    public static bool TryParse(LcRawStream stream, out IReadOnlyList<Guid> guids)
    {
        guids = [];
        var data = stream.Data;
        if (data.Length < 8 || (data.Length - 8) % 16 != 0)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        if (count <= 0 || count != (data.Length - 8) / 16)
        {
            return false;
        }

        var result = new Guid[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new Guid(data.AsSpan(8 + 16 * i, 16));
        }

        guids = result;
        return true;
    }
}
