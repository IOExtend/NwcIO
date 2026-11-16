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

        Assert.True(counting.Writes > 1, "The transforms section was buffered until the end of the export.");
        using var json = JsonDocument.Parse(counting.ToArray());
        Assert.Equal(2000, json.RootElement.GetProperty("transforms").GetArrayLength());
    }

    private sealed class CountingStream : MemoryStream
    {
        public int Writes { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Writes++;
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Writes++;
            base.Write(buffer);
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
