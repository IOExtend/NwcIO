using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using Xunit;

namespace NwcIO.Tests;

public class StreamClassifierTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Classify_GuidShapedToc_ReturnsToc(int chunkCount)
    {
        var recordCount = 3 * chunkCount;
        var data = new byte[8 + 4 * chunkCount + 4 * recordCount];
        BinaryPrimitives.WriteInt32LittleEndian(data, recordCount);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), chunkCount);
        for (var i = 0; i < chunkCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + 4 * i), 180);
        }
        for (var i = 0; i < recordCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + 4 * chunkCount + 4 * i), 60);
        }

        var stream = new LcRawStream
        {
            Index = chunkCount,
            SectionIndex = 0,
            FileOffset = 0,
            CompressedLength = 0,
            GapBefore = 0,
            Data = data,
        };

        Assert.Equal(NwcStreamKind.Toc, StreamClassifier.Classify(stream));
    }

    [Fact]
    public void Classify_LinkStreamThatMatchesLegacyTransformHeader_ReturnsTransformLinks()
    {
        // NWTL with 100 links, the first one an index of 13, also matches the legacy [n][100][13] layout.
        var data = new byte[8 + 4 * 100];
        BinaryPrimitives.WriteInt32LittleEndian(data, TransformStreamFormat.LinksMagic);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 13);
        for (var i = 1; i < 100; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + 4 * i), -1);
        }

        var stream = new LcRawStream
        {
            Index = 0,
            SectionIndex = 0,
            FileOffset = 0,
            CompressedLength = 0,
            GapBefore = 0,
            Data = data,
        };

        Assert.Equal(NwcStreamKind.TransformLinks, StreamClassifier.Classify(stream));
    }
}
