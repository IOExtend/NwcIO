using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using NwcIO.Internal.Export;

namespace NwcIO.Model.Diff;

/// <summary>
/// Kind of modification detected for a Navisworks scene item or geometric element between revisions.
/// </summary>
public enum NwcChangeKind
{
    Added,
    Deleted,
    Modified,
    Unchanged,
}

/// <summary>
/// Options configuring Navisworks model revision comparison.
/// </summary>
public sealed class NwcDiffOptions
{
    /// <summary>
    /// Geometric displacement tolerance in meters below which changes in bounding box center or vertex coordinates are ignored (default: 0.005m = 5mm).
    /// </summary>
    public double GeometricTolerance { get; set; } = 0.005;

    /// <summary>Whether to compare property records attached to scene nodes (default: true).</summary>
    public bool CompareProperties { get; set; } = true;

    /// <summary>Whether to compare 3D spatial transforms (default: true).</summary>
    public bool CompareTransforms { get; set; } = true;

    /// <summary>Optional filter matching node names or classes to compare.</summary>
    public string? NameFilter { get; set; }
}

/// <summary>
/// Detail of a single property record change between model revisions.
/// </summary>
public sealed record NwcPropertyChange(
    string Category,
    string PropertyName,
    string? OldValue,
    string? NewValue,
    string ChangeType // "Added", "Removed", "Modified"
);

/// <summary>
/// Difference record for a specific element or node between baseline and revision models.
/// </summary>
public sealed class NwcElementDiff
{
    public required int NodeId { get; init; }

    public required string Name { get; init; }

    public string? ClassName { get; init; }

    public Guid? ElementGuid { get; init; }

    public NwcChangeKind ChangeKind { get; set; } = NwcChangeKind.Unchanged;

    public bool GeometryChanged { get; set; }

    public Vector3d TranslationDelta { get; set; }

    public List<NwcPropertyChange> PropertyChanges { get; } = [];

    public NwcNode? BaselineNode { get; init; }

    public NwcNode? RevisionNode { get; init; }

    public NwcMesh? BaselineMesh { get; init; }

    public NwcMesh? RevisionMesh { get; init; }

    /// <summary>
    /// Gets the best available 3D centroid for this element (from mesh or transform).
    /// </summary>
    public Vector3d Centroid
    {
        get
        {
            var mesh = RevisionMesh ?? BaselineMesh;
            if (mesh is not null)
            {
                var box = mesh.GetWorldBoundingBox();
                if (!box.IsEmpty) return box.Center;
            }

            var node = RevisionNode ?? BaselineNode;
            if (node?.Transform is not null)
            {
                return node.Transform.Translation;
            }

            return Vector3d.Zero;
        }
    }

    public override string ToString() => $"[{ChangeKind}] {Name} ({ClassName ?? "Node"})";
}

/// <summary>
/// Complete revision comparison report between two Navisworks documents or federated assemblies.
/// </summary>
public sealed class NwcDiffReport
{
    public string BaselineName { get; }
    public string RevisionName { get; }

    public List<NwcElementDiff> Added { get; } = [];
    public List<NwcElementDiff> Deleted { get; } = [];
    public List<NwcElementDiff> Modified { get; } = [];
    public List<NwcElementDiff> Unchanged { get; } = [];

    public int TotalChangesCount => Added.Count + Deleted.Count + Modified.Count;

    public NwcDiffReport(string baselineName, string revisionName)
    {
        BaselineName = baselineName;
        RevisionName = revisionName;
    }

