using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class DiagnosticsExportTests
{
    [Fact]
    public void Open_MalformedPropertyRecord_ReportsPropertyScanStopped()
    {
        using var stream = new MemoryStream(MalformedSceneTreeFile());

        var document = NwcDocument.Open(stream);

        var diagnostic = Assert.Single(document.Diagnostics, d => d.Code == "property-scan-stopped");
        Assert.Equal(NwcDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("invalid length", diagnostic.Message);
    }

    [Fact]
    public void Open_StrictWithMalformedPropertyRecord_Throws()
    {
        using var stream = new MemoryStream(MalformedSceneTreeFile());

        Assert.Throws<InvalidDataException>(() =>
            NwcDocument.Open(stream, new NwcReaderOptions { Strict = true }));
    }

    [Fact]
    public void JsonExport_IncludesDocumentAndAssemblyDiagnostics()
    {
        using var stream = new MemoryStream(MalformedSceneTreeFile());
        var document = NwcDocument.Open(stream);

        string json = document.ToJson();
        Assert.Contains("\"diagnostics\"", json);
        Assert.Contains("property-scan-stopped", json);

        var assembly = NwcAssembly.Create("Fed", document);
        using var assemblyStream = new MemoryStream();
        assembly.SaveJson(assemblyStream);
        string assemblyJson = Encoding.UTF8.GetString(assemblyStream.ToArray());
        Assert.Contains("\"diagnostics\"", assemblyJson);
        Assert.Contains("property-scan-stopped", assemblyJson);
    }

    private static byte[] MalformedSceneTreeFile()
    {
        // The first two ints classify the stream as SceneTree; tag 53 at byte 12 has an invalid -1 length.
        var data = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 86);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 53);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), -1);
        return NwcContainerWriter.Write([data]);
    }
}
