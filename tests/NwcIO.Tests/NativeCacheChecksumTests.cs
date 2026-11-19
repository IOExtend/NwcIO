using NwcIO.Internal.Container;
using Xunit;

namespace NwcIO.Tests;

public class NativeCacheChecksumTests
{
    [Fact]
    public void BlowfishBlockMatchesThePublishedZeroVector()
    {
        var cipher = new BlowfishBlock(new byte[8]);
        Assert.Equal((0x4ef99745u, 0x6198dd78u), cipher.Encrypt(0, 0));
    }

    [Fact]
    public void FooterUsesNamesWithoutTheirLengthOrAlignmentBytes()
    {
        Span<byte> actual = stackalloc byte[8];
        NativeCacheChecksum.Write(
            [new(@"cache\LcOpNwdGeometry", 1, 52, 71, 4 + (12L << 32)),
             new("LcOpOdyFileInfoChunk", 1, 123, 82, 0)],
            actual);
        Assert.Equal("512F80DC6C253E61", Convert.ToHexString(actual));
    }
}
