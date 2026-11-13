using System.IO.Compression;
using NwcIO.Model.Clash;
using Xunit;

namespace NwcIO.Tests;

public sealed class ClashBcfSourceCompatibilityTests
{
    // These calls must compile in a consumer assembly without casting the empty collection.
    [Fact]
    public void Save_EmptyCollectionExpression_CreatesEmptyBcfArchive()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nwcio_{Guid.NewGuid():N}.bcfzip");
        try
        {
            NwcClashBcfExporter.Save(path, []);

            using var archive = ZipFile.OpenRead(path);
            Assert.Equal("bcf.version", Assert.Single(archive.Entries).FullName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Write_EmptyCollectionExpression_CreatesEmptyBcfArchive()
    {
        using var stream = new MemoryStream();
        NwcClashBcfExporter.Write(stream, []);

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Equal("bcf.version", Assert.Single(archive.Entries).FullName);
    }
}
