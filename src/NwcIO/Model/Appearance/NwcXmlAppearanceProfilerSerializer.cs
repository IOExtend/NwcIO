using System.Globalization;
using System.Text;
using System.Xml.Linq;
using NwcIO.Model.Sets;

namespace NwcIO.Model.Appearance;

/// <summary>
/// Serializes and deserializes Navisworks Appearance Profiler rule files (.xml).
/// Fully compatible with Autodesk Navisworks Manage and Simulate Appearance Profiler import/export.
/// </summary>
public static class NwcXmlAppearanceProfilerSerializer
{
    private const string SchemaLocation = "http://download.autodesk.com/us/navisworks/schemas/nw-exchange-12.0.xsd";

    /// <summary>
    /// Saves appearance profiler rules to an XML file.
    /// </summary>
    public static void Save(NwcAppearanceProfiler profiler, string filePath)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        ArgumentNullException.ThrowIfNull(filePath);

        using var fs = File.Create(filePath);
        Write(profiler, fs);
    }

    /// <summary>
    /// Writes appearance profiler rules to an XML stream.
    /// </summary>
    public static void Write(NwcAppearanceProfiler profiler, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        ArgumentNullException.ThrowIfNull(stream);

        var root = new XElement("exchange",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "noNamespaceSchemaLocation", SchemaLocation));

        var appearanceProfiler = new XElement("appearanceprofiler");
        var rulesElem = new XElement("rules");
        appearanceProfiler.Add(rulesElem);
        root.Add(appearanceProfiler);

        foreach (var rule in profiler.Rules)
        {
            var ruleElem = new XElement("rule",
                new XAttribute("name", rule.Name),
                new XAttribute("enabled", rule.Enabled ? "true" : "false"));

            var colorElem = new XElement("color",
                new XAttribute("r", rule.Color.R.ToString("0.######", CultureInfo.InvariantCulture)),
                new XAttribute("g", rule.Color.G.ToString("0.######", CultureInfo.InvariantCulture)),
                new XAttribute("b", rule.Color.B.ToString("0.######", CultureInfo.InvariantCulture)));
            ruleElem.Add(colorElem);

            ruleElem.Add(new XElement("transparency", rule.Transparency.ToString("0.######", CultureInfo.InvariantCulture)));

            var cond = rule.Condition;
            var condElem = new XElement("condition",
                new XAttribute("category", cond.Category),
                new XAttribute("property", cond.Property),
                new XAttribute("operator", OperatorToString(cond.Operator)),
                new XAttribute("value", cond.Value));

            ruleElem.Add(new XElement("conditions", condElem));
            rulesElem.Add(ruleElem);
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
        doc.Save(writer);
    }

    /// <summary>
    /// Loads appearance profiler rules from an XML file on disk.
    /// </summary>
    public static NwcAppearanceProfiler Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var fs = File.OpenRead(filePath);
        return Read(fs);
    }

    /// <summary>
    /// Reads appearance profiler rules from an XML stream.
    /// </summary>
    public static NwcAppearanceProfiler Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var doc = XDocument.Load(stream);
        var profiler = new NwcAppearanceProfiler();

        var rulesRoot = doc.Descendants("appearanceprofiler").Elements("rules").FirstOrDefault()
            ?? doc.Descendants("rules").FirstOrDefault();

        if (rulesRoot is null) return profiler;

        foreach (var ruleElem in rulesRoot.Elements("rule"))
        {
            string name = (string?)ruleElem.Attribute("name") ?? "Rule";
            bool enabled = (bool?)ruleElem.Attribute("enabled") ?? true;

            var colorElem = ruleElem.Element("color");
            float r = 1.0f, g = 1.0f, b = 1.0f;
            if (colorElem is not null)
            {
                if (float.TryParse((string?)colorElem.Attribute("r"), NumberStyles.Float, CultureInfo.InvariantCulture, out float cr)) r = cr;
                if (float.TryParse((string?)colorElem.Attribute("g"), NumberStyles.Float, CultureInfo.InvariantCulture, out float cg)) g = cg;
                if (float.TryParse((string?)colorElem.Attribute("b"), NumberStyles.Float, CultureInfo.InvariantCulture, out float cb)) b = cb;
            }

            float transparency = 0.0f;
            var transElem = ruleElem.Element("transparency");
            if (transElem is not null && float.TryParse(transElem.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float tr))
            {
                transparency = tr;
            }

            var condition = new NwcSearchCondition();
            var condElem = ruleElem.Descendants("condition").FirstOrDefault();
            if (condElem is not null)
            {
                condition.Category = (string?)condElem.Attribute("category") ?? "Item";
                condition.Property = (string?)condElem.Attribute("property") ?? "Name";
                condition.Operator = ParseOperator((string?)condElem.Attribute("operator"));
                condition.Value = (string?)condElem.Attribute("value") ?? string.Empty;
            }

            profiler.Rules.Add(new NwcAppearanceRule
            {
                Name = name,
                Enabled = enabled,
                Color = new RgbaColor(r, g, b, 1.0f),
                Transparency = transparency,
                Condition = condition,
            });
        }

        return profiler;
    }

    private static string OperatorToString(NwcSearchOperator op) => op switch
    {
        NwcSearchOperator.Equals => "equals",
        NwcSearchOperator.NotEquals => "not_equals",
        NwcSearchOperator.Contains => "contains",
        NwcSearchOperator.StartsWith => "starts_with",
        NwcSearchOperator.EndsWith => "ends_with",
        NwcSearchOperator.GreaterThan => "greater_than",
        NwcSearchOperator.LessThan => "less_than",
        NwcSearchOperator.GreaterThanOrEqual => "greater_equal",
        NwcSearchOperator.LessThanOrEqual => "less_equal",
        _ => "equals",
    };

    private static NwcSearchOperator ParseOperator(string? opStr) => opStr?.ToLowerInvariant() switch
    {
        "contains" => NwcSearchOperator.Contains,
        "not_equals" or "notequals" or "!=" => NwcSearchOperator.NotEquals,
        "starts_with" or "startswith" => NwcSearchOperator.StartsWith,
        "ends_with" or "endswith" => NwcSearchOperator.EndsWith,
        "greater_than" or "greaterthan" or ">" => NwcSearchOperator.GreaterThan,
        "less_than" or "lessthan" or "<" => NwcSearchOperator.LessThan,
        "greater_equal" or ">=" => NwcSearchOperator.GreaterThanOrEqual,
        "less_equal" or "<=" => NwcSearchOperator.LessThanOrEqual,
        _ => NwcSearchOperator.Equals,
    };
}
