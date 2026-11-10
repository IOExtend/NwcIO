using System.Globalization;
using System.Text;
using System.Xml.Linq;
using NwcIO.Model.Redline;

namespace NwcIO.Model.Sectioning;

/// <summary>
/// Serializes and deserializes saved viewpoints, cameras, clipping section planes, and redline markups
/// to and from Autodesk Navisworks XML viewpoints exchange format (`.xml`).
/// </summary>
public static class NwcXmlViewpointSerializer
{
    private const string SchemaLocation = "http://download.autodesk.com/us/navisworks/schemas/nw-exchange-12.0.xsd";

    /// <summary>Serializes a list of saved viewpoints to a Navisworks exchange XML string.</summary>
    public static string Serialize(IEnumerable<NwcSavedViewpoint> viewpoints, string units = "m")
    {
        ArgumentNullException.ThrowIfNull(viewpoints);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("exchange",
                new XAttribute(XNamespace.None + "units", units),
                new XElement("viewpoints",
                    viewpoints.Select(SerializeViewpoint))));

        return doc.Declaration + Environment.NewLine + doc.ToString();
    }

    /// <summary>Writes saved viewpoints to a file in Navisworks XML format.</summary>
    public static void Save(string path, IEnumerable<NwcSavedViewpoint> viewpoints, string units = "m")
    {
        ArgumentNullException.ThrowIfNull(path);
        string xml = Serialize(viewpoints, units);
        File.WriteAllText(path, xml, Encoding.UTF8);
    }

    /// <summary>Deserializes saved viewpoints from a Navisworks exchange XML string.</summary>
    public static List<NwcSavedViewpoint> Deserialize(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using var reader = new StringReader(xml);
        return Deserialize(reader);
    }

    /// <summary>Deserializes saved viewpoints from a <see cref="TextReader"/>.</summary>
    public static List<NwcSavedViewpoint> Deserialize(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var doc = XDocument.Load(reader);
        var result = new List<NwcSavedViewpoint>();

        var vpElements = doc.Descendants().Where(e => e.Name.LocalName.Equals("viewpoint", StringComparison.OrdinalIgnoreCase));
        foreach (var vpElem in vpElements)
        {
            var vp = DeserializeViewpoint(vpElem);
            if (vp is not null)
            {
                result.Add(vp);
            }
        }

        return result;
    }

    /// <summary>Deserializes saved viewpoints from a file.</summary>
    public static List<NwcSavedViewpoint> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Deserialize(reader);
    }

    private static XElement SerializeViewpoint(NwcSavedViewpoint vp)
    {
        var cam = vp.Camera;
        var camElem = new XElement("camera",
            new XAttribute("projection", cam.Projection == NwcCameraProjection.Orthographic ? "ortho" : "persp"),
            new XAttribute("fov", cam.FieldOfView.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("aspect", cam.AspectRatio.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("near", cam.NearDistance.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("far", cam.FarDistance.ToString("R", CultureInfo.InvariantCulture)),
            new XElement("position",
                new XElement("pos3f",
                    new XAttribute("x", cam.Position.X.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("y", cam.Position.Y.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("z", cam.Position.Z.ToString("R", CultureInfo.InvariantCulture)))),
            new XElement("rotation",
                new XElement("quaternion",
                    new XAttribute("a", cam.Rotation.X.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("b", cam.Rotation.Y.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("c", cam.Rotation.Z.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("d", cam.Rotation.W.ToString("R", CultureInfo.InvariantCulture)))));

        var vpElem = new XElement("viewpoint",
            new XAttribute("name", vp.Name),
            new XAttribute("guid", vp.Id.ToString()),
            camElem);

        // Section planes
        var allPlanes = new List<NwcSectionPlane>(vp.SectionPlanes);
        if (vp.SectionBox is not null && vp.SectionBox.IsEnabled)
        {
            allPlanes.AddRange(vp.SectionBox.ToSectionPlanes());
        }

        if (allPlanes.Count > 0)
        {
            var planesElem = new XElement("clip_planes");
            foreach (var plane in allPlanes)
            {
                planesElem.Add(new XElement("clip_plane",
                    new XAttribute("index", plane.PlaneIndex),
                    new XAttribute("enabled", plane.IsEnabled ? "1" : "0"),
                    new XAttribute("distance", plane.Distance.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("alignment", plane.Alignment.ToString().ToLowerInvariant()),
                    new XElement("plane",
                        new XElement("vec3f",
                            new XAttribute("x", plane.Normal.X.ToString("R", CultureInfo.InvariantCulture)),
                            new XAttribute("y", plane.Normal.Y.ToString("R", CultureInfo.InvariantCulture)),
                            new XAttribute("z", plane.Normal.Z.ToString("R", CultureInfo.InvariantCulture))))));
            }
            vpElem.Add(planesElem);
        }

        // Section box
        if (vp.SectionBox is not null)
        {
            var b = vp.SectionBox.Bounds;
            vpElem.Add(new XElement("section_box",
                new XAttribute("enabled", vp.SectionBox.IsEnabled ? "1" : "0"),
                new XElement("min",
                    new XAttribute("x", b.Min.X.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("y", b.Min.Y.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("z", b.Min.Z.ToString("R", CultureInfo.InvariantCulture))),
                new XElement("max",
                    new XAttribute("x", b.Max.X.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("y", b.Max.Y.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("z", b.Max.Z.ToString("R", CultureInfo.InvariantCulture)))));
        }

        // Redlines
        if (vp.Redlines.Count > 0)
        {
            var redlinesElem = new XElement("redlines");
            foreach (var r in vp.Redlines)
            {
                redlinesElem.Add(SerializeRedline(r));
            }
            vpElem.Add(redlinesElem);
        }

        // Comments
        if (vp.Comments.Count > 0)
        {
            var commentsElem = new XElement("comments");
            foreach (var c in vp.Comments)
            {
                commentsElem.Add(new XElement("comment", c));
            }
            vpElem.Add(commentsElem);
        }

        return vpElem;
    }

    private static XElement SerializeRedline(NwcRedline redline)
    {
        var authorAttr = redline.Author is not null ? new XAttribute("author", redline.Author) : null;
        return redline switch
        {
            NwcRedlineText rt => new XElement("redline_text",
                new XAttribute("guid", rt.Id.ToString()),
                authorAttr,
                new XAttribute("text", rt.Text),
                new XAttribute("size", rt.FontSize.ToString("R", CultureInfo.InvariantCulture)),
                new XElement("pos3f",
                    new XAttribute("x", rt.Position.X.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("y", rt.Position.Y.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("z", rt.Position.Z.ToString("R", CultureInfo.InvariantCulture))),
                SerializeColor(rt.Color)),

            NwcRedlineArrow ra => new XElement("redline_arrow",
                new XAttribute("guid", ra.Id.ToString()),
                authorAttr,
                new XElement("start",
                    new XElement("pos3f",
                        new XAttribute("x", ra.StartPoint.X.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("y", ra.StartPoint.Y.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("z", ra.StartPoint.Z.ToString("R", CultureInfo.InvariantCulture)))),
                new XElement("end",
                    new XElement("pos3f",
                        new XAttribute("x", ra.EndPoint.X.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("y", ra.EndPoint.Y.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("z", ra.EndPoint.Z.ToString("R", CultureInfo.InvariantCulture)))),
                SerializeColor(ra.Color)),

            NwcRedlineLine rl => new XElement("redline_line",
                new XAttribute("guid", rl.Id.ToString()),
                authorAttr,
                new XAttribute("arrow", rl.HasArrow ? "1" : "0"),
                new XElement("start",
                    new XElement("pos3f",
                        new XAttribute("x", rl.StartPoint.X.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("y", rl.StartPoint.Y.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("z", rl.StartPoint.Z.ToString("R", CultureInfo.InvariantCulture)))),
                new XElement("end",
                    new XElement("pos3f",
                        new XAttribute("x", rl.EndPoint.X.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("y", rl.EndPoint.Y.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("z", rl.EndPoint.Z.ToString("R", CultureInfo.InvariantCulture)))),
                SerializeColor(rl.Color)),

            NwcRedlineCloud rc => new XElement("redline_cloud",
                new XAttribute("guid", rc.Id.ToString()),
                authorAttr,
                new XAttribute("closed", rc.IsClosed ? "1" : "0"),
                new XElement("vertices",
                    rc.Vertices.Select(v => new XElement("pos3f",
                        new XAttribute("x", v.X.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("y", v.Y.ToString("R", CultureInfo.InvariantCulture)),
                        new XAttribute("z", v.Z.ToString("R", CultureInfo.InvariantCulture))))),
                SerializeColor(rc.Color)),

            NwcRedlineEllipse re => new XElement("redline_ellipse",
                new XAttribute("guid", re.Id.ToString()),
                authorAttr,
                new XAttribute("rx", re.RadiusX.ToString("R", CultureInfo.InvariantCulture)),
                new XAttribute("ry", re.RadiusY.ToString("R", CultureInfo.InvariantCulture)),
                new XElement("pos3f",
                    new XAttribute("x", re.Center.X.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("y", re.Center.Y.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("z", re.Center.Z.ToString("R", CultureInfo.InvariantCulture))),
                SerializeColor(re.Color)),

            NwcRedlineTag rtag => new XElement("redline_tag",
                new XAttribute("guid", rtag.Id.ToString()),
                authorAttr,
                new XAttribute("number", rtag.TagNumber),
                new XAttribute("status", rtag.Status.ToString().ToLowerInvariant()),
                new XElement("pos3f",
                    new XAttribute("x", rtag.Position.X.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("y", rtag.Position.Y.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("z", rtag.Position.Z.ToString("R", CultureInfo.InvariantCulture))),
                rtag.Comment is not null ? new XElement("comment", rtag.Comment) : null,
                SerializeColor(rtag.Color)),

            _ => new XElement("redline", new XAttribute("guid", redline.Id.ToString()), authorAttr)
        };
    }

    private static XElement SerializeColor(RgbaColor color)
    {
        return new XElement("color",
            new XAttribute("red", color.R.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("green", color.G.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("blue", color.B.ToString("R", CultureInfo.InvariantCulture)),
            new XAttribute("alpha", color.A.ToString("R", CultureInfo.InvariantCulture)));
    }

    private static NwcSavedViewpoint? DeserializeViewpoint(XElement vpElem)
    {
        string name = (string?)vpElem.Attribute("name") ?? "Viewpoint";
        string? guidStr = (string?)vpElem.Attribute("guid");
        var guid = Guid.TryParse(guidStr, out var g) ? g : Guid.NewGuid();

        var camElem = vpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("camera", StringComparison.OrdinalIgnoreCase));
        var camera = DeserializeCamera(camElem);

        var sectionPlanes = new List<NwcSectionPlane>();
        var clipPlanesElem = vpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("clip_planes", StringComparison.OrdinalIgnoreCase));
        if (clipPlanesElem is not null)
        {
            foreach (var cpElem in clipPlanesElem.Elements().Where(e => e.Name.LocalName.Equals("clip_plane", StringComparison.OrdinalIgnoreCase)))
            {
                int idx = int.TryParse((string?)cpElem.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pIdx) ? pIdx : 0;
                bool enabled = ((string?)cpElem.Attribute("enabled")) != "0";
                double dist = double.TryParse((string?)cpElem.Attribute("distance"), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0.0;
                string? alignStr = (string?)cpElem.Attribute("alignment");
                var align = Enum.TryParse<NwcSectionAlignment>(alignStr, true, out var al) ? al : NwcSectionAlignment.Custom;

                var vecElem = cpElem.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("vec3f", StringComparison.OrdinalIgnoreCase));
                var normal = ParseVec3f(vecElem, new Vector3d(0, 0, 1));

                sectionPlanes.Add(new NwcSectionPlane(idx, normal, dist, enabled, align));
            }
        }

        NwcSectionBox? sectionBox = null;
        var boxElem = vpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("section_box", StringComparison.OrdinalIgnoreCase));
        if (boxElem is not null)
        {
            bool enabled = ((string?)boxElem.Attribute("enabled")) != "0";
            var minElem = boxElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("min", StringComparison.OrdinalIgnoreCase));
            var maxElem = boxElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("max", StringComparison.OrdinalIgnoreCase));
            var min = ParseVec3f(minElem, Vector3d.Zero);
            var max = ParseVec3f(maxElem, Vector3d.Zero);
            sectionBox = new NwcSectionBox(new BoundingBox3d(min, max), enabled);
        }

        var redlines = new List<NwcRedline>();
        var redlinesElem = vpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("redlines", StringComparison.OrdinalIgnoreCase));
        if (redlinesElem is not null)
        {
            foreach (var rElem in redlinesElem.Elements())
            {
                var r = DeserializeRedline(rElem);
                if (r is not null) redlines.Add(r);
            }
        }

        var comments = new List<string>();
        var commentsElem = vpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("comments", StringComparison.OrdinalIgnoreCase));
        if (commentsElem is not null)
        {
            foreach (var cElem in commentsElem.Elements().Where(e => e.Name.LocalName.Equals("comment", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(cElem.Value))
                {
                    comments.Add(cElem.Value.Trim());
                }
            }
        }

        return new NwcSavedViewpoint
        {
            Id = guid,
            Name = name,
            Camera = camera,
            SectionPlanes = sectionPlanes,
            SectionBox = sectionBox,
            Redlines = redlines,
            Comments = comments,
        };
    }

    private static NwcCamera DeserializeCamera(XElement? camElem)
    {
        if (camElem is null)
        {
            return new NwcCamera { Position = new Vector3d(10, -10, 10), Rotation = Quaterniond.Identity };
        }

        string projStr = (string?)camElem.Attribute("projection") ?? "persp";
        var proj = projStr.StartsWith("ortho", StringComparison.OrdinalIgnoreCase)
            ? NwcCameraProjection.Orthographic
            : NwcCameraProjection.Perspective;

        double fov = double.TryParse((string?)camElem.Attribute("fov"), NumberStyles.Float, CultureInfo.InvariantCulture, out double f) ? f : 60.0;
        double aspect = double.TryParse((string?)camElem.Attribute("aspect"), NumberStyles.Float, CultureInfo.InvariantCulture, out double a) ? a : 1.333333;
        double near = double.TryParse((string?)camElem.Attribute("near"), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : 0.01;
        double far = double.TryParse((string?)camElem.Attribute("far"), NumberStyles.Float, CultureInfo.InvariantCulture, out double fa) ? fa : 1000.0;

        var posElem = camElem.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase));
        var pos = ParseVec3f(posElem, new Vector3d(10, -10, 10));

        var quatElem = camElem.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("quaternion", StringComparison.OrdinalIgnoreCase));
        double qx = double.TryParse((string?)quatElem?.Attribute("a"), NumberStyles.Float, CultureInfo.InvariantCulture, out double q1) ? q1 : 0.0;
        double qy = double.TryParse((string?)quatElem?.Attribute("b"), NumberStyles.Float, CultureInfo.InvariantCulture, out double q2) ? q2 : 0.0;
        double qz = double.TryParse((string?)quatElem?.Attribute("c"), NumberStyles.Float, CultureInfo.InvariantCulture, out double q3) ? q3 : 0.0;
        double qw = double.TryParse((string?)quatElem?.Attribute("d"), NumberStyles.Float, CultureInfo.InvariantCulture, out double q4) ? q4 : 1.0;

        return new NwcCamera
        {
            Position = pos,
            Rotation = new Quaterniond(qx, qy, qz, qw),
            Projection = proj,
            FieldOfView = fov,
            AspectRatio = aspect,
            NearDistance = near,
            FarDistance = far,
        };
    }

    private static NwcRedline? DeserializeRedline(XElement elem)
    {
        string tag = elem.Name.LocalName.ToLowerInvariant();
        string? guidStr = (string?)elem.Attribute("guid");
        var guid = Guid.TryParse(guidStr, out var g) ? g : Guid.NewGuid();
        string? author = (string?)elem.Attribute("author");
        var color = ParseColor(elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("color", StringComparison.OrdinalIgnoreCase)));

        if (tag == "redline_text")
        {
            string text = (string?)elem.Attribute("text") ?? string.Empty;
            double size = double.TryParse((string?)elem.Attribute("size"), NumberStyles.Float, CultureInfo.InvariantCulture, out double sz) ? sz : 12.0;
            var pos = ParseVec3f(elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)), Vector3d.Zero);
            return new NwcRedlineText(text, pos, size, color) { Id = guid, Author = author };
        }

        if (tag == "redline_arrow")
        {
            var startElem = elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("start", StringComparison.OrdinalIgnoreCase));
            var endElem = elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("end", StringComparison.OrdinalIgnoreCase));
            var p0 = ParseVec3f(startElem?.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)), Vector3d.Zero);
            var p1 = ParseVec3f(endElem?.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)), Vector3d.Zero);
            return new NwcRedlineArrow(p0, p1, color) { Id = guid, Author = author };
        }

        if (tag == "redline_line")
        {
            bool arrow = ((string?)elem.Attribute("arrow")) == "1";
            var startElem = elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("start", StringComparison.OrdinalIgnoreCase));
            var endElem = elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("end", StringComparison.OrdinalIgnoreCase));
            var p0 = ParseVec3f(startElem?.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)), Vector3d.Zero);
            var p1 = ParseVec3f(endElem?.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)), Vector3d.Zero);
            return new NwcRedlineLine(p0, p1, arrow, color) { Id = guid, Author = author };
        }

        if (tag == "redline_cloud")
        {
            bool closed = ((string?)elem.Attribute("closed")) != "0";
            var verts = new List<Vector3d>();
            var vElem = elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("vertices", StringComparison.OrdinalIgnoreCase));
            if (vElem is not null)
            {
                foreach (var ptElem in vElem.Elements().Where(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)))
                {
                    verts.Add(ParseVec3f(ptElem, Vector3d.Zero));
                }
            }
            return new NwcRedlineCloud(verts, closed, color) { Id = guid, Author = author };
        }

        if (tag == "redline_ellipse")
        {
            double rx = double.TryParse((string?)elem.Attribute("rx"), NumberStyles.Float, CultureInfo.InvariantCulture, out double r1) ? r1 : 1.0;
            double ry = double.TryParse((string?)elem.Attribute("ry"), NumberStyles.Float, CultureInfo.InvariantCulture, out double r2) ? r2 : 1.0;
            var pos = ParseVec3f(elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)), Vector3d.Zero);
            return new NwcRedlineEllipse(pos, rx, ry, color) { Id = guid, Author = author };
        }

        if (tag == "redline_tag")
        {
            int num = int.TryParse((string?)elem.Attribute("number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tn) ? tn : 1;
            string? statStr = (string?)elem.Attribute("status");
            var stat = Enum.TryParse<NwcRedlineStatus>(statStr, true, out var st) ? st : NwcRedlineStatus.New;
            var pos = ParseVec3f(elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase)), Vector3d.Zero);
            string? comment = (string?)elem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("comment", StringComparison.OrdinalIgnoreCase));
            return new NwcRedlineTag(num, pos, comment, stat) { Id = guid, Color = color, Author = author };
        }

        return null;
    }

    private static Vector3d ParseVec3f(XElement? elem, Vector3d defaultValue)
    {
        if (elem is null) return defaultValue;
        double x = double.TryParse((string?)elem.Attribute("x"), NumberStyles.Float, CultureInfo.InvariantCulture, out double px) ? px : defaultValue.X;
        double y = double.TryParse((string?)elem.Attribute("y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double py) ? py : defaultValue.Y;
        double z = double.TryParse((string?)elem.Attribute("z"), NumberStyles.Float, CultureInfo.InvariantCulture, out double pz) ? pz : defaultValue.Z;
        return new Vector3d(x, y, z);
    }

    private static RgbaColor ParseColor(XElement? elem)
    {
        if (elem is null) return new RgbaColor(1f, 0f, 0f, 1f);
        float r = float.TryParse((string?)elem.Attribute("red"), NumberStyles.Float, CultureInfo.InvariantCulture, out float cr) ? cr : 1f;
        float g = float.TryParse((string?)elem.Attribute("green"), NumberStyles.Float, CultureInfo.InvariantCulture, out float cg) ? cg : 0f;
        float b = float.TryParse((string?)elem.Attribute("blue"), NumberStyles.Float, CultureInfo.InvariantCulture, out float cb) ? cb : 0f;
        float a = float.TryParse((string?)elem.Attribute("alpha"), NumberStyles.Float, CultureInfo.InvariantCulture, out float ca) ? ca : 1f;
        return new RgbaColor(r, g, b, a);
    }
}
