using System.Text;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class XRefTableParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsXRefTable()
    {
        const string json = """
            {"Type":"XRefTable","Version":1,"SavedFilename":"S:\\Projects\\Building.nwc","Table":[{"OriginalPath":"C:/Textures/concrete.png","RemappedPath":"D:/Textures/concrete.png"}]}
            """;

        // Prepend 4 bytes of arbitrary header prefix as observed in container streams
        byte[] raw = new byte[4 + Encoding.UTF8.GetByteCount(json)];
        Encoding.UTF8.GetBytes(json, 0, json.Length, raw, 4);

        var table = XRefTableParser.Parse(raw);

        Assert.NotNull(table);
        Assert.Equal("XRefTable", table.Type);
        Assert.Equal(1, table.Version);
        Assert.Equal(@"S:\Projects\Building.nwc", table.SavedFilename);
        Assert.Single(table.References);

        var xref = table.References[0];
        Assert.Equal("C:/Textures/concrete.png", xref.OriginalPath);
        Assert.Equal("D:/Textures/concrete.png", xref.RemappedPath);
        Assert.Equal("D:/Textures/concrete.png", xref.EffectivePath);
    }

    [Theory]
    [InlineData(0x0000007B)]
    [InlineData(0x0000017B)]
    [InlineData(0x00007B00)]
    [InlineData(0x007B0000)]
    [InlineData(0x7B000000)]
    [InlineData(0x7B7B7B7B)]
    [InlineData(0x0000007B, 3)]
    public void Parse_PrefixContainingOpeningBrace_ReturnsXRefTable(int prefix, int trailingNulls = 0)
    {
        const string json = """{"Type":"XRefTable","Version":1,"SavedFilename":"a.nwc","Table":[]}""";
        byte[] raw = new byte[4 + Encoding.UTF8.GetByteCount(json) + trailingNulls];
        BitConverter.GetBytes(prefix).CopyTo(raw, 0);
        Encoding.UTF8.GetBytes(json, 0, json.Length, raw, 4);

        var table = XRefTableParser.Parse(raw);

        Assert.NotNull(table);
        Assert.Equal("XRefTable", table.Type);
        Assert.Equal("a.nwc", table.SavedFilename);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 4)]
    [InlineData(true, 0)]
    [InlineData(true, 4)]
    public void Parse_NestedType_PreservesRoot(bool formatted, int prefixLength)
    {
        string json = formatted
            ? """{ "Type" : "XRefTable", "Version":1,"Table":[{"Type":"Model","OriginalPath":"A.nwc","RemappedPath":""}],"SavedFilename":"fed.nwf"}"""
            : """{"Version":1,"Type":"XRefTable","Table":[{"Type":"Model","OriginalPath":"A.nwc","RemappedPath":""}],"SavedFilename":"fed.nwf"}""";
        byte[] data = new byte[prefixLength + Encoding.UTF8.GetByteCount(json) + 3];
        Encoding.UTF8.GetBytes(json, 0, json.Length, data, prefixLength);

        var table = XRefTableParser.Parse(data);

        Assert.NotNull(table);
        Assert.Equal("XRefTable", table.Type);
        Assert.Equal(1, table.Version);
        Assert.Equal("fed.nwf", table.SavedFilename);
        Assert.Equal("A.nwc", Assert.Single(table.References).OriginalPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_PrefixContainingOpeningBrace_NestedType_PreservesRoot(bool formatted)
    {
        string json = formatted
            ? """{ "Version" : 1, "Type" : "XRefTable", "Table" : [ { "Type" : "Model", "OriginalPath" : "A.nwc", "RemappedPath" : "" } ], "SavedFilename" : "fed.nwf" }"""
            : """{"Version":1,"Type":"XRefTable","Table":[{"Type":"Model","OriginalPath":"A.nwc","RemappedPath":""}],"SavedFilename":"fed.nwf"}""";
        byte[] data = new byte[4 + Encoding.UTF8.GetByteCount(json)];
        BitConverter.GetBytes(0x0000007B).CopyTo(data, 0);
        Encoding.UTF8.GetBytes(json, 0, json.Length, data, 4);

        var table = XRefTableParser.Parse(data);

        Assert.NotNull(table);
        Assert.Equal("XRefTable", table.Type);
        Assert.Equal("fed.nwf", table.SavedFilename);
        Assert.Equal("A.nwc", Assert.Single(table.References).OriginalPath);
    }

    [Fact]
    public void Parse_WithoutTypeSignature_ReturnsXRefTable()
    {
        const string json = """{ "Type" : "XRefTable", "Table" : [] }""";

        var table = XRefTableParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(table);
        Assert.Equal("XRefTable", table.Type);
        Assert.Empty(table.References);
    }

    [Theory]
    [InlineData("{\"Type\": garbage}")]
    [InlineData("{{\"Type\": garbage}")]
    [InlineData("0123456789abcdef")]
    public void Parse_MalformedJson_ReturnsNull(string data)
    {
        Assert.Null(XRefTableParser.Parse(Encoding.UTF8.GetBytes(data)));
    }

    [Fact]
    public void Parse_EmptyTable_ReturnsEmptyList()
    {
        const string json = """{"Type":"XRefTable","Version":1,"SavedFilename":"file.nwc","Table":[]}""";
        byte[] data = Encoding.UTF8.GetBytes(json);

        var table = XRefTableParser.Parse(data);

        Assert.NotNull(table);
        Assert.Empty(table.References);
    }

    [Fact]
    public void Parse_InvalidData_ReturnsNull()
    {
        byte[] garbage = [1, 2, 3, 4, 5];
        var table = XRefTableParser.Parse(garbage);
        Assert.Null(table);
    }
}
