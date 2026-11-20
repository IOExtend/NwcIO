using System.Text;
using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Streams 3D meshes to Wavefront OBJ format (.obj), compatible with Blender, 3ds Max,
/// MeshLab, Windows 3D Viewer, and standard CAD/BIM visualizers.
/// </summary>
internal static class ObjExporter
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private const string DefaultMaterial = "nwc_default";

    /// <summary>
    /// Writes meshes to a file in Wavefront OBJ format.
    /// </summary>
    public static void WriteFile(
        IReadOnlyList<NwcMesh> meshes,
        string path,
        bool worldCoordinates = true,
        string? sourceDocument = null,
        Func<NwcMesh, string?>? objectNamePrefix = null) =>
        WriteFileCore(meshes, path, worldCoordinates, sourceDocument, objectNamePrefix, asynchronous: false, default)
            .GetSynchronousResult();

    public static Task WriteFileAsync(IReadOnlyList<NwcMesh> meshes, string path, bool worldCoordinates,
        string? sourceDocument, CancellationToken cancellationToken) =>
        WriteFileCore(meshes, path, worldCoordinates, sourceDocument, null, asynchronous: true, cancellationToken).AsTask();

    private static async ValueTask WriteFileCore(IReadOnlyList<NwcMesh> meshes, string path, bool worldCoordinates,
        string? sourceDocument, Func<NwcMesh, string?>? objectNamePrefix, bool asynchronous, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(path);
        cancellationToken.ThrowIfCancellationRequested();

        var materials = new Dictionary<(NwcMaterial Material, string? Map), string>();
        bool hasUnassigned = false;
        foreach (var mesh in meshes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mesh.Vertices.Count == 0 || !HasPrimitives(mesh)) continue;
            if (mesh.Material is { } material)
            {
                bool hasTexturedFaces = material.DiffuseTexture is not null && mesh.Indices.Count >= 3 &&
                    TextureExport.GetCoordinates(mesh, mesh.Vertices.Count, cancellationToken) is not null;
                var key = (material, GetMap(mesh, hasTexturedFaces));
                if (!materials.ContainsKey(key))
                {
                    materials.Add(key, FormattableString.Invariant($"nwc_{materials.Count + 1}"));
                }
            }
            else
            {
                hasUnassigned = true;
            }
        }

        string? materialFileName = null;
        (string, Func<Stream, ValueTask>)? companion = null;
        if (materials.Count != 0)
        {
            string materialPath = Path.ChangeExtension(Path.GetFullPath(path), ".mtl");
            materialFileName = Path.GetFileName(materialPath);
            if (materialFileName != SanitizeObjLine(materialFileName) || materialFileName.Contains('#') || materialFileName.Contains('\\'))
            {
                throw new ArgumentException("The output file name cannot be written as an OBJ material library reference.", nameof(path));
            }
            companion = (materialPath, output => WriteText(output,
                writer => WriteMaterials(writer, materials, hasUnassigned, cancellationToken)));
        }

        await AtomicFileWriter.WriteCore(path, output => WriteText(output, writer => WriteCore(meshes, writer,
                worldCoordinates, sourceDocument, objectNamePrefix, materialFileName, companion.HasValue ? materials : null, cancellationToken)),
            asynchronous, cancellationToken, companion).ConfigureAwait(false);

        ValueTask WriteText(Stream output, Action<TextWriter> write)
        {
            if (asynchronous)
            {
                return new ValueTask(CompatibilityExport.WriteAsync(output, stream => CompatibilityExport.WriteText(stream, Utf8, write),
                    cancellationToken));
            }
            CompatibilityExport.WriteText(output, Utf8, write);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Writes meshes to a <see cref="TextWriter"/> in Wavefront OBJ format.
    /// </summary>
    public static void Write(
        IReadOnlyList<NwcMesh> meshes,
        TextWriter writer,
        bool worldCoordinates = true,
        string? sourceDocument = null,
        Func<NwcMesh, string?>? objectNamePrefix = null,
        CancellationToken cancellationToken = default) =>
        WriteCore(meshes, writer, worldCoordinates, sourceDocument, objectNamePrefix, null, null, cancellationToken);

    private static void WriteCore(IReadOnlyList<NwcMesh> meshes, TextWriter writer, bool worldCoordinates,
        string? sourceDocument, Func<NwcMesh, string?>? objectNamePrefix,
        string? materialFileName, Dictionary<(NwcMaterial Material, string? Map), string>? materials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("# Wavefront OBJ exported by NwcIO");
        if (!string.IsNullOrEmpty(sourceDocument))
        {
            writer.WriteLine(FormattableString.Invariant($"# Source: {SanitizeObjLine(sourceDocument)}"));
        }

        writer.WriteLine(FormattableString.Invariant($"# Meshes: {meshes.Count}"));
        writer.WriteLine(FormattableString.Invariant($"# Coordinate Space: {(worldCoordinates ? "World" : "Local")}"));
        if (materialFileName is not null) writer.WriteLine($"mtllib {materialFileName}");
        writer.WriteLine();

        int baseVertexIndex = 1;
        int baseUvIndex = 1;

        for (int m = 0; m < meshes.Count; m++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mesh = meshes[m];
            var vertices = worldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
            if (vertices.Count == 0 || (mesh.Indices.Count == 0 && mesh.LineIndices.Count == 0))
            {
                continue;
            }

            string objectName = string.IsNullOrEmpty(mesh.Name) ? string.Empty : SanitizeObjLine(mesh.Name);
            if (objectName.Length == 0)
            {
                objectName = $"Mesh_{mesh.Id}";
            }

            string? prefix = objectNamePrefix?.Invoke(mesh);
            if (!string.IsNullOrEmpty(prefix))
            {
                objectName = $"{SanitizeObjLine(prefix)}/{objectName}";
            }

            writer.WriteLine(FormattableString.Invariant($"o {objectName}"));

            // Vertex positions
            for (int v = 0; v < vertices.Count; v++)
            {
                var pt = vertices[v];
                writer.WriteLine(FormattableString.Invariant($"v {pt.X:F4} {pt.Y:F4} {pt.Z:F4}"));
            }

            var uv = mesh.Indices.Count >= 3 ? TextureExport.GetCoordinates(mesh, vertices.Count, cancellationToken) : null;
            if (uv is not null)
            {
                foreach (var coordinate in uv)
                {
                    writer.WriteLine(FormattableString.Invariant($"vt {coordinate.X:R} {coordinate.Y:R}"));
                }
            }

            if (materials is not null && HasPrimitives(mesh))
            {
                string materialName = mesh.Material is { } material ? materials[(material, GetMap(mesh, uv is not null))] : DefaultMaterial;
                writer.WriteLine($"usemtl {materialName}");
            }

            // Triangle faces (1-based global indices)
            if (mesh.Indices.Count > 0)
            {
                var faces = worldCoordinates ? mesh.GetWorldIndices() : mesh.Indices;
                for (int i = 0; i <= faces.Count - 3; i += 3)
                {
                    int i0 = baseVertexIndex + faces[i];
                    int i1 = baseVertexIndex + faces[i + 1];
                    int i2 = baseVertexIndex + faces[i + 2];
                    if (uv is null)
                    {
                        writer.WriteLine(FormattableString.Invariant($"f {i0} {i1} {i2}"));
                    }
                    else
                    {
                        writer.WriteLine(FormattableString.Invariant($"f {i0}/{baseUvIndex + faces[i]} {i1}/{baseUvIndex + faces[i + 1]} {i2}/{baseUvIndex + faces[i + 2]}"));
                    }
                }
            }

            // Line segments (1-based global indices)
            if (mesh.LineIndices.Count > 0)
            {
                for (int i = 0; i <= mesh.LineIndices.Count - 2; i += 2)
                {
                    int i0 = baseVertexIndex + mesh.LineIndices[i];
                    int i1 = baseVertexIndex + mesh.LineIndices[i + 1];
                    writer.WriteLine(FormattableString.Invariant($"l {i0} {i1}"));
                }
            }

            baseVertexIndex += vertices.Count;
            baseUvIndex += uv?.Count ?? 0;
            writer.WriteLine();
        }
    }

    private static bool HasPrimitives(NwcMesh mesh) => mesh.Indices.Count >= 3 || mesh.LineIndices.Count >= 2;

    private static string? GetMap(NwcMesh mesh, bool hasUv)
    {
        if (!hasUv || mesh.Indices.Count < 3 || TextureExport.GetLocalPath(mesh.Material?.DiffuseTexture) is not { } path ||
            path.Contains('#')) return null;
        return path[0] == '-' ? "./" + path : path;
    }

    private static void WriteMaterials(TextWriter writer, Dictionary<(NwcMaterial Material, string? Map), string> materials, bool hasUnassigned,
        CancellationToken cancellationToken)
    {
        foreach (var (entry, name) in materials)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteMaterial(name, entry.Material.Color, entry.Map);
        }
        if (hasUnassigned) WriteMaterial(DefaultMaterial, RgbaColor.White, null);

        void WriteMaterial(string name, RgbaColor color, string? map)
        {
            writer.WriteLine($"newmtl {name}");
            writer.WriteLine(FormattableString.Invariant($"Kd {Component(color.R):R} {Component(color.G):R} {Component(color.B):R}"));
            writer.WriteLine(FormattableString.Invariant($"d {Component(color.A):R}"));
            if (map is not null) writer.WriteLine($"map_Kd {map}");
            writer.WriteLine();
        }

        static float Component(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    }

    /// <summary>OBJ directives are line based; never let a name from the file inject its own line.</summary>
    private static string SanitizeObjLine(string value)
    {
        var characters = value.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? '_' : c).ToArray();
        return new string(characters).Trim();
    }
}
