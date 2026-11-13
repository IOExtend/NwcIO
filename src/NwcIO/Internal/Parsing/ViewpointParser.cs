using System.Buffers.Binary;
using System.Text;
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
        // 1. Try UTF-8
        try
        {
            string utf8 = Encoding.UTF8.GetString(data, offset, length);
            if (!utf8.Contains('\uFFFD') && utf8.All(c => !char.IsControl(c) || c == '\t'))
            {
                result = utf8;
                return Sanitize(result);
            }
        }
        catch { }

        // 2. Try CP866 (common in Navisworks localized releases)
        string cp866 = Encoding866.GetString(data, offset, length);
        if (cp866.All(c => !char.IsControl(c) || c == '\t'))
        {
            result = cp866;
            return Sanitize(result);
        }

        // 3. Fallback to CP1251
        result = Encoding1251.GetString(data, offset, length);
        return Sanitize(result);
    }

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
