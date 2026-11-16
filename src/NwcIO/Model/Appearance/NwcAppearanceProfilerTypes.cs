using System.Globalization;
using NwcIO.Model.Sets;

namespace NwcIO.Model.Appearance;

/// <summary>
/// A single rule within the Navisworks Appearance Profiler that overrides element color
/// and transparency based on search criteria.
/// </summary>
public sealed class NwcAppearanceRule
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public NwcSearchCondition Condition { get; set; } = new();
    public RgbaColor Color { get; set; } = RgbaColor.White;
    public float Transparency { get; set; } // 0.0 = completely opaque, 1.0 = completely transparent

    public NwcAppearanceRule() { }

    public NwcAppearanceRule(string name, NwcSearchCondition condition, RgbaColor color, float transparency = 0.0f)
    {
        Name = name;
        Condition = condition;
        Color = color;
        Transparency = transparency;
    }

    public bool Matches(NwcNode node) => Enabled && Condition.Evaluate(node);
}

/// <summary>
/// Manages a collection of appearance rules to systematically colorize and adjust
/// transparencies across a Navisworks scene graph (e.g. coloring HVAC, piping, structure).
/// </summary>
public sealed class NwcAppearanceProfiler
{
    public List<NwcAppearanceRule> Rules { get; } = [];

    public void AddRule(
        string name,
        string category,
        string property,
        NwcSearchOperator op,
        string value,
        RgbaColor color,
        float transparency = 0.0f)
    {
        Rules.Add(new NwcAppearanceRule(
            name,
            new NwcSearchCondition(category, property, op, value),
            color,
            transparency));
    }

    public void AddRule(string name, NwcSearchCondition condition, RgbaColor color, float transparency = 0.0f)
    {
        Rules.Add(new NwcAppearanceRule(name, condition, color, transparency));
    }

    /// <summary>
    /// Applies the appearance rules across the document's scene tree and geometry meshes.
    /// </summary>
    /// <returns>The total number of elements whose appearance was overridden.</returns>
    public int Apply(NwcDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int matchedCount = 0;

        // One material per rule, reused by every matched element instead of allocating one per node.
        var ruleMaterials = new NwcMaterial[Rules.Count];
        for (int i = 0; i < Rules.Count; i++)
        {
            var rule = Rules[i];
            float alpha = Math.Clamp(1.0f - rule.Transparency, 0.0f, 1.0f);
            ruleMaterials[i] = new NwcMaterial
            {
                Id = document.Materials.Count + 1 + i,
                Name = $"Profiler_{rule.Name}",
                Color = new RgbaColor(rule.Color.R, rule.Color.G, rule.Color.B, alpha),
            };
        }

        void Traverse(NwcNode node)
        {
            for (int ruleIndex = 0; ruleIndex < Rules.Count; ruleIndex++)
            {
                if (Rules[ruleIndex].Matches(node))
                {
                    var mat = ruleMaterials[ruleIndex];

                    node.Material = mat;
                    foreach (var nodeMesh in node.Meshes)
                    {
                        nodeMesh.Material = mat;
                    }

                    if (node.Mesh is not null)
                    {
                        node.Mesh.Material = mat;
                    }

                    matchedCount++;
                    break;
                }
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                Traverse(node.Children[i]);
            }
        }

        if (document.RootNode is not null)
        {
            Traverse(document.RootNode);
        }
        else
        {
            for (int i = 0; i < document.Meshes.Count; i++)
            {
                var mesh = document.Meshes[i];
                var pseudoNode = new NwcNode
                {
                    Id = mesh.Id,
                    Name = mesh.Name,
                    Class = "Geometry",
                    Mesh = mesh,
                };

                foreach (var rule in Rules)
                {
                    if (rule.Matches(pseudoNode))
                    {
                        mesh.Material = ruleMaterials[Rules.IndexOf(rule)];
                        matchedCount++;
                        break;
                    }
                }
            }
        }

        return matchedCount;
    }
}
