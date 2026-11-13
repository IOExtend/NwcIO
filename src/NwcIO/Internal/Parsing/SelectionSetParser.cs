using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model.Sets;

namespace NwcIO.Internal.Parsing;

internal static class SelectionSetParser
{
    public static IReadOnlyList<NwcSetItem> Parse(LcRawStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var data = stream.Data;
        if (data.Length < 8) return Array.Empty<NwcSetItem>();

        try
        {
            // Binary header [100][105][count][length][xml bytes]
            if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0)) == 100 &&
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)) == 105 &&
                data.Length >= 16)
            {
                var xmlLen = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
                if (xmlLen <= 0 || xmlLen > data.Length - 16)
                {
                    return Array.Empty<NwcSetItem>();
                }

                using var xml = new MemoryStream(data, 16, xmlLen, writable: false);
                return NwcXmlSetSerializer.Read(xml);
            }

            // Direct XML payload
            using var ms = new MemoryStream(data, writable: false);
            return NwcXmlSetSerializer.Read(ms);
        }
        catch (FormatException)
        {
            // Valid XML whose condition uses an operator this library does not know must
            // surface to the caller; swallowing it here made Open return zero sets.
            throw;
        }
        catch
        {
            // The stream is not XML at all — leave the set list empty rather than failing Open.
            return Array.Empty<NwcSetItem>();
        }
    }
}
