using System.Buffers.Binary;
using System.Text;
using NwcIO.Model;

namespace NwcIO.Internal.Container;

internal static class NativeCacheChecksum
{
    // This fixed format constant encodes the directory's Adler-32 for lichunk-008.
    private static readonly BlowfishBlock EncodingBlock = new(
        Convert.FromHexString("5b515893711d22e3d04def350953a42550f01329064615cce3b758982927c4c1"));

    public static void Write(IReadOnlyList<NwcDbCacheEntry> entries, Span<byte> output)
    {
        uint a = 1, b = 0;
        Span<byte> fields = stackalloc byte[28];
        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteInt32LittleEndian(fields, entry.Flags);
            BinaryPrimitives.WriteInt64LittleEndian(fields[4..], entry.Offset);
            BinaryPrimitives.WriteInt64LittleEndian(fields[12..], entry.Length);
            BinaryPrimitives.WriteInt64LittleEndian(fields[20..], entry.Extra);
            Add(Encoding.UTF8.GetBytes(entry.Name), ref a, ref b);
            Add(fields, ref a, ref b);
        }

        uint checksum = (b << 16) | a;
        var (left, right) = EncodingBlock.Encrypt(checksum, checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(output, left);
        BinaryPrimitives.WriteUInt32LittleEndian(output[4..], right);
    }

    private static void Add(ReadOnlySpan<byte> bytes, ref uint a, ref uint b)
    {
        const uint modulus = 65521;
        foreach (byte value in bytes)
        {
            a = (a + value) % modulus;
            b = (b + a) % modulus;
        }
    }
}
