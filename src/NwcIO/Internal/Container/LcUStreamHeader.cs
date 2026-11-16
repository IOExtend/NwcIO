namespace NwcIO.Internal.Container;

/// <summary>
/// A parsed <c>#LcUStream-V1.1 ...</c> section header.
/// NWC/NWD files are a sequence of sections, each introduced by an ASCII header line,
/// e.g. <c>#LcUStream-V1.1 binary double release C lichunk-008</c> or
/// <c>#LcUStream-V1.1 binary double release utf8 navisworks:448 lizlib</c>.
/// </summary>
internal sealed class LcUStreamHeader
{
    public required int FileOffset { get; init; }

    /// <summary>Total header length in bytes (text line + terminating LF).</summary>
    public required int HeaderLength { get; init; }

    /// <summary>Format identifier, e.g. "LcUStream-V1.1".</summary>
    public required string Format { get; init; }

    /// <summary>Space-separated tokens after the format identifier,
    /// e.g. ["binary", "double", "release", "C", "lichunk-008"].</summary>
    public required IReadOnlyList<string> Tokens { get; init; }

    /// <summary>Text encoding declared by the section ("C" = 8-bit, "utf8").</summary>
    public string? Encoding => Tokens.FirstOrDefault(t => t is "C" or "utf8");

    /// <summary>Chunk layout marker ("lichunk-008", "lizlib", ...).</summary>
    public string? Marker => Tokens.FirstOrDefault(t => t.StartsWith("lichunk-", StringComparison.Ordinal) || t is "lizlib");

    /// <summary>Internal format version when declared ("navisworks:448" -> 448).</summary>
    public int? InternalVersion
    {
        get
        {
            var token = Tokens.FirstOrDefault(t => t.Contains(':'));
            if (token is null)
            {
                return null;
            }

            var value = token[(token.IndexOf(':') + 1)..];
            return int.TryParse(value, out var v) ? v : null;
        }
    }

    public static LcUStreamHeader Parse(byte[] data, int offset)
    {
        int end = Array.IndexOf(data, (byte)'\n', offset);
        if (end < 0)
        {
            throw new InvalidDataException($"LcUStream header at offset {offset} is not terminated.");
        }

        var line = System.Text.Encoding.ASCII.GetString(data, offset, end - offset);
        if (!line.StartsWith("#LcUStream", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected '#LcUStream' header at offset {offset}, found '{line}'.");
        }

        var parts = line.Split(' ');
        return new LcUStreamHeader
        {
            FileOffset = offset,
            HeaderLength = end - offset + 1,
            Format = parts[0][1..],
            Tokens = parts[1..],
        };
    }
}
