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
}
