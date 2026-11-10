using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using NwcIO.Model.Redline;

namespace NwcIO.Model.Sectioning;

/// <summary>
/// Exports Navisworks saved viewpoints, section planes, and redlines into
/// buildingSMART BIM Collaboration Format (BCF 2.1) zip archives (.bcfzip)
/// for direct issue tracking and coordination across Revit, Solibri, and Archicad.
/// </summary>
public static class NwcViewpointBcfExporter
{
    private static readonly XmlWriterSettings Settings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(false),
        OmitXmlDeclaration = false,
    };

    /// <summary>Saves a collection of saved viewpoints as a BCF 2.1 (.bcfzip) file to disk.</summary>
    public static void Save(string path, IEnumerable<NwcSavedViewpoint> viewpoints, string projectName = "Navisworks Viewpoint Coordination")
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(viewpoints);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        Write(fs, viewpoints, projectName);
    }

    /// <summary>Writes saved viewpoints into a stream as a BCF 2.1 zip archive.</summary>
    public static void Write(Stream stream, IEnumerable<NwcSavedViewpoint> viewpoints, string projectName = "Navisworks Viewpoint Coordination")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(viewpoints);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        // 1. Root bcf.version
        var versionEntry = archive.CreateEntry("bcf.version", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(versionEntry.Open(), new UTF8Encoding(false)))
        {
            writer.WriteLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            writer.WriteLine("""<Version VersionId="2.1" DetailedVersion="2.1" />""");
        }

        // 2. Per-viewpoint topic folders
        foreach (var vp in viewpoints)
        {
            string topicFolder = vp.Id.ToString("D");

            // Write markup.bcf
            var markupEntry = archive.CreateEntry($"{topicFolder}/markup.bcf", CompressionLevel.Optimal);
            using (var s = markupEntry.Open())
            using (var xml = XmlWriter.Create(s, Settings))
            {
                WriteMarkupXml(xml, vp, projectName);
            }

            // Write viewpoint.bcfv
            var vpEntry = archive.CreateEntry($"{topicFolder}/viewpoint.bcfv", CompressionLevel.Optimal);
            using (var s = vpEntry.Open())
            using (var xml = XmlWriter.Create(s, Settings))
            {
                WriteViewpointXml(xml, vp);
            }
        }
    }

    private static void WriteMarkupXml(XmlWriter xml, NwcSavedViewpoint vp, string projectName)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("Markup", "http://www.buildingsmart-tech.org/specifications/bcf-releases");

        xml.WriteStartElement("Header");
        xml.WriteStartElement("File");
        xml.WriteAttributeString("IfcProject", projectName);
        xml.WriteAttributeString("Date", DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z");
        xml.WriteElementString("Filename", "Navisworks.nwd");
        xml.WriteEndElement();
        xml.WriteEndElement();

        // Topic
        xml.WriteStartElement("Topic");
        xml.WriteAttributeString("Guid", vp.Id.ToString("D"));
        xml.WriteAttributeString("TopicType", "Review");
        xml.WriteAttributeString("TopicStatus", "Active");

        xml.WriteElementString("Title", vp.Name);
        xml.WriteElementString("CreationDate", DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z");
        xml.WriteElementString("Description", $"Navisworks viewpoint '{vp.Name}' with {vp.Redlines.Count} redlines and {vp.SectionPlanes.Count} section planes.");
        xml.WriteEndElement(); // Topic

        // Viewpoint reference
        xml.WriteStartElement("Viewpoints");
        xml.WriteAttributeString("Guid", vp.Id.ToString("D"));
        xml.WriteElementString("Viewpoint", "viewpoint.bcfv");
        xml.WriteEndElement();

        // Comments
        foreach (var c in vp.Comments)
        {
            xml.WriteStartElement("Comment");
            xml.WriteAttributeString("Guid", Guid.NewGuid().ToString("D"));
            xml.WriteElementString("Date", DateTime.UtcNow.ToString("s", CultureInfo.InvariantCulture) + "Z");
            xml.WriteElementString("Author", "Navisworks");
            xml.WriteElementString("Comment", c);
            xml.WriteEndElement();
        }

        foreach (var tag in vp.Redlines.OfType<NwcRedlineTag>())
        {
            if (!string.IsNullOrWhiteSpace(tag.Comment))
            {
                xml.WriteStartElement("Comment");
                xml.WriteAttributeString("Guid", tag.Id.ToString("D"));
                xml.WriteElementString("Date", (tag.CreatedAt ?? DateTime.UtcNow).ToString("s", CultureInfo.InvariantCulture) + "Z");
                xml.WriteElementString("Author", tag.Author ?? "Reviewer");
                xml.WriteElementString("Comment", $"[Tag #{tag.TagNumber}] {tag.Comment}");
                xml.WriteEndElement();
            }
        }

        xml.WriteEndElement(); // Markup
        xml.WriteEndDocument();
    }

    private static void WriteViewpointXml(XmlWriter xml, NwcSavedViewpoint vp)
    {
        xml.WriteStartDocument();
        xml.WriteStartElement("VisualizationInfo", "http://www.buildingsmart-tech.org/specifications/bcf-releases");
        xml.WriteAttributeString("Guid", vp.Id.ToString("D"));

        var cam = vp.Camera;
        var forward = cam.Direction;
        var up = cam.Up;

        if (cam.Projection == NwcCameraProjection.Orthographic)
        {
            xml.WriteStartElement("OrthogonalCamera");
            xml.WriteStartElement("CameraViewPoint");
            WritePoint(xml, cam.Position);
            xml.WriteEndElement();
            xml.WriteStartElement("CameraDirection");
            WritePoint(xml, forward);
            xml.WriteEndElement();
            xml.WriteStartElement("CameraUpVector");
            WritePoint(xml, up);
            xml.WriteEndElement();
            xml.WriteElementString("ViewToWorldScale", cam.FocalDistance.ToString("R", CultureInfo.InvariantCulture));
            xml.WriteEndElement();
        }
        else
        {
            xml.WriteStartElement("PerspectiveCamera");
            xml.WriteStartElement("CameraViewPoint");
            WritePoint(xml, cam.Position);
            xml.WriteEndElement();
            xml.WriteStartElement("CameraDirection");
            WritePoint(xml, forward);
            xml.WriteEndElement();
            xml.WriteStartElement("CameraUpVector");
            WritePoint(xml, up);
            xml.WriteEndElement();
            xml.WriteElementString("FieldOfView", cam.FieldOfView.ToString("R", CultureInfo.InvariantCulture));
            xml.WriteEndElement();
        }

        // Section clipping planes
        var allPlanes = new List<NwcSectionPlane>(vp.SectionPlanes);
        if (vp.SectionBox is not null && vp.SectionBox.IsEnabled)
        {
            allPlanes.AddRange(vp.SectionBox.ToSectionPlanes());
        }

        if (allPlanes.Count > 0)
        {
            xml.WriteStartElement("ClippingPlanes");
            foreach (var plane in allPlanes.Where(p => p.IsEnabled))
            {
                var n = plane.Normal.Normalize();
                var loc = n * plane.Distance;

                xml.WriteStartElement("ClippingPlane");
                xml.WriteStartElement("Location");
                WritePoint(xml, loc);
                xml.WriteEndElement();
                xml.WriteStartElement("Direction");
                WritePoint(xml, n);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }

        // Redlines (Lines and Arrows as BCF Lines)
        var lines = new List<(Vector3d Start, Vector3d End)>();
        foreach (var r in vp.Redlines)
        {
            if (r is NwcRedlineArrow a) lines.Add((a.StartPoint, a.EndPoint));
            else if (r is NwcRedlineLine l) lines.Add((l.StartPoint, l.EndPoint));
        }

        if (lines.Count > 0)
        {
            xml.WriteStartElement("Lines");
            foreach (var (p0, p1) in lines)
            {
                xml.WriteStartElement("Line");
                xml.WriteStartElement("StartPoint");
                WritePoint(xml, p0);
                xml.WriteEndElement();
                xml.WriteStartElement("EndPoint");
                WritePoint(xml, p1);
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }

        xml.WriteEndElement(); // VisualizationInfo
        xml.WriteEndDocument();
    }

    private static void WritePoint(XmlWriter xml, Vector3d pt)
    {
        xml.WriteElementString("X", pt.X.ToString("R", CultureInfo.InvariantCulture));
        xml.WriteElementString("Y", pt.Y.ToString("R", CultureInfo.InvariantCulture));
        xml.WriteElementString("Z", pt.Z.ToString("R", CultureInfo.InvariantCulture));
    }
}
