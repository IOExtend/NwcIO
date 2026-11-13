using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Parsing;
using NwcIO.Internal.Container;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class DbCacheParserTests
{
    [Fact]
    public void Open_OverflowingDbCacheNameLength_SkipsDirectory()
    {
        var data = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), int.MaxValue);
        Encoding.ASCII.GetBytes("LcOpDBCache").CopyTo(data, 12);
        using var file = new MemoryStream(NwcContainerWriter.Write([data]));

        var doc = NwcDocument.Open(file);

        Assert.Null(doc.DbCache);
    }


    [Fact]
    public void Parse_DirectoryWithPad8Entry_DecodesEveryEntry()
    {
        var db = DbCacheParser.Parse(FormatVectors.D1);
        Assert.NotNull(db);
        Assert.Equal("", db.DatabaseName); // no LcOpDBCache entry
        Assert.Equal(
            [new NwcDbCacheEntry(@"cache\LcOpNwdLogicalHierarchy", 1, 4096, 300, 0), new NwcDbCacheEntry(@"cache\LcOpGuidStore", 1, 4396, 120, 0)],
            db.Entries);
    }

    [Theory]
    [InlineData(100, 1)]   // PAD8 int non-zero
    [InlineData(41, 1)]    // name pad byte non-zero
    [InlineData(0, 0)]     // head byte 0 FF -> 00: head -256
    [InlineData(4, 3)]     // n = 3 with two entries
    [InlineData(-1, 0)]    // -1: append one trailing byte
    public void Parse_DirectoryDefect_ReturnsNull(int offset, byte value)
        => Assert.Null(DbCacheParser.Parse(FormatVectors.Patched(FormatVectors.D1, offset, [value])));

    [Fact]
    public void Parse_NonUtf8NamePrefix_MatchesFinalSegment()
    {
        byte[] name = [0xC3, 0x28, (byte)'\\', .. Encoding.ASCII.GetBytes("LcOpNwdGeometry")];
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(-1);
        writer.Write(1);
        WriteEntry(writer, name, 1, 1000L, 5000L, 0, 0);

        var db = DbCacheParser.Parse(ms.ToArray());

        Assert.NotNull(db);
        Assert.EndsWith(@"\LcOpNwdGeometry", db.Entries[0].Name);
        Assert.Same(db.Entries[0], db.Find("LcOpNwdGeometry"));
    }

    [Fact]
    public void Parse_WrittenDirectory_KeepsExtraAndPrefixedNames()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(-1);
        writer.Write(3);
        WriteEntry(writer, "LcOpDBCache", 1, 52L, 674L, 0, 0);
        WriteEntry(writer, @"Proj\Sub\LcOpNwdGeometry", 1, 1000L, 5000L, 4, 25);
        WriteEntry(writer, @"cache\LcOpGuidStore", 1, 6000L, 200L, 0, 0);

        var db = DbCacheParser.Parse(ms.ToArray());

        Assert.NotNull(db);
        Assert.Equal("LcOpDBCache", db.DatabaseName);
        Assert.Equal(3, db.Entries.Count);
        Assert.Equal(107374182404L, db.Entries[1].Extra);
        Assert.Same(db.Entries[1], db.Find("LcOpNwdGeometry"));
        Assert.Equal(6000L, db.Entries[2].Offset);
    }

    private static void WriteEntry(BinaryWriter writer, string name, int flags, long offset, long len, int a, int b)
        => WriteEntry(writer, Encoding.ASCII.GetBytes(name), flags, offset, len, a, b);

    private static void WriteEntry(BinaryWriter writer, byte[] name, int flags, long offset, long len, int a, int b)
    {
        writer.Write(name.Length);
        writer.Write(name);
        int pad = ((name.Length + 3) & ~3) - name.Length;
        for (int p = 0; p < pad; p++) writer.Write((byte)0);

        writer.Write(flags);
        if (writer.BaseStream.Position % 8 == 4)
        {
            writer.Write(0);
        }

        writer.Write(offset);
        writer.Write(len);
        writer.Write(a);
        writer.Write(b);
    }
}
