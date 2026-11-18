using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using NwcIO.Model.Sets;

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
        xml.WriteAttributeString("guid", test.TestGuid.ToString("D"));
        xml.WriteAttributeString("testtype", test.TestType.ToString().ToLowerInvariant());
        xml.WriteAttributeString("status", test.Status);
        xml.WriteAttributeString("tolerance", test.Tolerance.ToString("0.######", CultureInfo.InvariantCulture));

        xml.WriteStartElement("linkage");
        xml.WriteAttributeString("mode", "none");
        xml.WriteEndElement();

        WriteSelection(xml, "selectiona", test.SelectionA);
        WriteSelection(xml, "selectionb", test.SelectionB);

        // Rules
        if (test.Rules.Count > 0)
        {
            xml.WriteStartElement("rules");
            foreach (var rule in test.Rules)
            {
                xml.WriteStartElement("rule");
                xml.WriteAttributeString("name", rule.Name);
                xml.WriteAttributeString("enabled", rule.Enabled ? "1" : "0");
                xml.WriteAttributeString("type", rule.RuleType.ToString());
                xml.WriteAttributeString("threshold", rule.Threshold.ToString("R", CultureInfo.InvariantCulture));
                if (rule.SetA is not null)
                {
                    xml.WriteAttributeString("seta", rule.SetA);
                }
                if (rule.SetB is not null)
                {
                    xml.WriteAttributeString("setb", rule.SetB);
                }
                if (rule.PropertyName is not null)
                {
                    xml.WriteAttributeString("propertyname", rule.PropertyName);
                }
                if (rule.PropertyValue is not null)
                {
                    xml.WriteAttributeString("propertyvalue", rule.PropertyValue);
                }
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

        // Results and Groups
        xml.WriteStartElement("clashresults");
        foreach (var group in test.Groups)
        {
            WriteClashGroup(xml, group);
        }
        foreach (var result in test.Results)
        {
            WriteClashResult(xml, result);
        }
        xml.WriteEndElement(); // clashresults

        xml.WriteEndElement(); // classtest
    }

    private static void WriteClashGroup(XmlWriter xml, NwcClashGroup group)
    {
        xml.WriteStartElement("clashgroup");
        xml.WriteAttributeString("name", group.Name);
        xml.WriteAttributeString("guid", group.GroupGuid.ToString("D"));
        xml.WriteAttributeString("status", group.Status.ToString().ToLowerInvariant());
        xml.WriteAttributeString("distance", group.Distance.ToString("0.######", CultureInfo.InvariantCulture));

        if (!string.IsNullOrEmpty(group.Description))
        {
            xml.WriteElementString("description", group.Description);
        }

        WriteDate(xml, "createddate", group.CreatedDate);
        if (group.AssignedTo is not null)
        {
            xml.WriteElementString("assignedto", group.AssignedTo);
        }
        if (group.ApprovedBy is not null)
        {
            xml.WriteElementString("approvedby", group.ApprovedBy);
        }
        if (group.GridLocation is not null)
        {
            xml.WriteElementString("gridlocation", group.GridLocation);
        }
        WriteComments(xml, group.Comments);

        var pt = group.ClashPoint;
        xml.WriteStartElement("clashpoint");
        xml.WriteStartElement("pos3f");
        xml.WriteAttributeString("x", pt.X.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteAttributeString("y", pt.Y.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteAttributeString("z", pt.Z.ToString("0.######", CultureInfo.InvariantCulture));
        xml.WriteEndElement(); // pos3f
        xml.WriteEndElement(); // clashpoint

        xml.WriteStartElement("clashresults");
        foreach (var clash in group.Clashes)
        {
            WriteClashResult(xml, clash);
        }
        xml.WriteEndElement(); // clashresults

        xml.WriteEndElement(); // clashgroup
    }

    private static void WriteClashResult(XmlWriter xml, NwcClashResult result)
    {
        xml.WriteStartElement("clashresult");
        xml.WriteAttributeString("name", result.Name);
        xml.WriteAttributeString("guid", result.ResultGuid.ToString("D"));
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

        WriteDate(xml, "createddate", result.CreatedDate);

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

        WriteComments(xml, result.Comments);

        xml.WriteEndElement(); // clashresult
    }

    private static XElement? Child(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Guid ParseGuid(XElement element, Guid fallback) =>
        Guid.TryParse((string?)element.Attribute("guid"), out var guid) ? guid : fallback;

    private static void WriteDate(XmlWriter xml, string name, DateTimeOffset date)
    {
        if (name != "date")
        {
            xml.WriteStartElement(name);
        }
        xml.WriteStartElement("date");
        // Keep legacy components, with an exact value for offsets and fractional seconds.
        xml.WriteAttributeString("value", date.ToString("O", CultureInfo.InvariantCulture));
        xml.WriteAttributeString("year", date.Year.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("month", date.Month.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("day", date.Day.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("hour", date.Hour.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("minute", date.Minute.ToString(CultureInfo.InvariantCulture));
        xml.WriteAttributeString("second", date.Second.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement();
        if (name != "date")
        {
            xml.WriteEndElement();
        }
    }

    private static DateTimeOffset ParseDate(XElement? element, DateTimeOffset fallback)
    {
        var date = element is null ? null : Child(element, "date") ?? element;
        if (date is null)
        {
            return fallback;
        }
        if (DateTimeOffset.TryParse((string?)date.Attribute("value"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return value;
        }
        var parts = new int[6];
        var names = new[] { "year", "month", "day", "hour", "minute", "second" };
        for (var i = 0; i < names.Length; i++)
        {
            if (!int.TryParse((string?)date.Attribute(names[i]), NumberStyles.Integer, CultureInfo.InvariantCulture, out parts[i]))
            {
                return fallback;
            }
        }
        try
        {
            return new DateTimeOffset(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback;
        }
    }

    private static void WriteComments(XmlWriter xml, List<NwcClashComment> comments)
    {
        if (comments.Count == 0)
        {
            return;
        }
        xml.WriteStartElement("comments");
        foreach (var comment in comments)
        {
            xml.WriteStartElement("comment");
            xml.WriteAttributeString("id", comment.Id.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("status", comment.Status.ToString().ToLowerInvariant());
            xml.WriteElementString("user", comment.Author);
            xml.WriteElementString("body", comment.Body);
            WriteDate(xml, "date", comment.Date);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static IEnumerable<NwcClashComment> ParseComments(XElement element, NwcClashStatus fallbackStatus)
    {
        var comments = Child(element, "comments");
        if (comments is null)
        {
            yield break;
        }
        foreach (var child in comments.Elements().Where(e => e.Name.LocalName.Equals("comment", StringComparison.OrdinalIgnoreCase)))
        {
            var comment = new NwcClashComment((string?)Child(child, "body") ?? string.Empty,
                (string?)Child(child, "user") ?? "User", fallbackStatus);
            if (int.TryParse((string?)child.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                comment.Id = id;
            }
            if (Enum.TryParse<NwcClashStatus>((string?)child.Attribute("status"), true, out var status) && Enum.IsDefined(status))
            {
                comment.Status = status;
            }
            comment.Date = ParseDate(Child(child, "date"), comment.Date);
            yield return comment;
        }
    }

    private static void WriteSelection(XmlWriter xml, string name, NwcClashSelection selection)
    {
        xml.WriteStartElement(name);
        xml.WriteAttributeString("name", selection.Name);
        foreach (var set in selection.SetNames)
        {
            xml.WriteElementString("set", set);
        }
        foreach (var id in selection.ExplicitNodeIds)
        {
            xml.WriteElementString("nodeid", id.ToString(CultureInfo.InvariantCulture));
        }
        if (selection.FilterCondition is { } condition)
        {
            xml.WriteStartElement("condition");
            xml.WriteAttributeString("category", condition.Category);
            xml.WriteAttributeString("property", condition.Property);
            xml.WriteAttributeString("operator", condition.Operator.ToString());
            xml.WriteString(condition.Value);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static NwcClashSelection ParseSelection(XElement? element)
    {
        if (element is null)
        {
            return NwcClashSelection.All();
        }
        var selection = new NwcClashSelection((string?)element.Attribute("name") ?? "All");
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName.Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                selection.SetNames.Add(child.Value);
            }
            else if (child.Name.LocalName.Equals("nodeid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(child.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                selection.ExplicitNodeIds.Add(id);
            }
        }
        if (Child(element, "condition") is { } condition)
        {
            _ = Enum.TryParse<NwcSearchOperator>((string?)condition.Attribute("operator"), true, out var op);
            selection.FilterCondition = new NwcSearchCondition((string?)condition.Attribute("category") ?? "Item",
                (string?)condition.Attribute("property") ?? "Name", op, condition.Value);
        }
        return selection;
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

            test.TestGuid = ParseGuid(testElem, test.TestGuid);
            test.SelectionA = ParseSelection(Child(testElem, "selectiona"));
            test.SelectionB = ParseSelection(Child(testElem, "selectionb"));

            // Rules
            var rulesElem = testElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("rules", StringComparison.OrdinalIgnoreCase));
            if (rulesElem is not null)
            {
                foreach (var rElem in rulesElem.Elements().Where(e => e.Name.LocalName.Equals("rule", StringComparison.OrdinalIgnoreCase)))
                {
                    string rName = (string?)rElem.Attribute("name") ?? string.Empty;
                    bool enabled = ((string?)rElem.Attribute("enabled")) == "1";
                    var type = Enum.TryParse<NwcClashRuleType>((string?)rElem.Attribute("type"), true, out var parsedType) && Enum.IsDefined(parsedType)
                        ? parsedType : NwcClashRuleType.Custom;
                    var rule = new NwcClashRule(rName, type, enabled)
                    {
                        SetA = (string?)rElem.Attribute("seta"),
                        SetB = (string?)rElem.Attribute("setb"),
                        PropertyName = (string?)rElem.Attribute("propertyname"),
                        PropertyValue = (string?)rElem.Attribute("propertyvalue"),
                    };
                    if (double.TryParse((string?)rElem.Attribute("threshold"), NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold))
                    {
                        rule.Threshold = threshold;
                    }
                    test.Rules.Add(rule);
                }
            }

            // Results and Groups
            var resultsElem = testElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("clashresults", StringComparison.OrdinalIgnoreCase));
            if (resultsElem is not null)
            {
                foreach (var child in resultsElem.Elements())
                {
                    if (child.Name.LocalName.Equals("clashgroup", StringComparison.OrdinalIgnoreCase))
                    {
                        var group = ParseClashGroup(child);
                        if (group is not null)
                        {
                            test.Groups.Add(group);
                        }
                    }
                    else if (child.Name.LocalName.Equals("clashresult", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = ParseClashResult(child);
                        if (result is not null)
                        {
                            test.AddResult(result);
                        }
                    }
                }
            }

            tests.Add(test);
        }

        return tests;
    }

    private static NwcClashGroup? ParseClashGroup(XElement grpElem)
    {
        string name = (string?)grpElem.Attribute("name") ?? "Group";
        string statusStr = (string?)grpElem.Attribute("status") ?? "new";
        _ = Enum.TryParse<NwcClashStatus>(statusStr, ignoreCase: true, out var status);

        var group = new NwcClashGroup(name, status)
        {
            Description = (string?)grpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("description", StringComparison.OrdinalIgnoreCase)),
            AssignedTo = (string?)grpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("assignedto", StringComparison.OrdinalIgnoreCase)),
            ApprovedBy = (string?)grpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("approvedby", StringComparison.OrdinalIgnoreCase)),
        };

        group.GroupGuid = ParseGuid(grpElem, group.GroupGuid);
        group.CreatedDate = ParseDate(Child(grpElem, "createddate"), group.CreatedDate);
        group.GridLocation = (string?)Child(grpElem, "gridlocation");
        group.Comments.AddRange(ParseComments(grpElem, status));

        var childResultsElem = grpElem.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("clashresults", StringComparison.OrdinalIgnoreCase));
        var resultElems = childResultsElem is not null
            ? childResultsElem.Elements().Where(e => e.Name.LocalName.Equals("clashresult", StringComparison.OrdinalIgnoreCase))
            : grpElem.Elements().Where(e => e.Name.LocalName.Equals("clashresult", StringComparison.OrdinalIgnoreCase));

        foreach (var rElem in resultElems)
        {
            var r = ParseClashResult(rElem);
            if (r is not null)
            {
                group.AddClash(r);
            }
        }

        return group;
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

        result.ResultGuid = ParseGuid(resElem, result.ResultGuid);
        result.CreatedDate = ParseDate(Child(resElem, "createddate"), result.CreatedDate);
        result.Comments.AddRange(ParseComments(resElem, status));

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
