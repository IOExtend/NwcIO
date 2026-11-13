namespace NwcIO.Tests;

/// <summary>Synthetic byte vectors of the file format. Every vector is a property so each access returns a fresh array.</summary>
internal static class FormatVectors
{
    public static byte[] D1 => Convert.FromHexString(
        "FFFFFFFF020000001D00000063616368655C4C634F704E77644C6F676963616C4869657261726368790000000100000000100000000000002C0100000000000000000000000000001300000063616368655C4C634F704775696453746F72650001000000000000002C1100000000000078000000000000000000000000000000");

    /// <summary>Returns a copy of <paramref name="source"/> with <paramref name="bytes"/> written at <paramref name="offset"/>; -1 appends.</summary>
    public static byte[] Patched(byte[] source, int offset, ReadOnlySpan<byte> bytes)
    {
        if (offset == -1)
        {
            var grown = new byte[source.Length + bytes.Length];
            source.CopyTo(grown, 0);
            bytes.CopyTo(grown.AsSpan(source.Length));
            return grown;
        }

        var copy = (byte[])source.Clone();
        bytes.CopyTo(copy.AsSpan(offset));
        return copy;
    }
}
