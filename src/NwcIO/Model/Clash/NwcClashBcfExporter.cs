using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NwcIO.Model.Clash;

/// <summary>
/// Exports Navisworks Clash Detective results to buildingSMART BIM Collaboration Format (BCF 2.1)
/// zip archives (.bcfzip) for direct issue coordination in Revit, Solibri, Archicad, and BIMcollab.
/// </summary>
public static class NwcClashBcfExporter
{
    private static readonly XmlWriterSettings Settings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(false),
        OmitXmlDeclaration = false,
    };

    /// <summary>
    /// Saves a collection of clash results as a BCF 2.1 (.bcfzip) file to disk.
    /// </summary>
    public static void Save(string path, IEnumerable<NwcClashResult> clashes, string projectName = "Navisworks Clash Coordination")
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(clashes);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        Write(fs, clashes, projectName);
    }

    /// <summary>
    /// Exports a clash test to a BCF 2.1 archive file.
    /// </summary>
    public static void Save(string path, NwcClashTest test, string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(test);
        Save(path, test.Results, projectName ?? test.Name);
    }

    /// <summary>
    /// Writes clash results into a stream as a BCF 2.1 zip archive.
    /// </summary>
    public static void Write(Stream stream, IEnumerable<NwcClashResult> clashes, string projectName = "Navisworks Clash Coordination")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(clashes);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // 1. Root bcf.version
        var versionEntry = archive.CreateEntry("bcf.version", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(versionEntry.Open(), new UTF8Encoding(false)))
        {
            writer.WriteLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            writer.WriteLine("""<Version VersionId="2.1" DetailedVersion="2.1" />""");
        }

        // 2. Per-clash topic folders
        foreach (var clash in clashes)
        {
            string topicFolder = clash.ResultGuid.ToString("D");

            // Write markup.bcf
            var markupEntry = archive.CreateEntry($"{topicFolder}/markup.bcf", CompressionLevel.Optimal);
            using (var s = markupEntry.Open())
            using (var xml = XmlWriter.Create(s, Settings))
            {
                WriteMarkupXml(xml, clash, projectName);
            }

            // Write viewpoint.bcfv
            var vpEntry = archive.CreateEntry($"{topicFolder}/viewpoint.bcfv", CompressionLevel.Optimal);
            using (var s = vpEntry.Open())
            using (var xml = XmlWriter.Create(s, Settings))
            {
                WriteViewpointXml(xml, clash);
            }
        }
    }

    private static void WriteMarkupXml(XmlWriter xml, NwcClashResult clash, string projectName)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("Markup", "http://www.buildingsmart-tech.org/specifications/bcf-releases");
        xml.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

        xml.WriteStartElement("Header");
        xml.WriteStartElement("File");
        xml.WriteElementString("Filename", projectName);
        xml.WriteElementString("Date", clash.CreatedDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        xml.WriteEndElement(); // File
        xml.WriteEndElement(); // Header

        xml.WriteStartElement("Topic");
        xml.WriteAttributeString("Guid", clash.ResultGuid.ToString("D"));
        xml.WriteAttributeString("TopicType", "Clash");
        xml.WriteAttributeString("TopicStatus", clash.Status.ToString());

        string title = string.IsNullOrWhiteSpace(clash.Description)
            ? $"{clash.Name}: {clash.Item1.Name} vs {clash.Item2.Name}"
            : clash.Description;

        xml.WriteElementString("Title", title);
        xml.WriteElementString("Priority", "High");

        var descBuilder = new StringBuilder();
        descBuilder.Append(CultureInfo.InvariantCulture, $"Clash {clash.Name} between {clash.Item1.Name} and {clash.Item2.Name}.");
        if (clash.Distance != 0.0)
        {
            descBuilder.Append(CultureInfo.InvariantCulture, $" Distance: {clash.Distance:F3} m.");
        }
        if (!string.IsNullOrEmpty(clash.GridLocation))
        {
            descBuilder.Append(CultureInfo.InvariantCulture, $" Location: {clash.GridLocation}.");
        }
        xml.WriteElementString("Description", descBuilder.ToString());

        xml.WriteElementString("CreationDate", clash.CreatedDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        xml.WriteElementString("CreationAuthor", "NwcIO Clash Detective");

        if (!string.IsNullOrEmpty(clash.AssignedTo))
        {
            xml.WriteElementString("AssignedTo", clash.AssignedTo);
        }

        // Viewpoint link
        xml.WriteStartElement("Viewpoints");
        xml.WriteAttributeString("Guid", clash.ResultGuid.ToString("D"));
        xml.WriteElementString("Viewpoint", "viewpoint.bcfv");
        xml.WriteEndElement(); // Viewpoints

        xml.WriteEndElement(); // Topic

        // Comments
        if (clash.Comments.Count > 0)
        {
            foreach (var comment in clash.Comments)
            {
                xml.WriteStartElement("Comment");
                xml.WriteAttributeString("Guid", Guid.NewGuid().ToString("D"));
                xml.WriteElementString("Date", comment.Date.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                xml.WriteElementString("Author", comment.Author);
                xml.WriteElementString("Comment", comment.Body);
                xml.WriteEndElement(); // Comment
            }
        }

        xml.WriteEndElement(); // Markup
        xml.WriteEndDocument();
    }

    private static void WriteViewpointXml(XmlWriter xml, NwcClashResult clash)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("VisualizationInfo", "http://www.buildingsmart-tech.org/specifications/bcf-releases");
        xml.WriteAttributeString("Guid", clash.ResultGuid.ToString("D"));

        // Camera positioning
        xml.WriteStartElement("PerspectiveCamera");

        var target = clash.ClashPoint;
        var offset = new Vector3d(5.0, -5.0, 3.5);
        var eye = target + offset;
        var dir = (target - eye).Normalize();

        xml.WriteStartElement("CameraViewPoint");
        xml.WriteElementString("X", eye.X.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteElementString("Y", eye.Y.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteElementString("Z", eye.Z.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteEndElement();

        xml.WriteStartElement("CameraDirection");
        xml.WriteElementString("X", dir.X.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteElementString("Y", dir.Y.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteElementString("Z", dir.Z.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteEndElement();

        xml.WriteStartElement("CameraUpVector");
        xml.WriteElementString("X", "0");
        xml.WriteElementString("Y", "0");
        xml.WriteElementString("Z", "1");
        xml.WriteEndElement();

        xml.WriteElementString("FieldOfView", "60");
        xml.WriteEndElement(); // PerspectiveCamera

        // Components selection
        var guids = new List<string>();
        if (!string.IsNullOrEmpty(clash.Item1.IfcGuid)) guids.Add(clash.Item1.IfcGuid);
        if (!string.IsNullOrEmpty(clash.Item2.IfcGuid)) guids.Add(clash.Item2.IfcGuid);

        if (guids.Count > 0)
        {
            xml.WriteStartElement("Components");
            xml.WriteStartElement("Selection");
            foreach (var guid in guids)
            {
                xml.WriteStartElement("Component");
                xml.WriteAttributeString("IfcGuid", guid);
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // Selection
            xml.WriteEndElement(); // Components
        }

        xml.WriteEndElement(); // VisualizationInfo
        xml.WriteEndDocument();
    }
}
