using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses saved viewpoints and camera orientation records from Navisworks container streams.
/// </summary>
internal static class ViewpointParser
{
    internal const int MinNameBytes = 2;
    internal const int MaxNameBytes = 120;

    private static readonly Encoding Encoding866;
    private static readonly Encoding Encoding1251;

    static ViewpointParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding866 = Encoding.GetEncoding(866);
        Encoding1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>
    /// Parses all saved viewpoints from the provided viewpoint stream.
    /// </summary>
    public static IReadOnlyList<NwcSavedViewpoint> Parse(LcRawStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Parse(stream.Data);
    }

    /// <summary>
    /// Parses all saved viewpoints from raw stream bytes.
    /// </summary>
    public static IReadOnlyList<NwcSavedViewpoint> Parse(byte[] data)
    {
        if (data is null || data.Length < 120)
        {
            return Array.Empty<NwcSavedViewpoint>();
        }

        var results = new List<NwcSavedViewpoint>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        int pos = 0;
        int maxOffset = data.Length - 96;

        while (pos <= maxOffset)
        {
            int tag = ReadInt32(data, pos);

            // Tag 517 (0x0205) is the primary Navisworks camera viewpoint record marker
            if (tag == 517 && ReadInt32(data, pos + 4) == 1)
            {
                double qx = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 36));
                double qy = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 44));
                double qz = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 52));
                double qw = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 60));

                double x = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 12));
                double y = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 20));
                double z = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 28));

                // Any unit quaternion is a valid rotation, including those with W = 0 (180° turns)
                double normSq = qx * qx + qy * qy + qz * qz + qw * qw;
                if (Math.Abs(normSq - 1.0) < 0.01 && double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z))
                {

                    double aspect = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 68));
                    double focal = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 76));
                    double height = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 84));
                    double nearDist = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 92));

                    string name = ExtractViewName(data, pos, results.Count + 1);
                    var comments = ExtractNearbyComments(data, pos);

                    // De-duplicate if exact match
                    string dedupeKey = FormattableString.Invariant($"{name}:{x:F2}:{y:F2}:{z:F2}:{qx:F3}:{qy:F3}:{qz:F3}:{qw:F3}");
                    if (seenKeys.Add(dedupeKey))
                    {
                        var camera = new NwcCamera
                        {
                            Position = new Vector3d(x, y, z),
                            Rotation = new Quaterniond(qx, qy, qz, qw),
                            FocalDistance = focal > 0 ? focal : 1.0,
                            FieldOfView = 60.0,
                            AspectRatio = aspect > 0 ? aspect : 1.0,
                            NearDistance = nearDist > 0 ? nearDist : 0.1,
                            FarDistance = focal > 0 ? focal * 10.0 : 10000.0,
                            Projection = NwcCameraProjection.Perspective,
                        };

                        results.Add(new NwcSavedViewpoint
                        {
                            Id = null,
                            Name = name,
                            Camera = camera,
                            Comments = comments,
                        });
                    }

                    // Advance past this viewpoint block
                    pos += 96;
                    continue;
                }
            }

            pos += 4;
        }

        return results;
    }

    internal static bool IsWritableName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim() != name)
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(name);
        return bytes.Length >= MinNameBytes && bytes.Length <= MaxNameBytes &&
            DecodeString(bytes, 0, bytes.Length) == name && IsMeaningfulName(name);
    }

    private static string ExtractViewName(byte[] data, int tagOffset, int fallbackIndex)
    {
        int minBack = Math.Max(0, tagOffset - 350);
        string bestName = "";

        // Look backwards for length-prefixed strings
        for (int i = tagOffset - 4; i >= minBack; i--)
        {
            int len = ReadInt32(data, i);
            if (len >= MinNameBytes && len <= MaxNameBytes && i + 4 + len <= tagOffset)
            {
                string candidate = DecodeString(data, i + 4, len);
                if (!string.IsNullOrWhiteSpace(candidate) && IsMeaningfulName(candidate))
                {
                    bestName = candidate.Trim();
                    break;
                }
            }
        }

        return string.IsNullOrWhiteSpace(bestName) ? $"Viewpoint {fallbackIndex}" : bestName;
    }

    private static List<string> ExtractNearbyComments(byte[] data, int tagOffset)
    {
        var comments = new List<string>();
        int searchEnd = Math.Min(data.Length - 4, tagOffset + 500);

        for (int i = tagOffset + 96; i < searchEnd; i += 4)
        {
            int len = ReadInt32(data, i);
            if (len >= 3 && len <= 100 && i + 4 + len <= data.Length)
            {
                string candidate = DecodeString(data, i + 4, len).Trim();
                if (candidate.StartsWith('{') && candidate.EndsWith('}') && candidate.Length > 2)
                {
                    comments.Add(candidate);
                }
                else if (candidate.Contains("BIM", StringComparison.OrdinalIgnoreCase) ||
                         candidate.Contains("MEP", StringComparison.OrdinalIgnoreCase))
                {
                    comments.Add(candidate);
                }
            }
        }

        return comments;
    }

    private static string DecodeString(byte[] data, int offset, int length)
    {
        if (length <= 0 || offset < 0 || offset + length > data.Length)
        {
            return string.Empty;
        }

        // Truncate at first null character if present
        int nullIdx = Array.IndexOf(data, (byte)0, offset, length);
        if (nullIdx >= 0)
        {
            length = nullIdx - offset;
            if (length <= 0) return string.Empty;
        }

        string result;
        // 1. UTF-8 first, validated without using an exception as control flow on untrusted bytes.
        if (Utf8.IsValid(data.AsSpan(offset, length)))
        {
            string utf8 = Encoding.UTF8.GetString(data, offset, length);
            if (utf8.All(c => !char.IsControl(c) || c is '\t' or '\n' or '\r'))
            {
                return Sanitize(utf8);
            }
        }

        // 2. Legacy 8-bit code page: Navisworks releases stored names as CP866 or CP1251 and the
        // section header does not say which. Decode both and keep the more plausible Cyrillic text;
        // CP866 was the previous unconditional choice, so it wins ties and wins whenever CP1251
        // has no Cyrillic letters at all (which is what an uppercase CP866 word looks like there).
        string cp866 = Encoding866.GetString(data, offset, length);
        string cp1251 = Encoding1251.GetString(data, offset, length);
        int cyrillic866 = CyrillicCount(cp866);
        int cyrillic1251 = CyrillicCount(cp1251);
        bool prefer1251 = cyrillic1251 > 0 && (cyrillic866 == 0 ||
            LegacyTextScore(cp1251) > LegacyTextScore(cp866) + 6);
        result = prefer1251 ? cp1251 : cp866;
        return Sanitize(result);
    }

    private static int CyrillicCount(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c is >= '\u0400' and <= '\u04FF')
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Scores decoded legacy text: Russian letter frequencies plus penalties for box-drawing,
    /// symbol-noise and non-Russian Cyrillic letters that a wrong code page produces.
    /// </summary>
    private static double LegacyTextScore(string text)
    {
        double score = 0;
        bool atWordStart = true;
        bool wordHasLower = false;
        int innerUppercase = 0;

        void EndWord()
        {
            // A whole word in capitals is normal (УЗЕЛ, ХВС); only mixed case is evidence of a wrong
            // code page, so the penalty applies only when the word also has lowercase letters.
            if (wordHasLower)
            {
                score -= 6 * innerUppercase;
            }

            atWordStart = true;
            wordHasLower = false;
            innerUppercase = 0;
        }

        foreach (char raw in text)
        {
            char c = char.ToLowerInvariant(raw);
            if (c is >= 'а' and <= 'я')
            {
                score += RussianLetterFrequency(c);
                if (char.IsUpper(raw) && !atWordStart)
                {
                    innerUppercase++;
                }

                if (char.IsLower(raw))
                {
                    wordHasLower = true;
                }

                atWordStart = false;
            }
            else if (c == 'ё')
            {
                score += 0.2;
                atWordStart = false;
            }
            else if (c is 'і' or 'ї' or 'є' or 'ґ')
            {
                score += 0.1;
                atWordStart = false;
            }
            else if (c is >= '\u0400' and <= '\u04FF')
            {
                score -= 4;
                atWordStart = false;
            }
            else if (c is >= '\u2500' and <= '\u259F')
            {
                score -= 12;
                EndWord();
            }
            else if (c is '\u2219' or '\u00A4' or '\u00A9' or '\u00AE' or '\u00B7')
            {
                score -= 6;
                EndWord();
            }
            else if (c is '\u00A0' or '\u00AD' or '\uFFFD')
            {
                // CP866 lowercase letters land on NBSP and soft hyphen when read as CP1251.
                score -= 10;
                EndWord();
            }
            else if (c is (>= '\u0080' and <= '\u009F') or
                (>= '\u2018' and <= '\u201E') or (>= '\u2020' and <= '\u2026') or
                '\u2030' or '\u2039' or '\u203A' or '\u2122' or '\u20AC')
            {
                // Bytes 0x80-0x9F become punctuation or C1 controls in CP1251; a CP866 word
                // decoded this way must not look more plausible than the original.
                score -= 6;
                EndWord();
            }
            else if (char.IsLetter(c))
            {
                // Latin letters are equally plausible in either code page and carry no signal.
                score += 1;
                if (char.IsLower(raw))
                {
                    wordHasLower = true;
                }

                atWordStart = false;
            }
            else if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsDigit(c))
            {
                EndWord();
            }
        }

        EndWord();
        return score;
    }

    private static double RussianLetterFrequency(char c) => c switch
    {
        'о' => 11.0, 'е' => 8.5, 'а' => 8.0, 'и' => 7.4, 'н' => 6.7, 'т' => 6.3, 'с' => 5.5,
        'р' => 4.7, 'в' => 4.5, 'л' => 4.4, 'к' => 3.5, 'м' => 3.2, 'д' => 3.0, 'п' => 2.8,
        'у' => 2.6, 'я' => 2.0, 'ы' => 1.9, 'ь' => 1.7, 'г' => 1.7, 'з' => 1.7, 'б' => 1.6,
        'ч' => 1.4, 'й' => 1.2, 'х' => 1.0, 'ж' => 0.9, 'ш' => 0.7, 'ю' => 0.6, 'ц' => 0.5,
        'щ' => 0.4, 'э' => 0.3, 'ф' => 0.3, 'ъ' => 0.05,
        _ => 0.0,
    };

    private static string Sanitize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var chars = text.Where(c => (c >= 0x20 && c <= 0xD7FF) || c == 0x09 || c == 0x0A || c == 0x0D || (c >= 0xE000 && c <= 0xFFFD)).ToArray();
        return new string(chars).Trim();
    }

    private static bool IsMeaningfulName(string text)
    {
        if (text.Length < 2) return false;
        if (text.All(c => c == '.' || c == ' ' || c == '-' || c == '_')) return false;

        // Ignore technical identifiers
        if (text.StartsWith("Lc", StringComparison.Ordinal) ||
            text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static int ReadInt32(byte[] data, int offset) =>
        offset + 4 <= data.Length ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset)) : 0;
}
