using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Parsing;
using Xunit;

namespace NwcIO.Tests;

public class PropertyRecordScannerTests
{
    [Fact]
    public void Scan_Tag52And53Records_DecodesCorrectly()
    {
        using var ms = new MemoryStream();

        // 1. Tag 53 node record
        WriteInt32(ms, 101);
        WriteInt32(ms, 53);
        WriteString(ms, "Layer_Ground");

        // 2. Tag 52 property record with string value
        WriteInt32(ms, 102);
        WriteInt32(ms, 52);
        WriteString(ms, "PropertyName");
        WriteString(ms, "LcRevitPropertyElementName");
        WriteInt32(ms, 4); // string type
        WriteString(ms, "Wall_Type_A");

        // 3. Tag 52 numeric property record
        WriteInt32(ms, 103);
        WriteInt32(ms, 52);
        WriteString(ms, "CategoryId");
        WriteString(ms, "LcRevitPropertyElementCategoryId");
        WriteInt32(ms, 2); // int32 type
        WriteInt32(ms, -2000011);

        var scanner = new PropertyRecordScanner(ms.ToArray());
        var records = scanner.Scan();

        Assert.Equal(3, records.Count);

        Assert.Equal(101, records[0].FirstInt);
        Assert.Equal(53, records[0].Tag);
        Assert.Equal("Layer_Ground", records[0].Name);
        Assert.Equal("Node", records[0].Class);

        Assert.Equal(102, records[1].FirstInt);
        Assert.Equal(52, records[1].Tag);
        Assert.Equal("PropertyName", records[1].Name);
        Assert.Equal("LcRevitPropertyElementName", records[1].Class);
        Assert.Equal(4, records[1].ValueType);
        Assert.Equal("Wall_Type_A", records[1].Value);

        Assert.Equal(103, records[2].FirstInt);
        Assert.Equal(52, records[2].Tag);
        Assert.Equal("CategoryId", records[2].Name);
        Assert.Equal(2, records[2].ValueType);
        Assert.Equal("-2000011", records[2].Value);
    }

    [Fact]
    public void Scan_AllVariantDataTypes_DecodesCorrectly()
    {
        using var ms = new MemoryStream();

        // 1. Double (Type 1)
        WriteInt32(ms, 201);
        WriteInt32(ms, 52);
        WriteString(ms, "Ratio");
        WriteString(ms, "LcData");
        WriteInt32(ms, 1);
        WriteDouble(ms, 3.14159);

        // 2. DateTime (Type 5) - 2026-04-01T12:00:00Z = 1775044800
        WriteInt32(ms, 202);
        WriteInt32(ms, 52);
        WriteString(ms, "ExportDate");
        WriteString(ms, "LcData");
        WriteInt32(ms, 5);
        WriteInt64(ms, 1775044800L);

        // 3. DoubleLength (Type 6)
        WriteInt32(ms, 203);
        WriteInt32(ms, 52);
        WriteString(ms, "Length");
        WriteString(ms, "LcData");
        WriteInt32(ms, 6);
        WriteDouble(ms, 2500.0);

        // 4. IdentifierString (Type 9)
        WriteInt32(ms, 204);
        WriteInt32(ms, 52);
        WriteString(ms, "Code");
        WriteString(ms, "LcData");
        WriteInt32(ms, 9);
        WriteString(ms, "ID_STRUCT_123");

        // 5. Point3D (Type 12)
        WriteInt32(ms, 205);
        WriteInt32(ms, 52);
        WriteString(ms, "Location");
        WriteString(ms, "LcData");
        WriteInt32(ms, 12);
        WriteDouble(ms, 10.0);
        WriteDouble(ms, 20.0);
        WriteDouble(ms, 30.0);

        // 6. Point2D (Type 13)
        WriteInt32(ms, 206);
        WriteInt32(ms, 52);
        WriteString(ms, "UV");
        WriteString(ms, "LcData");
        WriteInt32(ms, 13);
        WriteDouble(ms, 0.5);
        WriteDouble(ms, 0.75);

        var scanner = new PropertyRecordScanner(ms.ToArray());
        var records = scanner.Scan();

        Assert.Equal(6, records.Count);
        Assert.Equal(1, records[0].ValueType);
        Assert.Equal("3.14159", records[0].Value);

        Assert.Equal(5, records[1].ValueType);
        Assert.Equal("2026-04-01T12:00:00Z", records[1].Value);

        Assert.Equal(6, records[2].ValueType);
        Assert.Equal("2500", records[2].Value);

        Assert.Equal(9, records[3].ValueType);
        Assert.Equal("ID_STRUCT_123", records[3].Value);

        Assert.Equal(12, records[4].ValueType);
        Assert.Equal("(10, 20, 30)", records[4].Value);

        Assert.Equal(13, records[5].ValueType);
        Assert.Equal("(0.5, 0.75)", records[5].Value);
    }

