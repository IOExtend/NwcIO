using System.Globalization;
using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Streams 3D meshes to Wavefront OBJ format (.obj), compatible with Blender, 3ds Max,
/// MeshLab, Windows 3D Viewer, and standard CAD/BIM visualizers.
/// </summary>
public static class ObjExporter
{
    /// <summary>
    /// Writes meshes to a file in Wavefront OBJ format.
    /// </summary>
    public static void WriteFile(
        IReadOnlyList<NwcMesh> meshes,
        string path,
        bool worldCoordinates = true,
        string? sourceDocument = null,
        Func<NwcMesh, string?>? objectNamePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(path);

        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 65536);
        Write(meshes, writer, worldCoordinates, sourceDocument, objectNamePrefix);
    }

    /// <summary>
    /// Writes meshes to a <see cref="TextWriter"/> in Wavefront OBJ format.
    /// </summary>
    public static void Write(
        IReadOnlyList<NwcMesh> meshes,
        TextWriter writer,
        bool worldCoordinates = true,
        string? sourceDocument = null,
        Func<NwcMesh, string?>? objectNamePrefix = null)
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
        writer.WriteLine();

        int baseVertexIndex = 1;

        for (int m = 0; m < meshes.Count; m++)
        {
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

            // Triangle faces (1-based global indices)
            if (mesh.Indices.Count > 0)
            {
                var faces = worldCoordinates ? mesh.GetWorldIndices() : mesh.Indices;
                for (int i = 0; i <= faces.Count - 3; i += 3)
                {
                    int i0 = baseVertexIndex + faces[i];
                    int i1 = baseVertexIndex + faces[i + 1];
                    int i2 = baseVertexIndex + faces[i + 2];
                    writer.WriteLine(FormattableString.Invariant($"f {i0} {i1} {i2}"));
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
            writer.WriteLine();
        }
    }

    /// <summary>OBJ directives are line based; never let a name from the file inject its own line.</summary>
    private static string SanitizeObjLine(string value)
    {
        var characters = value.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? '_' : c).ToArray();
        return new string(characters).Trim();
    }
}
