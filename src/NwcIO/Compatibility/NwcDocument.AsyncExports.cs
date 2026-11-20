using System.Text;
using NwcIO.Internal.Export;
using NwcIO.Model;
using NwcIO.Model.Sectioning;
using NwcIO.Model.Viewer;

namespace NwcIO;

public sealed partial class NwcDocument
{
    private static readonly Encoding ExportUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Exports Wavefront OBJ and a sibling material library when needed, asynchronously.</summary>
    public Task SaveObjAsync(string path, bool worldCoordinates = true, CancellationToken cancellationToken = default) =>
        ObjExporter.WriteFileAsync(Meshes, path, worldCoordinates, FileInfo.SourcePath, cancellationToken);

    /// <summary>Exports Wavefront OBJ to a UTF-8 stream asynchronously.</summary>
    public Task SaveObjAsync(Stream stream, bool worldCoordinates = true, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(stream, output => CompatibilityExport.WriteText(output, ExportUtf8,
            writer => ObjExporter.Write(Meshes, writer, worldCoordinates, FileInfo.SourcePath, cancellationToken: cancellationToken)),
            cancellationToken);

    /// <summary>Exports Wavefront OBJ to a text writer asynchronously.</summary>
    public Task SaveObjAsync(TextWriter writer, bool worldCoordinates = true, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(writer,
            output => ObjExporter.Write(Meshes, output, worldCoordinates, FileInfo.SourcePath, cancellationToken: cancellationToken),
            cancellationToken);

    /// <summary>Exports binary glTF to a file asynchronously.</summary>
    public Task SaveGlbAsync(string path, bool worldCoordinates = true, CancellationToken cancellationToken = default) =>
        SaveGlbAsync(path, NwcGlbExportOptions.ForCoordinates(worldCoordinates), cancellationToken);

    /// <summary>Exports binary glTF to a stream asynchronously.</summary>
    public Task SaveGlbAsync(Stream stream, bool worldCoordinates = true, CancellationToken cancellationToken = default) =>
        SaveGlbAsync(stream, NwcGlbExportOptions.ForCoordinates(worldCoordinates), cancellationToken);

    /// <summary>Exports binary glTF to a file asynchronously using the given options.</summary>
    public Task SaveGlbAsync(string path, NwcGlbExportOptions options, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteFileAsync(path, output => GlbExporter.Write(Meshes, Materials, output, options,
            viewpoints: SavedViewpoints, cancellationToken: cancellationToken), cancellationToken);

    /// <summary>Exports binary glTF to a stream asynchronously using the given options.</summary>
    public Task SaveGlbAsync(Stream stream, NwcGlbExportOptions options, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(stream, output => GlbExporter.Write(Meshes, Materials, output, options,
            viewpoints: SavedViewpoints, cancellationToken: cancellationToken), cancellationToken);

    /// <summary>Exports an IFC STEP file asynchronously.</summary>
    public Task SaveIfcAsync(string path, NwcIfcExportOptions? options = null, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteFileAsync(path, stream => CompatibilityExport.WriteText(stream, ExportUtf8,
            writer => IfcExporter.Write(this, writer, options, Path.GetFileName(path), cancellationToken)), cancellationToken);

    /// <summary>Exports IFC STEP to a UTF-8 stream asynchronously.</summary>
    public Task SaveIfcAsync(Stream stream, NwcIfcExportOptions? options = null, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(stream, output => CompatibilityExport.WriteText(output, ExportUtf8,
            writer => IfcExporter.Write(this, writer, options, cancellationToken: cancellationToken)), cancellationToken);

    /// <summary>Exports IFC STEP to a text writer asynchronously.</summary>
    public Task SaveIfcAsync(TextWriter writer, NwcIfcExportOptions? options = null, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(writer,
            output => IfcExporter.Write(this, output, options, cancellationToken: cancellationToken), cancellationToken);

    /// <summary>Exports saved viewpoints to a BCF 2.1 file asynchronously.</summary>
    public async Task SaveViewpointsBcfAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await CompatibilityExport.WriteFileAsync(path, output => WriteViewpointsBcf(output, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Exports saved viewpoints to a BCF 2.1 stream asynchronously.</summary>
    public Task SaveViewpointsBcfAsync(Stream stream, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(stream, output => WriteViewpointsBcf(output, cancellationToken), cancellationToken);

    private void WriteViewpointsBcf(Stream output, CancellationToken cancellationToken) =>
        BcfExporter.WriteViewpoints(SavedViewpoints, output, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"),
            MetresPerUnit ?? 1.0, cancellationToken);

    /// <summary>Exports saved viewpoints to a Navisworks XML file asynchronously.</summary>
    public Task SaveViewpointsXmlAsync(string path, string? units = null, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteFileAsync(path, output => WriteViewpointsXml(output, units, cancellationToken), cancellationToken);

    /// <summary>Exports saved viewpoints to a Navisworks XML stream asynchronously.</summary>
    public Task SaveViewpointsXmlAsync(Stream stream, string? units = null, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(stream, output => WriteViewpointsXml(output, units, cancellationToken), cancellationToken);

    private void WriteViewpointsXml(Stream output, string? units, CancellationToken cancellationToken) =>
        CompatibilityExport.WriteText(output, Encoding.UTF8,
            writer => writer.Write(NwcXmlViewpointSerializer.Serialize(SavedViewpoints, units ?? LengthUnit.GetSymbol() ?? "m", cancellationToken)));

    /// <summary>Exports a standalone HTML viewer to a file asynchronously.</summary>
    public Task SaveHtmlViewerAsync(string path, NwcHtmlViewerOptions? options = null, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteFileAsync(path, output => CompatibilityExport.WriteText(output, Encoding.UTF8,
            writer => NwcHtmlViewerExporter.WriteDocument(writer, this, options, Path.GetFileNameWithoutExtension(path), cancellationToken)),
            cancellationToken);

    /// <summary>Exports a standalone HTML viewer to a stream asynchronously.</summary>
    public Task SaveHtmlViewerAsync(Stream stream, NwcHtmlViewerOptions? options = null, CancellationToken cancellationToken = default) =>
        CompatibilityExport.WriteAsync(stream, output => CompatibilityExport.WriteText(output, Encoding.UTF8,
            writer => NwcHtmlViewerExporter.WriteDocument(writer, this, options, Path.GetFileNameWithoutExtension(FileInfo.SourcePath ?? "Model"),
                cancellationToken)), cancellationToken);
}
