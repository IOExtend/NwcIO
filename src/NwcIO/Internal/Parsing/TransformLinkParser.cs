using System.Buffers.Binary;
using NwcIO.Internal.Container;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Reads the writer's per-mesh transform link stream ("NWTL"): one index into the transform stream per
/// mesh, -1 for none. Indices stay unique when merged documents repeat <see cref="NwcTransform.Id"/> values.
/// </summary>
internal static class TransformLinkParser
{
    public static IReadOnlyList<int>? TryParse(LcRawStream stream)
    {
        var data = stream.Data;
        if (data.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0)) != TransformStreamFormat.LinksMagic)
        {
            return null;
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        if (count < 0 || 8L + 4L * count > data.Length)
        {
            return null;
        }

        var links = new int[count];
        for (int i = 0; i < count; i++)
        {
            links[i] = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8 + 4 * i));
        }

        return links;
    }
}
