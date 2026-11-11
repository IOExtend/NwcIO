using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class DbCacheParserTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(5)]
    public void Parse_SyntheticDbCache_DecodesEntriesCorrectly(int magic)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: magic, entryCount 2, name "LcOpDBCache" (len 11)
        writer.Write(magic);
        writer.Write(2); // 2 entries
        byte[] nameBytes = Encoding.ASCII.GetBytes("LcOpDBCache");
        writer.Write(nameBytes.Length);
        writer.Write(nameBytes);
        int pad = ((nameBytes.Length + 3) & ~3) - nameBytes.Length;
        for (int p = 0; p < pad; p++) writer.Write((byte)0);

        // 4 x int64 metadata
        writer.Write((long)1);
        writer.Write((long)52);
        writer.Write((long)674);
        writer.Write((long)0);

        // Entry 1: "cache\LcOpNwdGeometry", flags 1, offset 1000, len 5000, extra 0
        WriteEntry(writer, @"cache\LcOpNwdGeometry", 1, 1000L, 5000L, 0L);

        // Entry 2: "cache\LcOpGuidStore", flags 1, offset 6000, len 200, extra 0
        WriteEntry(writer, @"cache\LcOpGuidStore", 1, 6000L, 200L, 0L);

        byte[] data = ms.ToArray();
        var db = DbCacheParser.Parse(data);

        Assert.NotNull(db);
        Assert.Equal("LcOpDBCache", db.DatabaseName);
        Assert.Equal(2, db.Entries.Count);

        var geomEntry = db.Find("LcOpNwdGeometry");
        Assert.NotNull(geomEntry);
        Assert.Equal(@"cache\LcOpNwdGeometry", geomEntry.Name);
        Assert.Equal(1, geomEntry.Flags);
        Assert.Equal(1000L, geomEntry.Offset);
        Assert.Equal(5000L, geomEntry.Length);

        var guidEntry = db.Find("LcOpGuidStore");
        Assert.NotNull(guidEntry);
        Assert.Equal(6000L, guidEntry.Offset);
        Assert.Equal(200L, guidEntry.Length);

        Assert.Null(db.Find("NonExistentEntry"));
    }

    private static void WriteEntry(BinaryWriter writer, string name, int flags, long offset, long len, long extra)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(name);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        int pad = ((bytes.Length + 3) & ~3) - bytes.Length;
        for (int p = 0; p < pad; p++) writer.Write((byte)0);

        writer.Write(flags);
        writer.Write(offset);
        writer.Write(len);
        writer.Write(extra);
    }
}
