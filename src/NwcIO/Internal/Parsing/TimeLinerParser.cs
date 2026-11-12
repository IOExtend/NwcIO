using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model.TimeLiner;

namespace NwcIO.Internal.Parsing;

internal static class TimeLinerParser
{
    public static NwcTimeLinerSchedule? Parse(LcRawStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var data = stream.Data;
        if (data.Length < 8) return null;

        // Binary header [100][106][count][length][XML or legacy CSV bytes]
        if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0)) == 100 &&
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)) == 106 &&
            data.Length >= 16)
        {
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
            if (payloadLength > 0 && payloadLength <= data.Length - 16)
            {
                using var ms = new MemoryStream(data, 16, payloadLength, writable: false);
                return ReadPayload(ms);
            }
        }

        // Direct XML or legacy CSV payload
        try
        {
            using var ms = new MemoryStream(data, writable: false);
            return ReadPayload(ms);
        }
        catch
        {
            return null;
        }
    }

    private static NwcTimeLinerSchedule ReadPayload(Stream stream)
    {
        var position = stream.Position;
        int firstCharacter;
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        {
            // StreamReader handles the BOM; skip whitespace before the XML marker.
            do
            {
                firstCharacter = reader.Read();
            }
            while (firstCharacter >= 0 && char.IsWhiteSpace((char)firstCharacter));
        }

        stream.Position = position;
        return firstCharacter == '<'
            ? NwcTimeLinerXmlSerializer.Read(stream)
            : NwcTimeLinerCsvSerializer.Read(stream);
    }
}
