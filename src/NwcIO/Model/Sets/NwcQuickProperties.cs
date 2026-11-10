using System.Xml.Linq;

namespace NwcIO.Model.Sets;

/// <summary>
/// Definition of a single quick property to display in tooltips and summary inspectors.
/// </summary>
public sealed record NwcQuickPropertyDefinition
{
    public string Category { get; init; } = string.Empty;
    public string Property { get; init; } = string.Empty;
    public string? DisplayName { get; init; }

    public string Title => !string.IsNullOrEmpty(DisplayName) ? DisplayName : Property;
}

/// <summary>
/// Configuration of Quick Properties and smart hover tooltips for Navisworks elements.
/// </summary>
public sealed class NwcQuickProperties
{
    public List<NwcQuickPropertyDefinition> Definitions { get; } = [];

    public NwcQuickProperties() { }

    /// <summary>Adds a property definition to the quick properties list.</summary>
    public NwcQuickProperties Add(string category, string property, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(property);

        Definitions.Add(new NwcQuickPropertyDefinition
        {
            Category = category,
            Property = property,
            DisplayName = displayName,
        });
        return this;
    }

    /// <summary>Creates a standard default set of quick properties (Element Name, Type, Material, Layer).</summary>
    public static NwcQuickProperties CreateDefault()
    {
        var qp = new NwcQuickProperties();
        qp.Add("Item", "Name", "Name");
        qp.Add("Item", "Type", "Type");
        qp.Add("Item", "Layer", "Layer");
        qp.Add("Material", "Name", "Material");
        qp.Add("Dimensions", "Area", "Area");
        qp.Add("Dimensions", "Volume", "Volume");
        return qp;
    }

    /// <summary>Extracts quick property key-value pairs from a scene node.</summary>
    public Dictionary<string, string> Extract(NwcNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var result = new Dictionary<string, string>();

        foreach (var def in Definitions)
        {
            if (def.Category.Equals("Item", StringComparison.OrdinalIgnoreCase))
            {
                if (def.Property.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    result[def.Title] = node.Name;
                    continue;
                }
                if (def.Property.Equals("Class", StringComparison.OrdinalIgnoreCase))
                {
                    result[def.Title] = node.Class;
                    continue;
                }
            }

            var prop = node.Properties.FirstOrDefault(p =>
                (p.Class.Equals(def.Category, StringComparison.OrdinalIgnoreCase) || def.Category == "*") &&
                p.Name.Equals(def.Property, StringComparison.OrdinalIgnoreCase));

            if (prop is not null && !string.IsNullOrEmpty(prop.Value))
            {
                result[def.Title] = prop.Value;
            }
        }

        return result;
    }

    /// <summary>Serializes quick property definitions to Navisworks XML format.</summary>
    public string ToXml()
    {
        var doc = new XDocument(
            new XElement("quickproperties",
                Definitions.Select(d =>
                    new XElement("property",
                        new XAttribute("category", d.Category),
                        new XAttribute("name", d.Property),
                        !string.IsNullOrEmpty(d.DisplayName) ? new XAttribute("displayname", d.DisplayName) : null
                    )
                )
            )
        );
        return doc.ToString();
    }

    /// <summary>Parses quick property definitions from Navisworks XML format.</summary>
    public static NwcQuickProperties FromXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        var doc = XDocument.Parse(xml);
        var qp = new NwcQuickProperties();

        var root = doc.Root;
        if (root is not null)
        {
            foreach (var elem in root.Elements("property"))
            {
                string? cat = (string?)elem.Attribute("category");
                string? prop = (string?)elem.Attribute("name");
                string? disp = (string?)elem.Attribute("displayname");

                if (!string.IsNullOrEmpty(cat) && !string.IsNullOrEmpty(prop))
                {
                    qp.Add(cat, prop, disp);
                }
            }
        }

        return qp;
    }
}
