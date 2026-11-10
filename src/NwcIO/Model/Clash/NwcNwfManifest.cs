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
        xml.WriteAttributeString("date", CreatedDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        // Referenced Models
        xml.WriteStartElement("Models");
        foreach (var modelPath in ModelPaths)
        {
            xml.WriteStartElement("Model");
            xml.WriteAttributeString("path", modelPath);
            xml.WriteEndElement();
        }
        xml.WriteEndElement(); // Models

        // Saved Viewpoints
        if (SavedViewpoints.Count > 0)
        {
            xml.WriteStartElement("Viewpoints");
            foreach (var vp in SavedViewpoints)
            {
                xml.WriteStartElement("Viewpoint");
                xml.WriteAttributeString("name", vp.Name);
                if (vp.CameraPosition.HasValue)
                {
                    xml.WriteAttributeString("cameraX", vp.CameraPosition.Value.X.ToString("0.###", CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("cameraY", vp.CameraPosition.Value.Y.ToString("0.###", CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("cameraZ", vp.CameraPosition.Value.Z.ToString("0.###", CultureInfo.InvariantCulture));
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // Viewpoints
        }

        // Appearance Profiler Rules
        if (AppearanceProfiler.Rules.Count > 0)
        {
            xml.WriteStartElement("AppearanceRules");
            foreach (var rule in AppearanceProfiler.Rules)
            {
                xml.WriteStartElement("Rule");
                xml.WriteAttributeString("name", rule.Name);
                xml.WriteAttributeString("category", rule.Condition.Category);
                xml.WriteAttributeString("property", rule.Condition.Property);
                xml.WriteAttributeString("value", rule.Condition.Value);
                xml.WriteAttributeString("color", rule.Color.ToString());
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // AppearanceRules
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

    /// <summary>
    /// Reads a federation project manifest from a stream.
    /// </summary>
    public static NwcNwfManifest Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = XDocument.Load(stream);
        var root = doc.Root ?? throw new InvalidDataException("Invalid XML manifest: root element missing.");

        var manifest = new NwcNwfManifest((string?)root.Attribute("project") ?? "Navisworks Coordination Project");

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
                manifest.ClashTests.Add(new NwcClashTest(name, NwcClashTestType.Hard, tol));
            }
        }

        return manifest;
    }
}
