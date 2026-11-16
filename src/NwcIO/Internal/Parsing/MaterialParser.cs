using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Extracts material definitions and RGBA colors from property records for files read without
/// the DbCache directory. A colour comes only from a <c>revit_Color</c> record; a material without
/// one gets a neutral colour and <see cref="NwcMaterial.IsInferred"/>. Meshes without a material
/// get one neutral inferred fallback.
/// </summary>
internal static class MaterialParser
{
    private static readonly RgbaColor NeutralColor = new(0.70f, 0.72f, 0.75f, 1.0f);

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
                            Color = NeutralColor,
                            IsInferred = true,
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
                    mat.IsInferred = false;
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
                            Color = NeutralColor,
                            IsInferred = true,
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
            AssignDefaultMaterial(meshes, resultList);
        }

        // Assign materials to scene tree nodes
        if (rootNode is not null)
        {
            AssignMaterialsToNodesRecursive(rootNode, materials, materialsById);
        }

        return resultList;
    }

    private static void AssignDefaultMaterial(IReadOnlyList<NwcMesh> meshes, List<NwcMaterial> materials)
    {
        // The parser has no per-geometry appearance data, so every mesh without a
        // material shares one neutral fallback marked inferred. It is listed with the
        // document materials so a mesh never points at a material missing from the list.
        NwcMaterial? fallback = null;
        foreach (var mesh in meshes)
        {
            if (mesh.Material is not null)
            {
                continue;
            }

            if (fallback is null)
            {
                fallback = new NwcMaterial
                {
                    Id = materials.Count == 0 ? 0 : materials.Max(m => m.Id) + 1,
                    Name = "Default Material",
                    Color = NeutralColor,
                    Metallic = 0.10f,
                    Roughness = 0.70f,
                    IsInferred = true,
                };
                materials.Add(fallback);
            }

            mesh.Material = fallback;
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
}
