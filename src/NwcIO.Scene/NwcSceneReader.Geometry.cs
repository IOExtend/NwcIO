using IOScene;
using NwcIO.Model;
using Vector3d = IOScene.Vector3d;
using RgbaColor = IOScene.RgbaColor;

namespace NwcIO.Scene;

public static partial class NwcSceneReader
{
    // Writes the geometries, materials and instances of the document's meshes in their order, each made at first use; returns
    // the element of every placed mesh (its node's Element, or the node itself outside elements).
    private static HashSet<NwcNode> AddGeometry(NwcDocument document, SceneDocument scene, double metresPerUnit, List<SceneDiagnostic> diagnostics)
    {
        var placed = new HashSet<NwcNode>();
        var geometries = new Dictionary<object, List<(int Id, RgbaColor? Color)>>();   // by GeometryIndex, else by the mesh
        var materials = new Dictionary<NwcMaterial, int>(ReferenceEqualityComparer.Instance);
        int unplaced = 0;
        foreach (var mesh in document.Meshes)
        {
            if (mesh.Node is not { } node)
            {
                unplaced++;
                continue;
            }

            object key = mesh.GeometryIndex >= 0 ? mesh.GeometryIndex : mesh;
            if (!geometries.TryGetValue(key, out var groups))
            {
                groups = AddGeometries(scene, mesh, key, metresPerUnit, diagnostics);
                geometries.Add(key, groups);
            }

            if (groups.Count == 0)
            {
                diagnostics.Add(new SceneDiagnostic(SceneSeverity.Info, "empty-mesh",
                    $"Mesh {mesh.Id} has no triangles and no line segments; it has no geometry in the scene.", node.Element?.ElementGuid?.ToString("D")));
                continue;
            }

            int? materialId = null;
            if (mesh.Material is { } material)
            {
                if (!materials.TryGetValue(material, out int id))
                {
                    id = scene.Materials.Count + 1;
                    var c = material.Color;
                    scene.Materials.Add(new SceneMaterial { Id = id, Name = material.Name, Color = new(c.RByte, c.GByte, c.BByte, c.AByte) });
                    materials.Add(material, id);
                }

                materialId = id;
            }

            Transform3d? transform = null;
            if (mesh.Transform is { } t)
            {
                double[] m = t.ToMatrix4x4();   // row-major, rows are the images of X, Y, Z, translation in row 3
                transform = new Transform3d(new(m[0], m[1], m[2]), new(m[4], m[5], m[6]), new(m[8], m[9], m[10]), new Vector3d(m[12], m[13], m[14]) * metresPerUnit);
            }

            foreach (var (geometryId, colour) in groups)
            {
                scene.Instances.Add(new SceneInstance { ElementId = node.Id, GeometryId = geometryId, Transform = transform, MaterialId = materialId, Color = colour });
            }

            placed.Add(node.Element ?? node);
        }

        if (unplaced > 0)
        {
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "unplaced-mesh",
                $"{unplaced} meshes have no tree node (the file was not read through its DbCache directory); they are not in the scene."));
        }

        return placed;
    }

    // The geometries of one geometry key with the colour of their instances: the triangles, then one line geometry per segment colour.
    private static List<(int Id, RgbaColor? Color)> AddGeometries(SceneDocument scene, NwcMesh mesh, object key, double metresPerUnit, List<SceneDiagnostic> diagnostics)
    {
        var result = new List<(int Id, RgbaColor? Color)>();
        Vector3d[] vertices = [.. mesh.Vertices.Select(v => new Vector3d(v.X * metresPerUnit, v.Y * metresPerUnit, v.Z * metresPerUnit))];
        if (mesh.Indices.Count > 0)
        {
            int id = scene.Geometries.Count + 1;
            scene.Geometries.Add(new SceneGeometry { Id = id, Topology = SceneTopology.Triangles, Vertices = vertices, Indices = mesh.Indices, Normals = Normals(mesh) });
            result.Add((id, null));
        }

        // Segments go to the group of their colour when both ends share it; a segment whose ends differ goes to the null group,
        // which is drawn with the material colour and reported once per geometry key as line-colour-mixed.
        var groups = new List<(NwcIO.Model.RgbaColor? Color, List<int> Indices)>();
        for (int s = 0; s < mesh.LineIndices.Count; s += 2)
        {
            int a = mesh.LineIndices[s], b = mesh.LineIndices[s + 1];
            NwcIO.Model.RgbaColor? colour = mesh.LineColors is { } c && c[a] == c[b] ? c[a] : null;
            int g = groups.FindIndex(x => x.Color == colour);
            if (g < 0) { groups.Add((colour, [])); g = groups.Count - 1; }
            groups[g].Indices.Add(a); groups[g].Indices.Add(b);
        }

        foreach (var (colour, indices) in groups)
        {
            int id = scene.Geometries.Count + 1;
            scene.Geometries.Add(new SceneGeometry { Id = id, Topology = SceneTopology.Lines, Vertices = vertices, Indices = indices });
            result.Add((id, colour is { } c ? new RgbaColor(c.RByte, c.GByte, c.BByte, c.AByte) : null));
            if (colour is null && mesh.LineColors is not null)
            {
                diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "line-colour-mixed",
                    $"Geometry record {key}: {indices.Count / 2} line segments join vertices of different colours; they are drawn with the material colour."));
            }
        }

        return result;
    }

    // The stored normals with each (0,0,0) replaced by the area-weighted average of the incident triangles' normals; null without stored normals.
    private static Vector3d[]? Normals(NwcMesh mesh)
    {
        if (mesh.Normals is not { } stored)
        {
            return null;
        }

        Vector3d[] normals = [.. stored.Select(n => new Vector3d(n.X, n.Y, n.Z))];
        if (!normals.Contains(Vector3d.Zero))
        {
            return normals;
        }

        var sums = new Vector3d[normals.Length];   // a cross product's length is twice the triangle's area
        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
            var p = mesh.Vertices[a];
            var n = Vector3d.Cross(
                new Vector3d(mesh.Vertices[b].X - p.X, mesh.Vertices[b].Y - p.Y, mesh.Vertices[b].Z - p.Z),
                new Vector3d(mesh.Vertices[c].X - p.X, mesh.Vertices[c].Y - p.Y, mesh.Vertices[c].Z - p.Z));
            sums[a] += n;
            sums[b] += n;
            sums[c] += n;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i] == Vector3d.Zero && sums[i].Length is > 0 and var length)
            {
                normals[i] = sums[i] / length;
            }
        }

        return normals;
    }
}
