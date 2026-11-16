using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class NwcDiagnosticTests
{
    [Fact]
    public void Open_TrailingGap_AddsContainerTrailingGapDiagnostic()
    {
        byte[] file = NwcIO.NwcDocument.Create().ToByteArray();
        byte[] withTail = [.. file, 1, 2, 3, 4, 5, 6, 7];

        var document = NwcIO.NwcDocument.Open(new MemoryStream(withTail));

        var diagnostic = Assert.Single(document.Diagnostics, item => item.Code == "ContainerTrailingGap");
        Assert.Equal(NwcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(file.Length, diagnostic.ByteOffset);
        Assert.Contains("7", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_TruncatedLastStream_AddsContainerTrailingGapDiagnostic()
    {
        byte[] file = NwcIO.NwcDocument.Create().ToByteArray();
        byte[] truncated = file[..^2]; // cut inside the Adler-32 trailer of the last zlib stream

        var document = NwcIO.NwcDocument.Open(new MemoryStream(truncated));

        var diagnostic = Assert.Single(document.Diagnostics, item => item.Code == "ContainerTrailingGap");
        Assert.True(diagnostic.ByteOffset > 0 && diagnostic.ByteOffset < truncated.Length);
    }

    [Fact]
    public void Assembly_CarriesTheDiagnosticsOfItsDocuments()
    {
        byte[] file = NwcIO.NwcDocument.Create().ToByteArray();
        byte[] withTail = [.. file, 1, 2, 3];
        var document = NwcIO.NwcDocument.Open(new MemoryStream(withTail));

        var assembly = NwcAssembly.Create("Federation", document);

        Assert.Contains(assembly.Diagnostics, item => item.Code == "ContainerTrailingGap");
    }
}
