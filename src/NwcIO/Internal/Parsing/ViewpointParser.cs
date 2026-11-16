using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;
using NwcIO.Model.Sectioning;

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
        int maxOffset = data.Length - 100;

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
                    double fieldOfView = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 84));
                    double nearDist = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pos + 92));

                    string name = ExtractViewName(data, pos, results.Count + 1);
                    int nextPos = pos + 100;
                    var extension = TryReadViewpointExtension(data, nextPos, out int extensionEnd);
                    var comments = extension is not null ? extension.Comments : ExtractNearbyComments(data, pos);
                    if (extension is not null)
                    {
                        nextPos = extensionEnd;
                    }

                    // De-duplicate if exact match
                    string dedupeKey = FormattableString.Invariant($"{name}:{x:F2}:{y:F2}:{z:F2}:{qx:F3}:{qy:F3}:{qz:F3}:{qw:F3}");
                    if (seenKeys.Add(dedupeKey))
                    {
                        var camera = new NwcCamera
                        {
                            Position = new Vector3d(x, y, z),
                            Rotation = new Quaterniond(qx, qy, qz, qw),
                            FocalDistance = focal > 0 ? focal : 1.0,
                            FieldOfView = fieldOfView > 0 ? fieldOfView : 60.0,
                            AspectRatio = aspect > 0 ? aspect : 1.0,
                            NearDistance = nearDist > 0 ? nearDist : 0.1,
                            FarDistance = extension?.FarDistance is > 0 ? extension.FarDistance : focal > 0 ? focal * 10.0 : 10000.0,
                            Projection = extension?.Projection ?? NwcCameraProjection.Perspective,
                        };

                        results.Add(new NwcSavedViewpoint
                        {
                            Id = extension?.Id,
                            Name = name,
                            Camera = camera,
                            Comments = comments,
                            SectionPlanes = extension?.SectionPlanes ?? Array.Empty<NwcSectionPlane>(),
                            SectionBox = extension?.SectionBox,
                        });
                    }

                    // Advance past this viewpoint block
                    pos = nextPos;
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

    /// <summary>Optional writer trailer after the fixed tag-517 record.</summary>
    private sealed record ViewpointExtension(
        double FarDistance,
        NwcCameraProjection Projection,
        Guid? Id,
        IReadOnlyList<string> Comments,
        IReadOnlyList<NwcSectionPlane> SectionPlanes,
        NwcSectionBox? SectionBox);

    private static ViewpointExtension? TryReadViewpointExtension(byte[] data, int offset, out int end)
    {
        end = offset;
        if (offset < 0 || offset + 8 > data.Length ||
            ReadInt32(data, offset) != ViewpointExtensionFormat.Magic)
        {
            return null;
        }

        int length = ReadInt32(data, offset + 4);
        long payloadStart = offset + 8L;
        long payloadEnd = payloadStart + length;
        if (length < 12 || payloadEnd > data.Length)
        {
            return null;
        }

        int p = (int)payloadStart;
        if (ReadInt32(data, p) != ViewpointExtensionFormat.Magic ||
            ReadInt32(data, p + 4) != ViewpointExtensionFormat.Version)
        {
            return null;
        }

        p += 8;
        if (!TryReadDouble(data, ref p, payloadEnd, out double farDistance) ||
            !TryReadInt32(data, ref p, payloadEnd, out int projectionValue) ||
            !TryReadInt32(data, ref p, payloadEnd, out int hasId))
        {
            return null;
        }

        Guid? id = null;
        if (hasId != 0)
        {
            if (p + 16 > payloadEnd)
            {
                return null;
            }

            id = new Guid(data.AsSpan(p, 16));
            p += 16;
        }

        if (!TryReadInt32(data, ref p, payloadEnd, out int commentCount) || commentCount is < 0 or > 1024)
        {
            return null;
        }

        var comments = new List<string>(commentCount);
        for (int i = 0; i < commentCount; i++)
        {
            if (!TryReadPaddedString(data, ref p, payloadEnd, out string comment))
            {
                return null;
            }

            comments.Add(comment);
        }

        if (!TryReadInt32(data, ref p, payloadEnd, out int planeCount) || planeCount is < 0 or > 64)
        {
            return null;
        }

        var planes = new List<NwcSectionPlane>(planeCount);
        for (int i = 0; i < planeCount; i++)
        {
            if (!TryReadInt32(data, ref p, payloadEnd, out int index) ||
                !TryReadInt32(data, ref p, payloadEnd, out int enabled) ||
                !TryReadDouble(data, ref p, payloadEnd, out double nx) ||
                !TryReadDouble(data, ref p, payloadEnd, out double ny) ||
                !TryReadDouble(data, ref p, payloadEnd, out double nz) ||
                !TryReadDouble(data, ref p, payloadEnd, out double distance) ||
                !TryReadInt32(data, ref p, payloadEnd, out int alignment))
            {
                return null;
            }

            planes.Add(new NwcSectionPlane
            {
                PlaneIndex = index,
                IsEnabled = enabled != 0,
                Normal = new Vector3d(nx, ny, nz),
                Distance = distance,
                Alignment = Enum.IsDefined(typeof(NwcSectionAlignment), alignment)
                    ? (NwcSectionAlignment)alignment
                    : NwcSectionAlignment.Custom,
            });
        }

        if (!TryReadInt32(data, ref p, payloadEnd, out int hasBox))
        {
            return null;
        }

        NwcSectionBox? box = null;
        if (hasBox != 0)
        {
            if (!TryReadInt32(data, ref p, payloadEnd, out int boxEnabled) ||
                !TryReadDouble(data, ref p, payloadEnd, out double minX) ||
                !TryReadDouble(data, ref p, payloadEnd, out double minY) ||
                !TryReadDouble(data, ref p, payloadEnd, out double minZ) ||
                !TryReadDouble(data, ref p, payloadEnd, out double maxX) ||
                !TryReadDouble(data, ref p, payloadEnd, out double maxY) ||
                !TryReadDouble(data, ref p, payloadEnd, out double maxZ))
            {
                return null;
            }

            box = new NwcSectionBox(new BoundingBox3d(
                new Vector3d(minX, minY, minZ),
                new Vector3d(maxX, maxY, maxZ)), boxEnabled != 0);
        }

        end = (int)payloadEnd;
        return new ViewpointExtension(
            farDistance,
            Enum.IsDefined(typeof(NwcCameraProjection), projectionValue)
                ? (NwcCameraProjection)projectionValue
                : NwcCameraProjection.Perspective,
            id,
            comments,
            planes,
            box);
    }

    private static bool TryReadInt32(byte[] data, ref int position, long end, out int value)
    {
        value = 0;
        if (position + 4 > end)
        {
            return false;
        }

        value = ReadInt32(data, position);
        position += 4;
        return true;
    }

    private static bool TryReadDouble(byte[] data, ref int position, long end, out double value)
    {
        value = 0;
        if (position + 8 > end)
        {
            return false;
        }

        value = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(position));
        position += 8;
        return true;
    }

    private static bool TryReadPaddedString(byte[] data, ref int position, long end, out string value)
    {
        value = string.Empty;
        if (position + 4 > end)
        {
            return false;
        }

        int length = ReadInt32(data, position);
        if (length < 0 || length > 65536 || position + 4L + length > end)
        {
            return false;
        }

        string text = Encoding.UTF8.GetString(data, position + 4, length);
        position += 4 + length;
        position += (4 - (length & 3)) & 3;
        if (position > end)
        {
            return false;
        }

        value = text;
        return true;
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
