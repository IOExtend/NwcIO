using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public sealed class DiagnosticsExportTests
{
    [Fact]
    public void Open_MalformedPropertyRecord_ReportsPropertyScanFailedWarning()
    {
        using var stream = new MemoryStream(MalformedSceneTreeFile());

        var document = NwcDocument.Open(stream);

        var diagnostic = Assert.Single(document.Diagnostics, d => d.Code == "property-scan-failed");
        Assert.Equal(NwcDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("invalid length", diagnostic.Message);
        Assert.Equal(0, diagnostic.StreamIndex);
        Assert.Equal(16L, diagnostic.ByteOffset);
    }

    [Fact]
    public void Open_StrictWithMalformedPropertyRecord_DoesNotThrow()
    {
        using var stream = new MemoryStream(MalformedSceneTreeFile());

        var document = NwcDocument.Open(stream, new NwcReaderOptions { Strict = true });

        Assert.DoesNotContain(document.Diagnostics, d => d.Severity == NwcDiagnosticSeverity.Error);
    }

    [Fact]
    public void JsonExport_IncludesDocumentAndAssemblyDiagnostics()
    {
        using var stream = new MemoryStream(MalformedSceneTreeFile());
        var document = NwcDocument.Open(stream);

        string json = document.ToJson();
        Assert.Contains("\"diagnostics\"", json);
        Assert.Contains("property-scan-failed", json);

        var assembly = NwcAssembly.Create("Fed", document);
        using var assemblyStream = new MemoryStream();
        assembly.SaveJson(assemblyStream);
        string assemblyJson = Encoding.UTF8.GetString(assemblyStream.ToArray());
        Assert.Contains("\"diagnostics\"", assemblyJson);
        Assert.Contains("property-scan-failed", assemblyJson);
    }

    [Fact]
    public void Open_NullPropertyString_IsValidAndStrictDoesNotThrow()
    {
        var data = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 86);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 53);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), -1);
        byte[] file = NwcContainerWriter.Write([data]);

        var document = NwcDocument.Open(new MemoryStream(file), new NwcReaderOptions { Strict = true });

        Assert.DoesNotContain(document.Diagnostics, d => d.Code == "property-scan-failed");
    }

    [Fact]
    public void Scan_NullValueString_KeepsRecordAndAdvancesPastPrefix()
    {
        // tag 52 record: name "a", class "b", valueType 4 (string) with a null (-1) value, then tag 53 node "n".
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(1); w.Write(52);
            w.Write(1); w.Write(new byte[] { (byte)'a', 0, 0, 0 });
            w.Write(1); w.Write(new byte[] { (byte)'b', 0, 0, 0 });
            w.Write(4); w.Write(-1);
            w.Write(2); w.Write(53);
            w.Write(1); w.Write(new byte[] { (byte)'n', 0, 0, 0 });
        }

        var scanner = new NwcIO.Internal.Parsing.PropertyRecordScanner(ms.ToArray());
        var records = scanner.Scan();

        Assert.Equal(2, records.Count);
        Assert.Null(records[0].Value);
        Assert.Equal("n", records[1].Name);
        Assert.Null(scanner.FailureReason);
    }

    private static byte[] MalformedSceneTreeFile()
    {
        // The first two ints classify the stream as SceneTree; tag 53 at byte 12 has an invalid -1 length.
        var data = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 100);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 86);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 53);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16), -2);
        return NwcContainerWriter.Write([data]);
    }
}

