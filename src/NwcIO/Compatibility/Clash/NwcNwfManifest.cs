using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NwcIO.Model.Appearance;
using NwcIO.Model.Sets;

namespace NwcIO.Model.Clash;

/// <summary>
/// Represents a Navisworks Federated Project Manifest (NWF) containing referenced model files,
/// saved viewpoints, selection sets, appearance profiler rules, and clash detective tests.
/// </summary>
public sealed class NwcNwfManifest
{
    private static readonly XmlWriterSettings Settings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(false),
        OmitXmlDeclaration = false,
    };

    public string ProjectName { get; set; } = "Navisworks Coordination Project";
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public List<string> ModelPaths { get; } = [];
    public List<NwcSavedViewpoint> SavedViewpoints { get; } = [];
    public List<NwcSetItem> Sets { get; } = [];
    public NwcAppearanceProfiler AppearanceProfiler { get; set; } = new();
    public List<NwcClashTest> ClashTests { get; } = [];

    public NwcNwfManifest() { }

    public NwcNwfManifest(string projectName)
    {
        ProjectName = projectName;
    }

    /// <summary>
    /// Saves the coordination project manifest as an XML file.
    /// </summary>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        Write(fs);
    }

    /// <summary>
    /// Writes the manifest into a stream.
    /// </summary>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var xml = XmlWriter.Create(stream, Settings);

        xml.WriteStartDocument();
        xml.WriteStartElement("NavisworksFederation");
        xml.WriteAttributeString("project", ProjectName);
        xml.WriteAttributeString("date", CreatedDate.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));

        // Referenced Models
        xml.WriteStartElement("Models");
        foreach (var modelPath in ModelPaths)
        {
            xml.WriteStartElement("Model");
            xml.WriteAttributeString("path", modelPath);
            xml.WriteEndElement();
        }
        xml.WriteEndElement(); // Models

        // Reuse the exchange serializers so nested sets and rule settings are preserved.
        if (SavedViewpoints.Count > 0)
        {
            xml.WriteStartElement("Viewpoints");
            XDocument.Parse(Sectioning.NwcXmlViewpointSerializer.Serialize(SavedViewpoints)).Root!.WriteTo(xml);
            xml.WriteEndElement();
        }
        if (Sets.Count > 0)
        {
            WriteExchange(xml, "Sets", stream => NwcXmlSetSerializer.Write(Sets, stream));
        }
        if (AppearanceProfiler.Rules.Count > 0)
        {
            WriteExchange(xml, "AppearanceRules", stream => NwcXmlAppearanceProfilerSerializer.Write(AppearanceProfiler, stream));
        }

        // Clash Tests
        if (ClashTests.Count > 0)
        {
            xml.WriteStartElement("ClashDetective");
            foreach (var ct in ClashTests)
            {
                xml.WriteStartElement("Test");
                xml.WriteAttributeString("name", ct.Name);
                xml.WriteAttributeString("type", ct.TestType.ToString());
                xml.WriteAttributeString("tolerance", ct.Tolerance.ToString("0.######", CultureInfo.InvariantCulture));
                xml.WriteAttributeString("clashCount", ct.TotalCount.ToString(CultureInfo.InvariantCulture));
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // ClashDetective
        }

        xml.WriteEndElement(); // NavisworksFederation
        xml.WriteEndDocument();
    }

    /// <summary>
    /// Reads a federation project manifest from a file.
    /// </summary>
    public static NwcNwfManifest Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    private static void WriteExchange(XmlWriter xml, string element, Action<Stream> write)
    {
        using var stream = new MemoryStream();
        write(stream);
        stream.Position = 0;
        xml.WriteStartElement(element);
        XDocument.Load(stream).Root!.WriteTo(xml);
        xml.WriteEndElement();
    }

    /// <summary>
    /// Reads a federation project manifest from a stream.
    /// </summary>
    public static NwcNwfManifest Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = XDocument.Load(stream);
        var root = doc.Root ?? throw new InvalidDataException("Invalid XML manifest: root element missing.");

        var manifest = new NwcNwfManifest((string?)root.Attribute("project") ?? "Navisworks Coordination Project");

        if (DateTimeOffset.TryParse((string?)root.Attribute("date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            manifest.CreatedDate = date;
        }

        var viewpoints = root.Element("Viewpoints");
        if (viewpoints is not null)
        {
            foreach (var element in viewpoints.Descendants().Where(e => e.Name.LocalName.Equals("viewpoint", StringComparison.OrdinalIgnoreCase)))
            {
                var viewpoint = Sectioning.NwcXmlViewpointSerializer.DeserializeViewpoint(element);
                if (viewpoint is null)
                {
                    continue;
                }
                // Pair legacy camera attributes with the element that produced this viewpoint.
                if (element.Attribute("cameraX") is not null)
                {
                    viewpoint = viewpoint with
                    {
                        Camera = new NwcCamera
                        {
                            Position = new Vector3d((double)element.Attribute("cameraX")!, (double)element.Attribute("cameraY")!, (double)element.Attribute("cameraZ")!),
                            Rotation = Quaterniond.Identity,
                        },
                    };
                }
                manifest.SavedViewpoints.Add(viewpoint);
            }
        }
        if (root.Element("Sets")?.Element("exchange") is { } sets)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(sets.ToString()));
            manifest.Sets.AddRange(NwcXmlSetSerializer.Read(input));
        }
        if (root.Element("AppearanceRules") is { } appearance)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(appearance.ToString()));
            manifest.AppearanceProfiler = NwcXmlAppearanceProfilerSerializer.Read(input);
            foreach (var rule in appearance.Elements("Rule"))
            {
                var colorText = (string?)rule.Attribute("color");
                if (colorText is not null && colorText.StartsWith("rgba(", StringComparison.Ordinal) && colorText.EndsWith(')'))
                {
                    colorText = colorText[5..^1];
                }
                var color = RgbaColor.TryParse(colorText, out var parsedColor) ? parsedColor : RgbaColor.White;
                manifest.AppearanceProfiler.Rules.Add(new NwcAppearanceRule(
                    (string?)rule.Attribute("name") ?? "Rule",
                    new NwcSearchCondition((string?)rule.Attribute("category") ?? "Item", (string?)rule.Attribute("property") ?? "Name",
                        NwcSearchOperator.Equals, (string?)rule.Attribute("value") ?? string.Empty), color));
            }
        }

        var modelsElem = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("Models", StringComparison.OrdinalIgnoreCase));
        if (modelsElem is not null)
        {
            foreach (var m in modelsElem.Elements().Where(e => e.Name.LocalName.Equals("Model", StringComparison.OrdinalIgnoreCase)))
            {
                string? p = (string?)m.Attribute("path");
                if (!string.IsNullOrEmpty(p)) manifest.ModelPaths.Add(p);
            }
        }

        var ctElem = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("ClashDetective", StringComparison.OrdinalIgnoreCase));
        if (ctElem is not null)
        {
            foreach (var t in ctElem.Elements().Where(e => e.Name.LocalName.Equals("Test", StringComparison.OrdinalIgnoreCase)))
            {
                string name = (string?)t.Attribute("name") ?? "Clash Test";
                double tol = double.TryParse((string?)t.Attribute("tolerance"), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedTol) ? parsedTol : 0.001;
                var type = Enum.TryParse<NwcClashTestType>((string?)t.Attribute("type"), true, out var parsedType) ? parsedType : NwcClashTestType.Hard;
                manifest.ClashTests.Add(new NwcClashTest(name, type, tol));
            }
        }

        return manifest;
    }
}