    /// <summary>
    /// Serializes the comparison report to JSON string.
    /// </summary>
    public string ToJson(bool indented = true)
    {
        using var stream = new MemoryStream();
        SaveJson(stream, indented);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Saves the comparison report to a JSON file.
    /// </summary>
    public void SaveJson(string path, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(path);
        SaveJson(stream, indented);
    }

    /// <summary>
    /// Saves the comparison report to a JSON stream.
    /// </summary>
    public void SaveJson(Stream stream, bool indented = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var jsonOptions = new JsonWriterOptions
        {
            Indented = indented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using var writer = new Utf8JsonWriter(stream, jsonOptions);
        writer.WriteStartObject();
        writer.WriteString("generator", "NwcIO NwcDiffEngine");
        writer.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteString("baseline", BaselineName);
        writer.WriteString("revision", RevisionName);

        writer.WriteStartObject("summary");
        writer.WriteNumber("added", Added.Count);
        writer.WriteNumber("deleted", Deleted.Count);
        writer.WriteNumber("modified", Modified.Count);
        writer.WriteNumber("unchanged", Unchanged.Count);
        writer.WriteNumber("totalChanges", TotalChangesCount);
        writer.WriteEndObject();

        WriteDiffList(writer, "added", Added);
        WriteDiffList(writer, "deleted", Deleted);
        WriteDiffList(writer, "modified", Modified);

        writer.WriteEndObject();
    }

    private static void WriteDiffList(Utf8JsonWriter writer, string propertyName, IReadOnlyList<NwcElementDiff> list)
    {
        writer.WriteStartArray(propertyName);
        foreach (var item in list)
        {
            writer.WriteStartObject();
            writer.WriteNumber("nodeId", item.NodeId);
            writer.WriteString("name", item.Name);
            if (!string.IsNullOrEmpty(item.ClassName)) writer.WriteString("className", item.ClassName);
            if (item.ElementGuid.HasValue) writer.WriteString("elementGuid", item.ElementGuid.Value.ToString("D"));
            writer.WriteString("changeKind", item.ChangeKind.ToString());
            writer.WriteBoolean("geometryChanged", item.GeometryChanged);

            if (item.GeometryChanged)
            {
                writer.WriteStartObject("translationDelta");
                writer.WriteNumber("x", item.TranslationDelta.X);
                writer.WriteNumber("y", item.TranslationDelta.Y);
                writer.WriteNumber("z", item.TranslationDelta.Z);
                writer.WriteEndObject();
            }

            if (item.PropertyChanges.Count > 0)
            {
                writer.WriteStartArray("propertyChanges");
                foreach (var p in item.PropertyChanges)
                {
                    writer.WriteStartObject();
                    writer.WriteString("category", p.Category);
                    writer.WriteString("name", p.PropertyName);
                    writer.WriteString("oldValue", p.OldValue);
                    writer.WriteString("newValue", p.NewValue);
                    writer.WriteString("changeType", p.ChangeType);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <summary>
    /// Exports the revision changes to a buildingSMART BCF 2.1 zip archive (.bcfzip) with 3D viewpoint cameras.
    /// </summary>
    public void SaveBcf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        SaveBcf(fs);
    }

    /// <summary>
    /// Exports the revision changes to a BCF 2.1 zip stream.
    /// </summary>
    public void SaveBcf(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var xmlSettings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        };

        // 1. bcf.version
        var versionEntry = zip.CreateEntry("bcf.version");
        using (var vStream = versionEntry.Open())
        using (var xml = XmlWriter.Create(vStream, xmlSettings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("Version");
            xml.WriteAttributeString("VersionId", "2.1");
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        // 2. project.bcfp
        var projEntry = zip.CreateEntry("project.bcfp");
        using (var pStream = projEntry.Open())
        using (var xml = XmlWriter.Create(pStream, xmlSettings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("ProjectExtension");
            xml.WriteStartElement("Project");
            xml.WriteAttributeString("ProjectId", Guid.NewGuid().ToString());
            xml.WriteElementString("Name", $"{BaselineName} vs {RevisionName}");
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        void WriteTopic(NwcElementDiff item, string changePrefix, string priority)
        {
            string topicGuid = Guid.NewGuid().ToString();
            string vpGuid = Guid.NewGuid().ToString();

            var markupEntry = zip.CreateEntry($"{topicGuid}/markup.bcf");
            using (var mStream = markupEntry.Open())
            using (var xml = XmlWriter.Create(mStream, xmlSettings))
            {
                xml.WriteStartDocument();
                xml.WriteStartElement("Markup");
                xml.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                xml.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");

                xml.WriteStartElement("Header");
                xml.WriteStartElement("File");
                xml.WriteAttributeString("IfcProject", "0");
                xml.WriteAttributeString("Date", timestamp);
                xml.WriteElementString("Filename", $"{BaselineName} vs {RevisionName}");
                xml.WriteEndElement();
                xml.WriteEndElement();

                xml.WriteStartElement("Topic");
                xml.WriteAttributeString("Guid", topicGuid);
                xml.WriteAttributeString("TopicType", "Change");
                xml.WriteAttributeString("TopicStatus", "Active");

                string title = $"[{changePrefix}] {item.ClassName ?? "Element"}: {item.Name}";
                xml.WriteElementString("Title", title);
                xml.WriteElementString("Priority", priority);
                xml.WriteElementString("CreationDate", timestamp);
                xml.WriteElementString("CreationAuthor", "NwcDiffEngine");

                var desc = new StringBuilder();
                desc.AppendLine(CultureInfo.InvariantCulture, $"{changePrefix} item '{item.Name}' in revision {RevisionName}.");
                if (item.GeometryChanged)
                {
                    desc.AppendLine(CultureInfo.InvariantCulture, $" - 3D Geometry shifted by: {item.TranslationDelta}");
                }
                if (item.PropertyChanges.Count > 0)
                {
                    desc.AppendLine(CultureInfo.InvariantCulture, $" - Property changes ({item.PropertyChanges.Count}):");
                    foreach (var p in item.PropertyChanges.Take(10))
                    {
                        desc.AppendLine(CultureInfo.InvariantCulture, $"   * {p.Category}.{p.PropertyName}: '{p.OldValue}' -> '{p.NewValue}' ({p.ChangeType})");
                    }
                }
                xml.WriteElementString("Description", desc.ToString().TrimEnd());
                xml.WriteEndElement(); // Topic

                xml.WriteStartElement("Viewpoints");
                xml.WriteAttributeString("Guid", vpGuid);
                xml.WriteElementString("Viewpoint", "viewpoint.bcfv");
                xml.WriteEndElement();

                xml.WriteEndElement(); // Markup
                xml.WriteEndDocument();
            }

            // viewpoint.bcfv
            var vpEntry = zip.CreateEntry($"{topicGuid}/viewpoint.bcfv");
            using (var vStream = vpEntry.Open())
            using (var xml = XmlWriter.Create(vStream, xmlSettings))
            {
                xml.WriteStartDocument();
                xml.WriteStartElement("VisualizationInfo");
                xml.WriteAttributeString("Guid", vpGuid);

                var center = item.Centroid;
                var eye = center + new Vector3d(5.0, -5.0, 4.0);
                var dir = (center - eye).Normalize();

                xml.WriteStartElement("PerspectiveCamera");
                xml.WriteStartElement("CameraViewPoint");
                xml.WriteElementString("X", eye.X.ToString("F4", CultureInfo.InvariantCulture));
                xml.WriteElementString("Y", eye.Y.ToString("F4", CultureInfo.InvariantCulture));
                xml.WriteElementString("Z", eye.Z.ToString("F4", CultureInfo.InvariantCulture));
                xml.WriteEndElement();

                xml.WriteStartElement("CameraDirection");
                xml.WriteElementString("X", dir.X.ToString("F4", CultureInfo.InvariantCulture));
                xml.WriteElementString("Y", dir.Y.ToString("F4", CultureInfo.InvariantCulture));
                xml.WriteElementString("Z", dir.Z.ToString("F4", CultureInfo.InvariantCulture));
                xml.WriteEndElement();

                xml.WriteStartElement("CameraUpVector");
                xml.WriteElementString("X", "0.0000");
                xml.WriteElementString("Y", "0.0000");
                xml.WriteElementString("Z", "1.0000");
                xml.WriteEndElement();

                xml.WriteElementString("FieldOfView", "60.00");
                xml.WriteEndElement(); // PerspectiveCamera

                if (item.ElementGuid.HasValue)
                {
                    string ifcGuid = IfcGuid.ToIfcGuid(item.ElementGuid.Value);
                    xml.WriteStartElement("Components");
                    xml.WriteStartElement("Selection");
                    xml.WriteStartElement("Component");
                    xml.WriteAttributeString("IfcGuid", ifcGuid);
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                }

                xml.WriteEndElement(); // VisualizationInfo
                xml.WriteEndDocument();
            }
        }

        foreach (var item in Added)
        {
            WriteTopic(item, "Added", "Normal");
        }

        foreach (var item in Deleted)
        {
            WriteTopic(item, "Deleted", "Normal");
        }

        foreach (var item in Modified)
        {
            WriteTopic(item, "Modified", "High");
        }
    }
}
