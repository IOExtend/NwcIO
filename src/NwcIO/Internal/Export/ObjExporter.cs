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
        bool writeMaterials = true)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(path);

        string? materialLibrary = null;
        if (writeMaterials)
        {
            string mtlPath = Path.ChangeExtension(path, ".mtl");
            WriteMaterialsFile(meshes, mtlPath);
            materialLibrary = Path.GetFileName(mtlPath);
        }

        using var fs = File.Create(path);
        using var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 65536);
        Write(meshes, writer, worldCoordinates, sourceDocument, materialLibrary);
    }

    /// <summary>
    /// Writes meshes to a <see cref="TextWriter"/> in Wavefront OBJ format.
    /// </summary>
    public static void Write(
        IReadOnlyList<NwcMesh> meshes,
        TextWriter writer,
        bool worldCoordinates = true,
        string? sourceDocument = null,
        string? materialLibrary = null)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("# Wavefront OBJ exported by NwcIO");
        if (!string.IsNullOrEmpty(sourceDocument))
        {
            writer.WriteLine(FormattableString.Invariant($"# Source: {sourceDocument}"));
        }

        writer.WriteLine(FormattableString.Invariant($"# Meshes: {meshes.Count}"));
        writer.WriteLine(FormattableString.Invariant($"# Coordinate Space: {(worldCoordinates ? "World" : "Local")}"));
        if (!string.IsNullOrEmpty(materialLibrary))
        {
            writer.WriteLine(FormattableString.Invariant($"mtllib {materialLibrary}"));
        }
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

            string objectName = string.IsNullOrEmpty(mesh.Name) ? $"Mesh_{mesh.Id}" : mesh.Name;
            writer.WriteLine(FormattableString.Invariant($"o {objectName}"));
            if (!string.IsNullOrEmpty(materialLibrary) && mesh.Material is not null)
            {
                writer.WriteLine(FormattableString.Invariant($"usemtl {GetMaterialName(mesh.Material)}"));
            }

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

    private static void WriteMaterialsFile(IReadOnlyList<NwcMesh> meshes, string path)
    {
        using var fs = File.Create(path);
        using var writer = new StreamWriter(
            fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine("# Wavefront MTL exported by NwcIO");

        var seen = new HashSet<NwcMaterial>();
        foreach (var mesh in meshes)
        {
            var material = mesh.Material;
            if (material is null || !seen.Add(material))
            {
                continue;
            }

            writer.WriteLine();
            writer.WriteLine(FormattableString.Invariant($"newmtl {GetMaterialName(material)}"));
            writer.WriteLine(FormattableString.Invariant(
                $"Kd {material.Color.R / 255.0:F6} {material.Color.G / 255.0:F6} {material.Color.B / 255.0:F6}"));
            writer.WriteLine(FormattableString.Invariant($"d {material.Color.A / 255.0:F6}"));
        }
    }

    private static string GetMaterialName(NwcMaterial material)
    {
        string name = string.IsNullOrWhiteSpace(material.Name) ? $"Material_{material.Id}" : material.Name;
        var sb = new System.Text.StringBuilder(name.Length + 8);
        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        }
        sb.Append('_').Append(material.Id);
        return sb.ToString();
    }
}
