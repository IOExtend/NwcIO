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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Classify_SavedViewWithEitherProjection_ReturnsSavedViews(int projection)
    {
        Assert.Equal(NwcStreamKind.SavedViews, StreamClassifier.Classify(SavedViewStream(projection)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Classify_SavedViewWithUnknownProjection_IsNotSavedViews(int projection)
    {
        Assert.NotEqual(NwcStreamKind.SavedViews, StreamClassifier.Classify(SavedViewStream(projection)));
    }

    private static LcRawStream SavedViewStream(int projection)
    {
        var data = new byte[200];
        BinaryPrimitives.WriteInt32LittleEndian(data, 517);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), projection);
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(36), 0.0);
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(44), 0.0);
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(52), 0.0);
        BinaryPrimitives.WriteDoubleLittleEndian(data.AsSpan(60), 1.0);

        return new LcRawStream
        {
            Index = 0,
            SectionIndex = 0,
            FileOffset = 0,
            CompressedLength = 0,
            GapBefore = 0,
            Data = data,
        };
    }
}
