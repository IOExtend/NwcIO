using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Extracts material definitions, RGBA colors, and surface parameters from property streams
/// and associates them with scene nodes and 3D meshes.
/// </summary>
public static class MaterialParser
{
    private static readonly NwcMaterial DefaultGeneric = new()
    {
        Id = 0,
        Name = "Default Material",
        Color = new RgbaColor(0.70f, 0.72f, 0.75f, 1.0f),
        Metallic = 0.10f,
        Roughness = 0.70f,
        IsInferred = true,
    };

    /// <summary>
    /// Parses materials from property records and assigns them to meshes and scene nodes.
    /// </summary>
    public static IReadOnlyList<NwcMaterial> Parse(
        IReadOnlyList<NwcPropertyRecord> records,
        IReadOnlyList<NwcMesh>? meshes = null,
        NwcNode? rootNode = null)
    {
        ArgumentNullException.ThrowIfNull(records);

        var materials = new Dictionary<string, NwcMaterial>(StringComparer.OrdinalIgnoreCase);
        var materialsById = new Dictionary<int, NwcMaterial>();

        string? currentMatName = null;
        int? currentMatId = null;

        for (int i = 0; i < records.Count; i++)
        {
            var p = records[i];

            // 1. Definition of material element: LcRevitElement Material "Name", #12345
            if (p.Class == "LcRevitElement" && p.Name.StartsWith("Material \"", StringComparison.Ordinal))
            {
                int quoteEnd = p.Name.IndexOf('"', 10);
                if (quoteEnd > 10)
                {
                    string name = p.Name[10..quoteEnd];
                    int hashIdx = p.Name.IndexOf('#', quoteEnd);
                    int? id = null;
                    if (hashIdx > 0 && int.TryParse(p.Name.AsSpan(hashIdx + 1), out int parsedId))
                    {
                        id = parsedId;
                    }

                    currentMatName = name;
                    currentMatId = id;

                    if (!materials.TryGetValue(name, out var mat))
                    {
                        mat = new NwcMaterial
                        {
                            Id = id ?? (materials.Count + 1),
                            Name = name,
                            Color = InferColorByName(name),
                        };
                        materials[name] = mat;
                        materialsById[mat.Id] = mat;
                    }
                }
            }
            // 2. Direct color property on material: revit_Color Color "255, 128, 0"
            else if (p.Class == "revit_Color" && p.Name == "Color" && currentMatName is not null)
            {
                if (materials.TryGetValue(currentMatName, out var mat) && RgbaColor.TryParse(p.Value, out var col))
                {
                    mat.Color = col;
                }
            }
            // 3. Property referencing a material: "Материал" = Material "Name", #12345
            else if (p.Value is not null && p.Value.StartsWith("Material \"", StringComparison.Ordinal))
            {
                int quoteEnd = p.Value.IndexOf('"', 10);
                if (quoteEnd > 10)
                {
                    string name = p.Value[10..quoteEnd];
                    if (!materials.ContainsKey(name))
                    {
                        int hashIdx = p.Value.IndexOf('#', quoteEnd);
                        int? id = null;
                        if (hashIdx > 0 && int.TryParse(p.Value.AsSpan(hashIdx + 1), out int parsedId))
                        {
                            id = parsedId;
                        }

                        var mat = new NwcMaterial
                        {
                            Id = id ?? (materials.Count + 1),
                            Name = name,
                            Color = InferColorByName(name),
                        };
                        materials[name] = mat;
                        materialsById[mat.Id] = mat;
                    }
                }
            }
        }

        var resultList = materials.Values.ToList();

        // Assign materials to meshes
        if (meshes is not null && meshes.Count > 0)
        {
            AssignMaterialsToMeshes(meshes, resultList);
        }

        // Assign materials to scene tree nodes
        if (rootNode is not null)
        {
            AssignMaterialsToNodesRecursive(rootNode, materials, materialsById);
        }

        return resultList;
    }

    private static void AssignMaterialsToMeshes(IReadOnlyList<NwcMesh> meshes, List<NwcMaterial> materials)
    {
        // The parser has no per-geometry appearance data yet, so every mesh
        // without a material gets one neutral fallback that is marked inferred.
        for (int i = 0; i < meshes.Count; i++)
        {
            if (meshes[i].Material is null)
            {
                meshes[i].Material = DefaultGeneric;
            }
        }
    }

    private static void AssignMaterialsToNodesRecursive(
        NwcNode node,
        Dictionary<string, NwcMaterial> materials,
        Dictionary<int, NwcMaterial> materialsById)
    {
        foreach (var prop in node.Properties)
        {
            if (prop.Value is not null && prop.Value.StartsWith("Material \"", StringComparison.Ordinal))
            {
                int quoteEnd = prop.Value.IndexOf('"', 10);
                if (quoteEnd > 10)
                {
                    string name = prop.Value[10..quoteEnd];
                    if (materials.TryGetValue(name, out var mat))
                    {
                        node.Material = mat;
                        break;
                    }
                }
            }
        }

        foreach (var child in node.Children)
        {
            AssignMaterialsToNodesRecursive(child, materials, materialsById);
        }
    }

    private static RgbaColor InferColorByName(string name)
    {
        if (name.Contains("арматур", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("сталь", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("steel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rebar", StringComparison.OrdinalIgnoreCase))
        {
            return RgbaColor.Rebar;
        }

        if (name.Contains("бетон", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("concrete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("железобетон", StringComparison.OrdinalIgnoreCase))
        {
            return RgbaColor.Concrete;
        }

        if (name.Contains("утеплител", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("полистирол", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("insulation", StringComparison.OrdinalIgnoreCase))
        {
            return RgbaColor.Insulation;
        }

        if (name.Contains("стекл", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("glass", StringComparison.OrdinalIgnoreCase))
        {
            return RgbaColor.Glass;
        }

        return RgbaColor.Gray;
    }
}
