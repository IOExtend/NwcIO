using System.Text;
using System.Xml.Linq;

namespace NwcIO.Model.Sets;

/// <summary>
/// Serializes and deserializes Navisworks Selection Sets and Search Sets to/from standard Navisworks XML (.xml) format.
/// Fully compatible with Autodesk Navisworks Manage, Simulate, and Freedom "Import/Export Selection Sets".
/// </summary>
public static class NwcXmlSetSerializer
{
    private const string SchemaLocation = "http://download.autodesk.com/us/navisworks/schemas/nw-exchange-12.0.xsd";

    /// <summary>
    /// Serializes a collection of selection set items (including folders and search sets) to a Navisworks XML file.
    /// </summary>
    public static void Save(IEnumerable<NwcSetItem> sets, string filePath)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(filePath);

        using var fs = File.Create(filePath);
        Write(sets, fs);
    }

    /// <summary>
    /// Serializes a collection of selection set items to an XML stream.
    /// </summary>
    public static void Write(IEnumerable<NwcSetItem> sets, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(stream);

        var root = new XElement("exchange",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "noNamespaceSchemaLocation", SchemaLocation));

        var selectionsets = new XElement("selectionsets");
        root.Add(selectionsets);

        foreach (var item in sets)
        {
            selectionsets.Add(SerializeItem(item));
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
        doc.Save(writer);
    }

    /// <summary>
    /// Parses Navisworks Selection Sets XML from a file on disk.
    /// </summary>
    public static IReadOnlyList<NwcSetItem> Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var fs = File.OpenRead(filePath);
        return Read(fs);
    }

    /// <summary>
    /// Parses Navisworks Selection Sets XML from a stream.
    /// </summary>
    public static IReadOnlyList<NwcSetItem> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var doc = XDocument.Load(stream);
        var setsContainer = doc.Root?.Element("selectionsets");
        if (setsContainer is null) return Array.Empty<NwcSetItem>();

        var list = new List<NwcSetItem>();
        foreach (var elem in setsContainer.Elements())
        {
            var item = DeserializeItem(elem);
            if (item is not null)
            {
                list.Add(item);
            }
        }

        return list;
    }

    private static XElement SerializeItem(NwcSetItem item)
    {
        if (item is NwcSelectionSetFolder folder)
        {
            var folderElem = new XElement("viewfolder",
                new XAttribute("name", folder.Name),
                new XAttribute("guid", folder.SetGuid.ToString()));

            if (!string.IsNullOrEmpty(folder.Comment))
            {
                folderElem.Add(new XElement("comments", new XElement("comment", folder.Comment)));
            }

            foreach (var child in folder.Children)
            {
                folderElem.Add(SerializeItem(child));
            }
            return folderElem;
        }

        if (item is NwcSearchSet searchSet)
        {
            var setElem = new XElement("selectionset",
                new XAttribute("name", searchSet.Name),
                new XAttribute("guid", searchSet.SetGuid.ToString()));

            if (!string.IsNullOrEmpty(searchSet.Comment))
            {
                setElem.Add(new XElement("comments", new XElement("comment", searchSet.Comment)));
            }

            var findspec = new XElement("findspec",
                new XAttribute("mode", searchSet.MatchAllConditions ? "all" : "any"),
                new XAttribute("disjoint", "0"));

            var conditions = new XElement("conditions");
            foreach (var c in searchSet.Conditions)
            {
                string testOp = NwcSearchOperatorFormat.Format(c.Operator);

                var condElem = new XElement("condition",
                    new XAttribute("test", testOp),
                    new XElement("category", new XElement("name", c.Category)),
                    new XElement("property", new XElement("name", c.Property)),
                    new XElement("value", new XElement("data", new XAttribute("type", "wstring"), c.Value)));

                conditions.Add(condElem);
            }

            findspec.Add(conditions);
            setElem.Add(findspec);
            return setElem;
        }

        if (item is NwcSelectionSet selectionSet)
        {
            var setElem = new XElement("selectionset",
                new XAttribute("name", selectionSet.Name),
                new XAttribute("guid", selectionSet.SetGuid.ToString()));

            if (!string.IsNullOrEmpty(selectionSet.Comment))
            {
                setElem.Add(new XElement("comments", new XElement("comment", selectionSet.Comment)));
            }

            if (selectionSet.NodeIds.Count > 0 || selectionSet.NodePaths.Count > 0)
            {
                var paths = new XElement("paths");
                foreach (int id in selectionSet.NodeIds)
                {
                    paths.Add(new XElement("path", new XElement("node", id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
                }
                foreach (string path in selectionSet.NodePaths)
                {
                    paths.Add(new XElement("path", new XElement("nodepath", path)));
                }
                setElem.Add(paths);
            }

            return setElem;
        }

        return new XElement("selectionset", new XAttribute("name", item.Name));
    }

    private static NwcSetItem? DeserializeItem(XElement elem)
    {
        string name = elem.Attribute("name")?.Value ?? string.Empty;
        string? guidStr = elem.Attribute("guid")?.Value;
        _ = Guid.TryParse(guidStr, out var guid);

        string? comment = elem.Element("comments")?.Element("comment")?.Value;

        if (elem.Name.LocalName.Equals("viewfolder", StringComparison.OrdinalIgnoreCase))
        {
            var folder = new NwcSelectionSetFolder(name)
            {
                SetGuid = guid != Guid.Empty ? guid : Guid.NewGuid(),
                Comment = comment,
            };

            foreach (var child in elem.Elements())
            {
                if (child.Name.LocalName is "viewfolder" or "selectionset")
                {
                    var childItem = DeserializeItem(child);
                    if (childItem is not null)
                    {
                        folder.Children.Add(childItem);
                    }
                }
            }

            return folder;
        }

        if (elem.Name.LocalName.Equals("selectionset", StringComparison.OrdinalIgnoreCase))
        {
            var findspec = elem.Element("findspec");
            if (findspec is not null)
            {
                // Search Set
                bool matchAll = (findspec.Attribute("mode")?.Value ?? "all").Equals("all", StringComparison.OrdinalIgnoreCase);
                var searchSet = new NwcSearchSet(name)
                {
                    SetGuid = guid != Guid.Empty ? guid : Guid.NewGuid(),
                    Comment = comment,
                    MatchAllConditions = matchAll,
                };

                var conditionsElem = findspec.Element("conditions");
                if (conditionsElem is not null)
                {
                    foreach (var cElem in conditionsElem.Elements("condition"))
                    {
                        string category = cElem.Element("category")?.Element("name")?.Value ?? "Item";
                        string property = cElem.Element("property")?.Element("name")?.Value ?? "Name";
                        string val = cElem.Element("value")?.Element("data")?.Value ?? string.Empty;

                        var op = NwcSearchOperatorFormat.Parse(cElem.Attribute("test")?.Value);

                        searchSet.Conditions.Add(new NwcSearchCondition(category, property, op, val));
                    }
                }

                return searchSet;
            }

            // Explicit Selection Set
            var selectionSet = new NwcSelectionSet(name)
            {
                SetGuid = guid != Guid.Empty ? guid : Guid.NewGuid(),
                Comment = comment,
            };

            var pathsElem = elem.Element("paths");
            if (pathsElem is not null)
            {
                foreach (var pElem in pathsElem.Elements("path"))
                {
                    string? nodeVal = pElem.Element("node")?.Value;
                    if (int.TryParse(nodeVal, out int nodeId))
                    {
                        selectionSet.NodeIds.Add(nodeId);
                    }
                    string? pathVal = pElem.Element("nodepath")?.Value;
                    if (!string.IsNullOrEmpty(pathVal))
                    {
                        selectionSet.NodePaths.Add(pathVal);
                    }
                }
            }

            return selectionSet;
        }

        return null;
    }
}
