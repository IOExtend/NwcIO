using System.Security.Cryptography;
using System.Text;

namespace NwcIO.Internal;

/// <summary>
/// IFC GlobalId encoding: 128-bit GUID to 22-character base64-like string.
/// Uses IFC-specific alphabet: 0-9, A-Z, a-z, _, $.
/// Algorithm standardized by buildingSMART IFC specification.
/// </summary>
internal static class IfcGuid
{
    private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>Convert a .NET Guid to IFC GlobalId (22 chars).</summary>
    public static string ToIfcGuid(Guid guid)
    {
        var b = guid.ToByteArray();

        // Rearrange .NET byte order (little-endian fields) to UUID big-endian
        var u = new byte[16];
        u[0] = b[3]; u[1] = b[2]; u[2] = b[1]; u[3] = b[0];
        u[4] = b[5]; u[5] = b[4];
        u[6] = b[7]; u[7] = b[6];
        Array.Copy(b, 8, u, 8, 8);

        // 128 bits -> 22 base-64 chars (22 * 6 = 132 bits, so 4 padding bits)
        // Split into two 64-bit halves
        ulong hi = 0, lo = 0;
        for (int i = 0; i < 8; i++) hi = (hi << 8) | u[i];
        for (int i = 8; i < 16; i++) lo = (lo << 8) | u[i];

        var result = new char[22];
        // Char 0: top 2 bits (padded to 6)
        result[0] = Chars[(int)(hi >> 62) & 0x3F];
        // Chars 1-10: next 60 bits from hi
        result[1] = Chars[(int)(hi >> 56) & 0x3F];
        result[2] = Chars[(int)(hi >> 50) & 0x3F];
        result[3] = Chars[(int)(hi >> 44) & 0x3F];
        result[4] = Chars[(int)(hi >> 38) & 0x3F];
        result[5] = Chars[(int)(hi >> 32) & 0x3F];
        result[6] = Chars[(int)(hi >> 26) & 0x3F];
        result[7] = Chars[(int)(hi >> 20) & 0x3F];
        result[8] = Chars[(int)(hi >> 14) & 0x3F];
        result[9] = Chars[(int)(hi >> 8) & 0x3F];
        result[10] = Chars[(int)(hi >> 2) & 0x3F];
        // Char 11: 2 bits from hi + 4 bits from lo
        result[11] = Chars[(int)(((hi & 0x03) << 4) | ((lo >> 60) & 0x0F))];
        // Chars 12-21: remaining 60 bits from lo
        result[12] = Chars[(int)(lo >> 54) & 0x3F];
        result[13] = Chars[(int)(lo >> 48) & 0x3F];
        result[14] = Chars[(int)(lo >> 42) & 0x3F];
        result[15] = Chars[(int)(lo >> 36) & 0x3F];
        result[16] = Chars[(int)(lo >> 30) & 0x3F];
        result[17] = Chars[(int)(lo >> 24) & 0x3F];
        result[18] = Chars[(int)(lo >> 18) & 0x3F];
        result[19] = Chars[(int)(lo >> 12) & 0x3F];
        result[20] = Chars[(int)(lo >> 6) & 0x3F];
        result[21] = Chars[(int)lo & 0x3F];

        return new string(result);
    }

    public static bool TryFromIfcGuid(string text, out Guid guid)
    {
        guid = default;
        if (text.Length != 22 || text[0] is < '0' or > '3')
            return false;

        UInt128 bits = 0;
        foreach (char ch in text)
        {
            int digit = Chars.IndexOf(ch);
            if (digit < 0)
                return false;
            bits = (bits << 6) | (uint)digit;
        }

        Span<byte> bytes = stackalloc byte[16];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt128BigEndian(bytes, bits);
        guid = new Guid(bytes, bigEndian: true);
        return true;
    }

    /// <summary>Generate a new random IFC GUID (22 chars).</summary>
    public static string NewGuid() => ToIfcGuid(Guid.NewGuid());

    /// <summary>Generate a deterministic IFC GUID from element ID and a namespace string.</summary>
    public static string FromElementId(int elemId, string ns = "NwcIO")
    {
        var bytes = Encoding.UTF8.GetBytes($"{ns}:{elemId}");
        var hash = SHA256.HashData(bytes);
        var guidBytes = new byte[16];
        Array.Copy(hash, 0, guidBytes, 0, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x30);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return ToIfcGuid(new Guid(guidBytes));
    }

    /// <summary>Generate a deterministic IFC GUID from a string key.</summary>
    public static string FromKey(string key)
    {
        var bytes = Encoding.UTF8.GetBytes($"NwcIO:key:{key}");
        var hash = SHA256.HashData(bytes);
        var guidBytes = new byte[16];
        Array.Copy(hash, 0, guidBytes, 0, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x30);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return ToIfcGuid(new Guid(guidBytes));
    }
}
