using System.Globalization;
using System.Text.RegularExpressions;
using IOScene;
using NwcIO.Model;

namespace NwcIO.Scene;

public static partial class NwcSceneReader
{
    private static readonly HashSet<string> KeyedTabs = new(StringComparer.Ordinal)
    {
        "LcRevitData_Element", "LcRevitData_Type", "LcRevitMaterialProperties", "LcRevitId",
    };

    private static readonly HashSet<string> KeyedProperties = new(StringComparer.Ordinal)
    {
        "LcRevitPropertyElementCategory", "LcRevitPropertyElementCategoryId", "LcRevitPropertyElementFamily", "LcRevitPropertyElementId",
        "LcRevitPropertyElementName", "LcRevitPropertyElementType", "LcRevitId",
    };

    // The ids of the sets of the node's tabs in slot order, each once; a tab's set is made at its first use, and a tab whose
    // properties are all left out has none (null in tabSets).
    private static List<int> PropertySetIds(NwcNode node, SceneDocument scene, Dictionary<NwcPropertyTab, int?> tabSets, bool feet)
    {
        var ids = new List<int>();
        foreach (var tab in node.PropertyTabs)
        {
            if (!tabSets.TryGetValue(tab, out int? id))
            {
                id = AddPropertySet(scene, tab, feet);
                tabSets.Add(tab, id);
            }

            if (id is int set && !ids.Contains(set))
            {
                ids.Add(set);
            }
        }

        return ids;
    }

    private static int? AddPropertySet(SceneDocument scene, NwcPropertyTab tab, bool feet)
    {
        string? setKey = tab.InternalName is { } tabName && KeyedTabs.Contains(tabName) ? "nwc:" + tabName : null;
        var properties = new List<SceneProperty>();
        foreach (var record in tab.Properties)
        {
            if (ValueText(record, feet) is not { } text)
            {
                continue;
            }

            // Only these names have one display name and one value type each; the folded lcldrevit_parameter_<text> names are lossy and repeat.
            string? key = setKey is not null && record.InternalName is { } name
                && (KeyedProperties.Contains(name) || StablePropertyName().IsMatch(name)) ? setKey + "." + name : null;
            properties.Add(new SceneProperty { Name = record.Name, Key = key, Value = SceneValue.FromText(text) });
        }

        if (properties.Count == 0)
        {
            return null;
        }

        int id = scene.PropertySets.Count + 1;
        scene.PropertySets.Add(new ScenePropertySet { Id = id, Name = tab.Name, Key = setKey, Properties = properties });
        return id;
    }

    // RvtIO.Scene's display text: the invariant culture, six significant digits and its exact factors from Revit's internal units
    // (foot, square foot, cubic foot, radian); null for a value that cannot be shown, or a dimensioned one in an unknown unit.
    private static string? ValueText(NwcPropertyRecord record, bool feet) => record.Value is not { } value ? null : record.ValueType switch
    {
        2 or 4 or 8 or 9 => value,
        3 => value == "0" ? "0" : "1",
        1 => Number(value, 1, ""),
        6 when feet => Number(value, 304.8, " mm"),
        10 when feet => Number(value, 0.09290304, " m²"),
        11 when feet => Number(value, 0.0283168466, " m³"),
        7 when feet => Number(value, 180.0 / Math.PI, " °"),
        _ => null,
    };

    // Text that is not an invariant number is left out like a non-finite one (design §6.3: never guessed, never a crash).
    private static string? Number(string stored, double factor, string unit) =>
        double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value * factor)
            ? string.Create(CultureInfo.InvariantCulture, $"{value * factor:G6}{unit}")
            : null;

    // \z, not $, which also matches before a final newline.
    [GeneratedRegex(@"^(lcldrevit_parameter_-[0-9]+|revit_(?!mat_cust_prop_)[A-Za-z0-9_]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex StablePropertyName();
}
