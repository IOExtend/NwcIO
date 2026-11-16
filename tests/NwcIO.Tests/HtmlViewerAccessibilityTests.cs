using NwcIO.Model;
using NwcIO.Model.Viewer;
using Xunit;

namespace NwcIO.Tests;

public sealed class HtmlViewerAccessibilityTests
{
    [Fact]
    public void Write_ContainsAccessibleMarkupAndKeyboardHandlers()
    {
        using var writer = new StringWriter();
        NwcHtmlViewerExporter.Write(writer, [], [], [], BoundingBox3d.Empty, new NwcHtmlViewerOptions());
        string html = writer.ToString();

        Assert.Contains("id=\"glcanvas\" tabindex=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"3D model view\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Clear measurement\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"TimeLiner date\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"TimeLiner playback speed\"", html, StringComparison.Ordinal);
        Assert.Contains("item.tabIndex = 0", html, StringComparison.Ordinal);
        Assert.Contains("case 'ArrowLeft'", html, StringComparison.Ordinal);
        Assert.Contains("e.key === 'Enter' || e.key === ' '", html, StringComparison.Ordinal);
    }
}
