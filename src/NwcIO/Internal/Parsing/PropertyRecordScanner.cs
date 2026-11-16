using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Scans scene-tree/property streams (kind <see cref="Internal.Container.NwcStreamKind.SceneTree"/>) into
/// <see cref="NwcPropertyRecord"/>s.
/// </summary>
/// <remarks>
/// Observed record layout:
/// - Tag 53 (0x35): Hierarchy node name: [int32 id][int32 53][len-prefixed UTF-8 node name + 4B pad]
/// - Tag 52 (0x34): Property or element record:
///   [int32 id][int32 52][len-prefixed name + pad][len-prefixed class + pad]
///   followed by [int32 valueType] and type-specific payload:
///     - 4, 9: string (len-prefixed + pad)
///     - 0, 2, 3: int32 / bool / counter (4 bytes)
///     - 1, 6, 7, 10, 11: double (8 bytes)
///     - 5: DateTime stored as int64 Unix epoch seconds (8 bytes)
///     - 8: reference element [refId:4][refTag:4][refName][refClass]
///     - 12: Point3D (24 bytes)
///     - 13: Point2D (16 bytes)
/// The scanner searches forward resiliently for tags 52/53 across the decompressed chunk.
/// </remarks>
public sealed class PropertyRecordScanner
{
    private readonly byte[] _data;
    private int _pos;

    public PropertyRecordScanner(byte[] data) => _data = data;

    /// <summary>Byte offset where scanning stopped.</summary>
    public int StoppedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public List<NwcPropertyRecord> Scan()
    {
        var records = new List<NwcPropertyRecord>();
        _pos = 0;
        StoppedAt = 0;
        FailureReason = null;

        while (_pos + 8 <= _data.Length)
        {
            int recId = ReadInt32At(_pos);
            int tag = ReadInt32At(_pos + 4);

            if (tag == 53) // hierarchy node name
            {
                int pNext = _pos + 8;
                if (TryReadLengthPrefixedString(ref pNext, out var nodeName) && !string.IsNullOrEmpty(nodeName))
                {
                    int? parentId = null;
                    if (pNext + 8 <= _data.Length && ReadInt32At(pNext) == -2)
                    {
                        parentId = ReadInt32At(pNext + 4);
                        pNext += 8;
                    }

                    records.Add(new NwcPropertyRecord
                    {
                        FirstInt = recId,
                        Tag = tag,
                        Name = nodeName,
                        Class = "Node",
                        ValueType = 0,
                        Value = null,
                        ParentId = parentId,
                    });
                    _pos = pNext;
                    continue;
                }
            }
            else if (tag == 52) // property / element
            {
                int pNext = _pos + 8;
                if (TryReadLengthPrefixedString(ref pNext, out var name) &&
                    !string.IsNullOrEmpty(name) &&
                    TryReadLengthPrefixedString(ref pNext, out var className) &&
                    !string.IsNullOrEmpty(className))
                {
                    int valueType = 0;
                    string? value = null;
                    long? intValue = null;
                    double? doubleValue = null;
                    int? referenceId = null;
                    int? referenceTag = null;
                    string? referenceClass = null;

                    if (pNext + 4 <= _data.Length)
                    {
                        valueType = ReadInt32At(pNext);
                        pNext += 4;

                        switch (valueType)
                        {
                            case 4: // DisplayString
                            case 9: // IdentifierString
                                if (TryReadLengthPrefixedString(ref pNext, out var strVal))
                                {
                                    value = strVal;
                                }
                                break;
                            case 0:
                            case 2:
                            case 3: // int32 / bool / counter
                                if (pNext + 4 <= _data.Length)
                                {
                                    intValue = ReadInt32At(pNext);
                                    value = intValue.Value.ToString(CultureInfo.InvariantCulture);
                                    pNext += 4;
                                }
                                break;
                            case 1:
                            case 6:
                            case 7:
                            case 10:
                            case 11: // double / double length / angle / area / volume
                                if (pNext + 8 <= _data.Length)
                                {
                                    doubleValue = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(pNext));
                                    value = doubleValue.Value.ToString(CultureInfo.InvariantCulture);
                                    pNext += 8;
                                }
                                break;
                            case 5: // DateTime (int64 unix seconds)
                                if (pNext + 8 <= _data.Length)
                                {
                                    long unixSec = BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(pNext));
                                    intValue = unixSec;
                                    value = HierarchyParser.FormatUnixSeconds(unixSec);
                                    pNext += 8;
                                }
                                break;
                            case 8: // reference element / NamedConstant: [refId][refTag][refName][refClass]
                                if (pNext + 8 <= _data.Length)
                                {
                                    int refId = ReadInt32At(pNext);
                                    int refTag = ReadInt32At(pNext + 4);
                                    pNext += 8;
                                    if (TryReadLengthPrefixedString(ref pNext, out var refName))
                                    {
                                        TryReadLengthPrefixedString(ref pNext, out var refClass);
                                        value = refName;
                                        referenceId = refId;
                                        referenceTag = refTag;
                                        referenceClass = refClass;
                                    }
                                }
                                break;
                            case 12: // Point3D (3 doubles)
                                if (pNext + 24 <= _data.Length)
                                {
                                    double x = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(pNext));
                                    double y = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(pNext + 8));
                                    double z = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(pNext + 16));
                                    value = string.Create(CultureInfo.InvariantCulture, $"({x}, {y}, {z})");
                                    pNext += 24;
                                }
                                break;
                            case 13: // Point2D (2 doubles)
                                if (pNext + 16 <= _data.Length)
                                {
                                    double x = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(pNext));
                                    double y = BinaryPrimitives.ReadDoubleLittleEndian(_data.AsSpan(pNext + 8));
                                    value = string.Create(CultureInfo.InvariantCulture, $"({x}, {y})");
                                    pNext += 16;
                                }
                                break;
                        }
                    }

                    records.Add(new NwcPropertyRecord
                    {
                        FirstInt = recId,
                        Tag = tag,
                        Name = name,
                        Class = className,
                        ValueType = valueType,
                        Value = value,
                        IntValue = intValue,
                        DoubleValue = doubleValue,
                        ReferenceId = referenceId,
                        ReferenceTag = referenceTag,
                        ReferenceClass = referenceClass,
                    });
                    _pos = pNext;
                    continue;
                }
            }

            _pos += 4;
        }

        StoppedAt = _pos;
        return records;
    }

    private int ReadInt32At(int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset));

    private bool TryReadLengthPrefixedString(ref int pos, out string? value)
    {
        value = null;
        if (pos + 4 > _data.Length)
        {
            return false;
        }

        int len = ReadInt32At(pos);
        if (len is < 0 or > 65536 || pos + 4 + len > _data.Length)
        {
            return false;
        }

        if (len == 0)
        {
            value = string.Empty;
            pos += 4;
            return true;
        }

        value = Encoding.UTF8.GetString(_data, pos + 4, len);
        pos += 4 + len;
        pos += (4 - (len & 3)) & 3; // 4-byte padding
        return true;
    }
}
