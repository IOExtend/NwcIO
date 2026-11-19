using System.Text.Json;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class JsonExportTests
{
    [Fact]
    public void Json_WritesTransformScaleOnlyWhenNotOne()
    {
        var doc = new NwcDocumentBuilder()
            .AddTransform(new NwcTransform { Id = 1, Scale = -1.0 })
            .AddTransform(new NwcTransform { Id = 2 })
            .Build();

        using var json = JsonDocument.Parse(doc.ToJson());
        var transforms = json.RootElement.GetProperty("transforms");

        Assert.Equal(-1.0, transforms[0].GetProperty("scale").GetDouble());
        Assert.False(transforms[1].TryGetProperty("scale", out _));
    }

    [Fact]
    public void Json_WithoutSceneTree_FlushesBeforeExportEnds()
    {
        var builder = new NwcDocumentBuilder();
        for (int i = 0; i < 2000; i++)
        {
            builder.AddTransform(new NwcTransform { Id = i });
        }

        var doc = builder.Build();
        using var counting = new CountingStream();

        doc.SaveJson(counting, new NwcJsonExportOptions { IncludeSceneTree = false });

        // Without incremental flushing the whole output arrives in one final write.
        Assert.True(counting.LargestWrite < counting.Length / 2, "The transforms section was buffered until the end of the export.");
        using var json = JsonDocument.Parse(counting.ToArray());
        Assert.Equal(2000, json.RootElement.GetProperty("transforms").GetArrayLength());
    }

    [Fact]
    public void SaveJson_CancelledDuringExport_StopsWritingBeforeTheEnd()
    {
        var builder = new NwcDocumentBuilder();
        for (int i = 0; i < 2000; i++)
        {
            builder.AddTransform(new NwcTransform { Id = i });
        }

        var doc = builder.Build();
        using var cancellation = new CancellationTokenSource();
        using var output = new CountingStream(cancellation.Cancel);

        Assert.Throws<OperationCanceledException>(() => doc.SaveJson(output, null, null, cancellation.Token));
        Assert.InRange(output.Length, 1, doc.ToJson().Length / 2);
    }

    [Fact]
    public async Task SaveJsonAsync_FileAndStream_WritesTheSameJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nwcio-test-{Guid.NewGuid():N}.json");
        try
        {
            var doc = NwcDocument.Create();
            using var output = new MemoryStream();
            await doc.SaveJsonAsync(path);
            await doc.SaveJsonAsync(output);
            Assert.Equal(File.ReadAllBytes(path), output.ToArray());
            using var json = JsonDocument.Parse(output.ToArray());
            Assert.Equal(0, json.RootElement.GetProperty("meshCount").GetInt32());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Json_WriteFile_RemovesPartialFileOnFailure()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nwcio-test-{Guid.NewGuid():N}.json");
        try
        {
            var doc = new NwcDocumentBuilder().Build();
            // Null options fail after the output file has been created.
            Assert.ThrowsAny<Exception>(() => NwcIO.Internal.Json.JsonExporter.WriteFile(doc, path, null!));
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class CountingStream(Action? afterWrite = null) : MemoryStream
    {
        public int LargestWrite { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            LargestWrite = Math.Max(LargestWrite, count);
            base.Write(buffer, offset, count);
            afterWrite?.Invoke();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            LargestWrite = Math.Max(LargestWrite, buffer.Length);
            base.Write(buffer);
            afterWrite?.Invoke();
        }
    }

    [Fact]
    public void Json_WritesNodeHyperlinks()
    {
        var node = new NwcNode
        {
            Id = 1,
            Name = "Wall_1",
            Class = "LcRevitData_Element",
        };
        var link = new NwcHyperlink
        {
            Name = "Detail Drawing",
            Url = "https://example.com/detail.dwg",
            Category = "Drawings",
        };
        link.AttachmentPoints.Add(new Vector3d(1.0, 2.0, 3.0));
        node.Hyperlinks.Add(link);

        var doc = new NwcDocumentBuilder()
            .SetRootNode(node)
            .Build();

        using var json = JsonDocument.Parse(doc.ToJson());
        var rootNode = json.RootElement.GetProperty("sceneTree");
        var hyperlinks = rootNode.GetProperty("hyperlinks");
        Assert.Equal(1, hyperlinks.GetArrayLength());

        var h0 = hyperlinks[0];
        Assert.Equal("Detail Drawing", h0.GetProperty("name").GetString());
        Assert.Equal("https://example.com/detail.dwg", h0.GetProperty("url").GetString());
        Assert.Equal("Drawings", h0.GetProperty("category").GetString());

        var pts = h0.GetProperty("attachmentPoints");
        Assert.Equal(1, pts.GetArrayLength());
        Assert.Equal(1.0, pts[0][0].GetDouble());
        Assert.Equal(2.0, pts[0][1].GetDouble());
        Assert.Equal(3.0, pts[0][2].GetDouble());
    }
}
