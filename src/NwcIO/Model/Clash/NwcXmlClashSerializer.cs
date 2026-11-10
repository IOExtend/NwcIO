using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace NwcIO.Model.Clash;

/// <summary>
/// Serializes and deserializes Autodesk Navisworks Clash Detective reports and batch tests
/// according to the official XML schema (nw-clashreport-11.0.xsd).
/// </summary>
public static class NwcXmlClashSerializer
{
    private static readonly char[] PathSeparators = ['/', '\\'];

    private static readonly XmlWriterSettings Settings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(false),
        OmitXmlDeclaration = false,
    };

    /// <summary>
    /// Saves a collection of clash tests as a Navisworks Clash Report XML file.
    /// </summary>
    public static void Save(string path, IEnumerable<NwcClashTest> tests, string batchName = "BatchTest")
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(tests);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(path);
        Write(fs, tests, batchName);
    }

    /// <summary>
    /// Writes clash tests into a stream as a Navisworks Clash Report XML document.
    /// </summary>
    public static void Write(Stream stream, IEnumerable<NwcClashTest> tests, string batchName = "BatchTest")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(tests);

        using var writer = XmlWriter.Create(stream, Settings);
        WriteDocument(writer, tests, batchName);
    }

    /// <summary>
    /// Writes clash tests to an <see cref="XmlWriter"/>.
    /// </summary>
    public static void WriteDocument(XmlWriter xml, IEnumerable<NwcClashTest> tests, string batchName = "BatchTest")
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(tests);

        xml.WriteStartDocument();
        xml.WriteStartElement("exchange");
        xml.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        xml.WriteAttributeString("xsi", "noNamespaceSchemaLocation", null, "http://download.autodesk.com/us/navisworks/schemas/nw-clashreport-11.0.xsd");

        xml.WriteStartElement("batchtest");
        xml.WriteAttributeString("name", batchName);

        xml.WriteStartElement("classtests");
        foreach (var test in tests)
        {
            WriteClashTest(xml, test);
        }
        xml.WriteEndElement(); // classtests

        xml.WriteEndElement(); // batchtest
        xml.WriteEndElement(); // exchange
        xml.WriteEndDocument();
    }

    private static void WriteClashTest(XmlWriter xml, NwcClashTest test)
    {
        xml.WriteStartElement("classtest");
        xml.WriteAttributeString("name", test.Name);
        xml.WriteAttributeString("testtype", test.TestType.ToString().ToLowerInvariant());
        xml.WriteAttributeString("status", test.Status);
        xml.WriteAttributeString("tolerance", test.Tolerance.ToString("0.######", CultureInfo.InvariantCulture));

        xml.WriteStartElement("linkage");
        xml.WriteAttributeString("mode", "none");
        xml.WriteEndElement();

        // Rules
        if (test.Rules.Count > 0)
        {
            xml.WriteStartElement("rules");
            foreach (var rule in test.Rules)
            {
                xml.WriteStartElement("rule");
                xml.WriteAttributeString("name", rule.Name);
                xml.WriteAttributeString("enabled", rule.Enabled ? "1" : "0");
                xml.WriteEndElement();
            }
            xml.WriteEndElement(); // rules
        }

        // Summary
        xml.WriteStartElement("summary");
        xml.WriteAttributeString("total", test.TotalCount.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("new", test.NewCount.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("active", test.ActiveCount.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("reviewed", test.ReviewedCount.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("approved", test.ApprovedCount.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("resolved", test.ResolvedCount.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement(); // summary

        // Results
        xml.WriteStartElement("clashresults");
        foreach (var result in test.Results)
        {
            WriteClashResult(xml, result);
        }
        xml.WriteEndElement(); // clashresults

        xml.WriteEndElement(); // classtest
    }

    private static void WriteClashResult(XmlWriter xml, NwcClashResult result)
    {
        xml.WriteStartElement("clashresult");
        xml.WriteAttributeString("name", result.Name);
        xml.WriteAttributeString("status", result.Status.ToString().ToLowerInvariant());
        xml.WriteAttributeString("distance", result.Distance.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteAttributeString("resultstatus", result.Status.ToString().ToLowerInvariant());

        if (!string.IsNullOrEmpty(result.Description))
        {
            xml.WriteElementString("description", result.Description);
        }

        xml.WriteElementString("resultstatus", result.Status.ToString().ToLowerInvariant());

        // Clash Point
        xml.WriteStartElement("clashpoint");
        xml.WriteStartElement("pos3f");
        xml.WriteAttributeString("x", result.ClashPoint.X.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteAttributeString("y", result.ClashPoint.Y.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteAttributeString("z", result.ClashPoint.Z.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteEndElement(); // pos3f
        xml.WriteEndElement(); // clashpoint

        if (!string.IsNullOrEmpty(result.GridLocation))
        {
            xml.WriteElementString("gridlocation", result.GridLocation);
        }

        // Created Date
        xml.WriteStartElement("createddate");
        xml.WriteStartElement("date");
        xml.WriteAttributeString("year", result.CreatedDate.Year.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("month", result.CreatedDate.Month.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("day", result.CreatedDate.Day.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("hour", result.CreatedDate.Hour.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("minute", result.CreatedDate.Minute.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("second", result.CreatedDate.Second.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement(); // date
        xml.WriteEndElement(); // createddate

        if (!string.IsNullOrEmpty(result.AssignedTo))
        {
            xml.WriteElementString("assignedto", result.AssignedTo);
        }
        if (!string.IsNullOrEmpty(result.ApprovedBy))
        {
            xml.WriteElementString("approvedby", result.ApprovedBy);
        }

        // Clash Objects
        xml.WriteStartElement("clashobjects");
        WriteClashObject(xml, result.Item1);
        WriteClashObject(xml, result.Item2);
        xml.WriteEndElement(); // clashobjects

        // Comments
        if (result.Comments.Count > 0)
        {
            xml.WriteStartElement("comments");
            foreach (var comment in result.Comments)
            {
                xml.WriteStartElement("comment");
                xml.WriteAttributeString("id", comment.Id.ToString(CultureInfo.InvariantCulture));
                xml.WriteElementString("user", comment.Author);
                xml.WriteElementString("body", comment.Body);
                xml.WriteEndElement(); // comment
            }
            xml.WriteEndElement(); // comments
        }

        xml.WriteEndElement(); // clashresult
    }

    private static void WriteClashObject(XmlWriter xml, NwcClashObject obj)
    {
        xml.WriteStartElement("clashobject");

        if (obj.ElementId.HasValue)
        {
            xml.WriteStartElement("objectattribute");
            xml.WriteElementString("name", "Element ID");
            xml.WriteElementString("value", obj.ElementId.Value.ToString(CultureInfo.InvariantCulture));
            xml.WriteEndElement();
        }

        if (!string.IsNullOrEmpty(obj.Layer))
        {
            xml.WriteElementString("layer", obj.Layer);
        }

        xml.WriteStartElement("pathlink");
        string[] parts = obj.Path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            foreach (var p in parts)
            {
                xml.WriteElementString("node", p);
            }
        }
        else
        {
            xml.WriteElementString("node", obj.Name);
        }
        xml.WriteEndElement(); // pathlink

        if (!string.IsNullOrEmpty(obj.IfcGuid))
        {
            xml.WriteStartElement("smartpath");
            xml.WriteElementString("guid", obj.IfcGuid);
            xml.WriteEndElement(); // smartpath
        }

        xml.WriteEndElement(); // clashobject
    }

    /// <summary>
    /// Reads a Navisworks Clash Report XML file from disk.
    /// </summary>
    public static List<NwcClashTest> Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var fs = File.OpenRead(path);
        return Read(fs);
    }

    /// <summary>
    /// Reads a Navisworks Clash Report from a stream.
    /// </summary>
    public static List<NwcClashTest> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = XDocument.Load(stream);
        return ParseDocument(doc);
    }

    /// <summary>
    /// Parses an <see cref="XDocument"/> representing a Navisworks Clash Report.
    /// </summary>
    public static List<NwcClashTest> ParseDocument(XDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var tests = new List<NwcClashTest>();

        var testElements = doc.Descendants().Where(e => e.Name.LocalName.Equals("classtest", StringComparison.OrdinalIgnoreCase));
        foreach (var testElem in testElements)
        {
            string name = (string?)testElem.Attribute("name") ?? "Clash Test";
            string testTypeStr = (string?)testElem.Attribute("testtype") ?? "hard";
            _ = Enum.TryParse<NwcClashTestType>(testTypeStr, ignoreCase: true, out var testType);

            double tolerance = 0.001;
            if (double.TryParse((string?)testElem.Attribute("tolerance"), NumberStyles.Float, CultureInfo.InvariantCulture, out double tol))
            {
                tolerance = tol;
            }

            var test = new NwcClashTest(name, testType, tolerance)
            {
                Status = (string?)testElem.Attribute("status") ?? "active",
            };

            // Rules
            var rulesElem = testElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("rules", StringComparison.OrdinalIgnoreCase));
            if (rulesElem is not null)
            {
                foreach (var rElem in rulesElem.Elements().Where(e => e.Name.LocalName.Equals("rule", StringComparison.OrdinalIgnoreCase)))
                {
                    string rName = (string?)rElem.Attribute("name") ?? string.Empty;
                    bool enabled = ((string?)rElem.Attribute("enabled")) == "1";
                    test.Rules.Add(new NwcClashRule(rName, NwcClashRuleType.Custom, enabled));
                }
            }

            // Results
            var resultsElem = testElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("clashresults", StringComparison.OrdinalIgnoreCase));
            if (resultsElem is not null)
            {
                foreach (var resElem in resultsElem.Elements().Where(e => e.Name.LocalName.Equals("clashresult", StringComparison.OrdinalIgnoreCase)))
                {
                    var result = ParseClashResult(resElem);
                    if (result is not null)
                    {
                        test.AddResult(result);
                    }
                }
            }

            tests.Add(test);
        }

        return tests;
    }

    private static NwcClashResult? ParseClashResult(XElement resElem)
    {
        string name = (string?)resElem.Attribute("name") ?? "Clash";
        string statusStr = (string?)resElem.Attribute("status") ?? "new";
        _ = Enum.TryParse<NwcClashStatus>(statusStr, ignoreCase: true, out var status);

        double distance = 0.0;
        if (double.TryParse((string?)resElem.Attribute("distance"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dist))
        {
            distance = dist;
        }

        var result = new NwcClashResult
        {
            Name = name,
            Status = status,
            Distance = distance,
            Description = (string?)resElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase)),
            GridLocation = (string?)resElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("gridlocation", StringComparison.OrdinalIgnoreCase)),
            AssignedTo = (string?)resElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("assignedto", StringComparison.OrdinalIgnoreCase)),
            ApprovedBy = (string?)resElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("approvedby", StringComparison.OrdinalIgnoreCase)),
        };

        // Clash Point
        var ptElem = resElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("clashpoint", StringComparison.OrdinalIgnoreCase));
        var posElem = ptElem?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pos3f", StringComparison.OrdinalIgnoreCase));
        if (posElem is not null &&
            double.TryParse((string?)posElem.Attribute("x"), NumberStyles.Float, CultureInfo.InvariantCulture, out double px) &&
            double.TryParse((string?)posElem.Attribute("y"), NumberStyles.Float, CultureInfo.InvariantCulture, out double py) &&
            double.TryParse((string?)posElem.Attribute("z"), NumberStyles.Float, CultureInfo.InvariantCulture, out double pz))
        {
            result.ClashPoint = new Vector3d(px, py, pz);
        }

        // Clash Objects
        var objsElem = resElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("clashobjects", StringComparison.OrdinalIgnoreCase));
        if (objsElem is not null)
        {
            var objList = objsElem.Elements().Where(e => e.Name.LocalName.Equals("clashobject", StringComparison.OrdinalIgnoreCase)).ToList();
            if (objList.Count > 0) result.Item1 = ParseClashObject(objList[0]);
            if (objList.Count > 1) result.Item2 = ParseClashObject(objList[1]);
        }

        // Comments
        var commentsElem = resElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("comments", StringComparison.OrdinalIgnoreCase));
        if (commentsElem is not null)
        {
            foreach (var cElem in commentsElem.Elements().Where(e => e.Name.LocalName.Equals("comment", StringComparison.OrdinalIgnoreCase)))
            {
                string user = (string?)cElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("user", StringComparison.OrdinalIgnoreCase)) ?? "User";
                string body = (string?)cElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
                result.Comments.Add(new NwcClashComment(body, user, status));
            }
        }

        return result;
    }

    private static NwcClashObject ParseClashObject(XElement objElem)
    {
        var obj = new NwcClashObject();

        obj.Layer = (string?)objElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("layer", StringComparison.OrdinalIgnoreCase));

        var attrElem = objElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("objectattribute", StringComparison.OrdinalIgnoreCase));
        if (attrElem is not null)
        {
            string? val = (string?)attrElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase));
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                obj.ElementId = id;
            }
        }

        var pathElem = objElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("pathlink", StringComparison.OrdinalIgnoreCase));
        if (pathElem is not null)
        {
            var nodes = pathElem.Elements().Where(e => e.Name.LocalName.Equals("node", StringComparison.OrdinalIgnoreCase)).Select(e => e.Value).ToList();
            if (nodes.Count > 0)
            {
                obj.Path = string.Join("/", nodes);
                obj.Name = nodes[^1];
            }
        }

        var smartElem = objElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("smartpath", StringComparison.OrdinalIgnoreCase));
        if (smartElem is not null)
        {
            obj.IfcGuid = (string?)smartElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("guid", StringComparison.OrdinalIgnoreCase));
        }

        return obj;
    }
}