    [Fact]
    public void SerializerAndScanner_RoundtripsVariantDataTypes()
    {
        var inputRecords = new List<Model.NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "DoubleProp", Class = "Tab", ValueType = 1, Value = "123.456" },
            new() { FirstInt = 2, Tag = 52, Name = "IntProp", Class = "Tab", ValueType = 2, Value = "42" },
            new() { FirstInt = 3, Tag = 52, Name = "BoolProp", Class = "Tab", ValueType = 3, Value = "1" },
            new() { FirstInt = 4, Tag = 52, Name = "StrProp", Class = "Tab", ValueType = 4, Value = "Hello World" },
            new() { FirstInt = 5, Tag = 52, Name = "DateProp", Class = "Tab", ValueType = 5, Value = "2026-04-01T12:00:00Z" },
            new() { FirstInt = 6, Tag = 52, Name = "LengthProp", Class = "Tab", ValueType = 6, Value = "100.5" },
            new() { FirstInt = 7, Tag = 52, Name = "IdProp", Class = "Tab", ValueType = 9, Value = "SYM_01" },
            new() { FirstInt = 8, Tag = 52, Name = "Point3Prop", Class = "Tab", ValueType = 12, Value = "(1, 2, 3)" },
            new() { FirstInt = 9, Tag = 52, Name = "Point2Prop", Class = "Tab", ValueType = 13, Value = "(4, 5)" },
            new() { FirstInt = 10, Tag = 52, Name = "RefProp", Class = "Tab", ValueType = 8, Value = "Target", ReferenceId = 42, ReferenceTag = 7, ReferenceClass = "LcRevitData_Element" },
        };

        var streamBytes = Internal.Container.NwcStreamSerializer.BuildSceneTreeStream(inputRecords, null);
        var scanner = new PropertyRecordScanner(streamBytes);
        var scanned = scanner.Scan();

        Assert.Equal(inputRecords.Count, scanned.Count);
        for (int i = 0; i < inputRecords.Count; i++)
        {
            Assert.Equal(inputRecords[i].ValueType, scanned[i].ValueType);
            Assert.Equal(inputRecords[i].Value, scanned[i].Value);
        }

        Assert.Equal(123.456, scanned.First(r => r.Name == "DoubleProp").DoubleValue);
        Assert.Equal(42, scanned.First(r => r.Name == "IntProp").IntValue);
        var reference = Assert.Single(scanned, r => r.Name == "RefProp");
        Assert.Equal(42, reference.ReferenceId);
        Assert.Equal(7, reference.ReferenceTag);
        Assert.Equal("LcRevitData_Element", reference.ReferenceClass);
    }

    private static void WriteDouble(MemoryStream ms, double value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteInt64(MemoryStream ms, long value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteInt32(MemoryStream ms, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        ms.Write(buf);
    }

    private static void WriteString(MemoryStream ms, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        WriteInt32(ms, bytes.Length);
        ms.Write(bytes);
        int pad = (4 - (bytes.Length & 3)) & 3;
        for (int i = 0; i < pad; i++)
        {
            ms.WriteByte(0);
        }
    }
}
