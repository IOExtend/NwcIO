using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NwcIO.Model.Clash;

namespace NwcIO.Model.Grids;

/// <summary>
/// A spatial grid system containing reference axes and lines (e.g. A, B, C / 1, 2, 3),
/// capable of computing intersections, locating 3D coordinates relative to grids,
/// and populating clash grid location tags.
/// </summary>
public sealed class NwcGridSystem
{
    public string Name { get; set; } = "Grids";

    public List<NwcGridLine> Lines { get; } = [];

    private List<NwcGridIntersection>? _cachedIntersections;

    public NwcGridSystem() { }

    public NwcGridSystem(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Adds a grid line to the system.
    /// </summary>
    public NwcGridLine AddLine(NwcGridLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Lines.Add(line);
        _cachedIntersections = null;
        return line;
    }

    /// <summary>
    /// Adds a grid line to the system by name and endpoints.
    /// </summary>
    public NwcGridLine AddLine(string name, Vector3d start, Vector3d end, double elevation = 0.0, string? systemName = null)
    {
        var line = new NwcGridLine(name, start, end, elevation, systemName ?? Name);
        return AddLine(line);
    }

    /// <summary>
    /// Calculates all 2D/3D intersection points between crossing grid lines in this system.
    /// </summary>
    public IReadOnlyList<NwcGridIntersection> GetIntersections()
    {
        if (_cachedIntersections is not null)
        {
            return _cachedIntersections;
        }

        var result = new List<NwcGridIntersection>();

        for (int i = 0; i < Lines.Count; i++)
        {
            for (int j = i + 1; j < Lines.Count; j++)
            {
                var l1 = Lines[i];
                var l2 = Lines[j];

                if (l1.Intersect2D(l2, out var pt))
                {
                    result.Add(new NwcGridIntersection
                    {
                        Line1Name = l1.Name,
                        Line2Name = l2.Name,
                        IntersectionPoint = pt,
                        SystemName = l1.SystemName ?? Name,
                    });
                }
            }
        }

        _cachedIntersections = result;
        return result;
    }

    /// <summary>
    /// Finds the nearest grid line to the specified 3D point.
    /// </summary>
    public NwcGridLine? FindNearestGrid(Vector3d point, out double distance, bool segmentOnly = false)
    {
        if (Lines.Count == 0)
        {
            distance = double.PositiveInfinity;
            return null;
        }

        NwcGridLine? best = null;
        double minDst = double.PositiveInfinity;

        for (int i = 0; i < Lines.Count; i++)
        {
            double d = Lines[i].DistanceToPoint2D(point, segmentOnly);
            if (d < minDst)
            {
                minDst = d;
                best = Lines[i];
            }
        }

        distance = minDst;
        return best;
    }

    /// <summary>
    /// Finds the nearest grid intersection (e.g. "A / 1") to the specified 3D point.
    /// </summary>
    public NwcGridIntersection? FindNearestIntersection(Vector3d point, out double distance)
    {
        var intersections = GetIntersections();
        if (intersections.Count == 0)
        {
            distance = double.PositiveInfinity;
            return null;
        }

        NwcGridIntersection? best = null;
        double minDst = double.PositiveInfinity;

        for (int i = 0; i < intersections.Count; i++)
        {
            double d = intersections[i].DistanceTo2D(point);
            if (d < minDst)
            {
                minDst = d;
                best = intersections[i];
            }
        }

        distance = minDst;
        return best;
    }

    /// <summary>
    /// Formats a human-readable grid location descriptor for a 3D coordinate (e.g. "A / 1", "A (+1.20m) / 2 (-0.50m)").
    /// </summary>
    public string FormatLocation(Vector3d point, double snapTolerance = 0.05)
    {
        if (Lines.Count == 0) return string.Empty;

        var inter = FindNearestIntersection(point, out double interDist);
        if (inter is not null)
        {
            var l1 = Lines.FirstOrDefault(l => string.Equals(l.Name, inter.Line1Name, StringComparison.OrdinalIgnoreCase));
            var l2 = Lines.FirstOrDefault(l => string.Equals(l.Name, inter.Line2Name, StringComparison.OrdinalIgnoreCase));

            if (l1 is not null && l2 is not null)
            {
                double off1 = l1.SignedDistance2D(point);
                double off2 = l2.SignedDistance2D(point);

                bool snap1 = Math.Abs(off1) <= snapTolerance;
                bool snap2 = Math.Abs(off2) <= snapTolerance;

                if (snap1 && snap2)
                {
                    return $"{l1.Name} / {l2.Name}";
                }

                string part1 = snap1 ? l1.Name : string.Create(CultureInfo.InvariantCulture, $"{l1.Name} ({off1:+0.00;-0.00;0.00}m)");
                string part2 = snap2 ? l2.Name : string.Create(CultureInfo.InvariantCulture, $"{l2.Name} ({off2:+0.00;-0.00;0.00}m)");
                return $"{part1} / {part2}";
            }

            return inter.DisplayName;
        }

        var nearestLine = FindNearestGrid(point, out double lineDist);
        if (nearestLine is not null)
        {
            double off = nearestLine.SignedDistance2D(point);
            if (Math.Abs(off) <= snapTolerance)
            {
                return $"Grid {nearestLine.Name}";
            }
            return string.Create(CultureInfo.InvariantCulture, $"Grid {nearestLine.Name} ({off:+0.00;-0.00;0.00}m)");
        }

        return string.Empty;
    }

    /// <summary>
    /// Automatically populates <see cref="NwcClashResult.GridLocation"/> for all clash results in a test.
    /// </summary>
    public int AssignGridLocations(NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        int count = 0;

        foreach (var clash in test.Results)
        {
            clash.GridLocation = FormatLocation(clash.ClashPoint);
            count++;
        }

        foreach (var group in test.Groups)
        {
            group.GridLocation = FormatLocation(group.ClashPoint);
            foreach (var c in group.Clashes)
            {
                c.GridLocation = FormatLocation(c.ClashPoint);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Populates <see cref="NwcClashResult.GridLocation"/> across all clash tests in a document.
    /// </summary>
    public int AssignGridLocations(NwcDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        int count = 0;
        foreach (var test in doc.ClashTests)
        {
            count += AssignGridLocations(test);
        }
        return count;
    }

    /// <summary>
    /// Automatically extracts grid lines from scene graph nodes if present.
    /// </summary>
    public static NwcGridSystem ExtractFromDocument(NwcDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var system = new NwcGridSystem("Model Grids");

        if (doc.RootNode is null) return system;

        void Walk(NwcNode node)
        {
            bool isGridNode = node.Class.Contains("Grid", StringComparison.OrdinalIgnoreCase) ||
                              node.Name.StartsWith("Grid", StringComparison.OrdinalIgnoreCase);

            if (isGridNode)
            {
                string name = node.Name;
                if (name.StartsWith("Grid", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(4).Trim(' ', ':', '-');
                }

                if (node.Mesh is not null && node.Mesh.Vertices.Count >= 2)
                {
                    var worldVerts = node.Mesh.GetWorldVertices();
                    if (worldVerts.Count >= 2)
                    {
                        var start = worldVerts[0];
                        var end = worldVerts[^1];
                        system.AddLine(name, start, end, (start.Z + end.Z) * 0.5);
                    }
                }
                else if (node.Transform is not null)
                {
                    var pos = node.Transform.Translation;
                    // Approximate axis line if bounds are known
                    var bounds = node.Transform.BoundingBox;
                    if (bounds.HasValue && !bounds.Value.IsEmpty)
                    {
                        system.AddLine(name, bounds.Value.Min, bounds.Value.Max, pos.Z);
                    }
                }
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                Walk(node.Children[i]);
            }
        }

        Walk(doc.RootNode);
        return system;
    }

    /// <summary>
    /// Saves the grid system to an XML file.
    /// </summary>
    public void SaveXml(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        SaveXml(fs);
    }

    /// <summary>
    /// Saves the grid system to an XML stream.
    /// </summary>
    public void SaveXml(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        };

        using var xml = XmlWriter.Create(stream, settings);
        xml.WriteStartDocument();
        xml.WriteStartElement("exchange");
        xml.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

        xml.WriteStartElement("grids");
        xml.WriteStartElement("grid_system");
        xml.WriteAttributeString("name", Name);

        foreach (var line in Lines)
        {
            xml.WriteStartElement("grid_line");
            xml.WriteAttributeString("name", line.Name);
            xml.WriteAttributeString("elevation", line.Elevation.ToString("F4", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(line.SystemName))
            {
                xml.WriteAttributeString("system", line.SystemName);
            }

            xml.WriteStartElement("start");
            xml.WriteAttributeString("x", line.Start.X.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("y", line.Start.Y.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("z", line.Start.Z.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteEndElement(); // start

            xml.WriteStartElement("end");
            xml.WriteAttributeString("x", line.End.X.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("y", line.End.Y.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteAttributeString("z", line.End.Z.ToString("F4", CultureInfo.InvariantCulture));
            xml.WriteEndElement(); // end

            xml.WriteEndElement(); // grid_line
        }

        xml.WriteEndElement(); // grid_system
        xml.WriteEndElement(); // grids
        xml.WriteEndElement(); // exchange
        xml.WriteEndDocument();
    }

    /// <summary>
    /// Loads a grid system from an XML file.
    /// </summary>
    public static NwcGridSystem LoadXml(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.OpenRead(path);
        return ReadXml(fs);
    }

    /// <summary>
    /// Loads a grid system from an XML stream.
    /// </summary>
    public static NwcGridSystem ReadXml(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = XDocument.Load(stream);

        var sysElem = doc.Descendants("grid_system").FirstOrDefault();
        string sysName = sysElem?.Attribute("name")?.Value ?? "Grids";

        var system = new NwcGridSystem(sysName);

        var lineElems = doc.Descendants("grid_line");
        foreach (var elem in lineElems)
        {
            string name = elem.Attribute("name")?.Value ?? "Grid";
            double elevation = 0.0;
            if (double.TryParse(elem.Attribute("elevation")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double elevVal))
            {
                elevation = elevVal;
            }

            string? lineSys = elem.Attribute("system")?.Value;

            var startElem = elem.Element("start");
            var endElem = elem.Element("end");

            Vector3d start = Vector3d.Zero;
            Vector3d end = Vector3d.Zero;

            if (startElem is not null)
            {
                double.TryParse(startElem.Attribute("x")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                double.TryParse(startElem.Attribute("y")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                double.TryParse(startElem.Attribute("z")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z);
                start = new Vector3d(x, y, z);
            }

            if (endElem is not null)
            {
                double.TryParse(endElem.Attribute("x")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double x);
                double.TryParse(endElem.Attribute("y")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double y);
                double.TryParse(endElem.Attribute("z")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double z);
                end = new Vector3d(x, y, z);
            }

            system.AddLine(new NwcGridLine(name, start, end, elevation, lineSys));
        }

        return system;
    }
}
