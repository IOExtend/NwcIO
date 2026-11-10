using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace NwcIO.Model.TimeLiner;

/// <summary>
/// Serializes and deserializes Navisworks 4D TimeLiner construction simulation schedules
/// to and from standard Navisworks XML format (.xml).
/// </summary>
public static class NwcTimeLinerXmlSerializer
{
    private const string SchemaLocation = "http://download.autodesk.com/us/navisworks/schemas/nw-timeliner-12.0.xsd";

    /// <summary>
    /// Serializes a 4D TimeLiner schedule to an XML file.
    /// </summary>
    public static void Save(NwcTimeLinerSchedule schedule, string filePath)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(filePath);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(filePath);
        Write(schedule, fs);
    }

    /// <summary>
    /// Serializes a 4D TimeLiner schedule to an XML stream.
    /// </summary>
    public static void Write(NwcTimeLinerSchedule schedule, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(stream);

        var root = new XElement("exchange",
            new XAttribute(XNamespace.Xmlns + "xsi", "http://www.w3.org/2001/XMLSchema-instance"),
            new XAttribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "noNamespaceSchemaLocation", SchemaLocation),
            new XAttribute("name", schedule.Name));

        var tasksElem = new XElement("tasks");
        root.Add(tasksElem);

        foreach (var task in schedule.Tasks)
        {
            tasksElem.Add(SerializeTask(task));
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
        doc.Save(writer);
    }

    private static XElement SerializeTask(NwcTimeLinerTask task)
    {
        var elem = new XElement("task",
            new XAttribute("id", task.Id),
            new XAttribute("name", task.Name),
            new XAttribute("type", task.TaskType.ToString()),
            new XAttribute("progress", task.Progress.ToString("F3", CultureInfo.InvariantCulture)));

        var plannedElem = new XElement("planned",
            new XAttribute("start", task.PlannedStart.ToString("o", CultureInfo.InvariantCulture)),
            new XAttribute("end", task.PlannedEnd.ToString("o", CultureInfo.InvariantCulture)));

        if (task.PlannedCost.HasValue)
        {
            plannedElem.Add(new XAttribute("cost", task.PlannedCost.Value.ToString("F2", CultureInfo.InvariantCulture)));
        }
        elem.Add(plannedElem);

        if (task.ActualStart.HasValue && task.ActualEnd.HasValue)
        {
            var actualElem = new XElement("actual",
                new XAttribute("start", task.ActualStart.Value.ToString("o", CultureInfo.InvariantCulture)),
                new XAttribute("end", task.ActualEnd.Value.ToString("o", CultureInfo.InvariantCulture)));

            if (task.ActualCost.HasValue)
            {
                actualElem.Add(new XAttribute("cost", task.ActualCost.Value.ToString("F2", CultureInfo.InvariantCulture)));
            }
            elem.Add(actualElem);
        }

        if (!string.IsNullOrEmpty(task.AttachedSelectionSetName))
        {
            elem.Add(new XElement("selectionSet", new XAttribute("name", task.AttachedSelectionSetName)));
        }

        if (task.AttachedNodeIds.Count > 0)
        {
            var nodesElem = new XElement("attachedNodes");
            foreach (int nodeId in task.AttachedNodeIds)
            {
                nodesElem.Add(new XElement("node", new XAttribute("id", nodeId)));
            }
            elem.Add(nodesElem);
        }

        if (task.Children.Count > 0)
        {
            var subtasksElem = new XElement("tasks");
            foreach (var child in task.Children)
            {
                subtasksElem.Add(SerializeTask(child));
            }
            elem.Add(subtasksElem);
        }

        return elem;
    }

    /// <summary>
    /// Loads a 4D TimeLiner schedule from an XML file.
    /// </summary>
    public static NwcTimeLinerSchedule Load(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var fs = File.OpenRead(filePath);
        return Read(fs, Path.GetFileNameWithoutExtension(filePath));
    }

    /// <summary>
    /// Opens and deserializes a 4D TimeLiner schedule from an XML file.
    /// </summary>
    public static NwcTimeLinerSchedule Open(string filePath) => Load(filePath);

    /// <summary>
    /// Reads a 4D TimeLiner schedule from an XML stream.
    /// </summary>
    public static NwcTimeLinerSchedule Read(Stream stream, string defaultName = "TimeLiner Schedule")
    {
        ArgumentNullException.ThrowIfNull(stream);

        var doc = XDocument.Load(stream);
        var root = doc.Root;
        if (root is null)
        {
            return new NwcTimeLinerSchedule(defaultName);
        }

        string scheduleName = root.Attribute("name")?.Value ?? defaultName;
        var schedule = new NwcTimeLinerSchedule(scheduleName);

        var tasksElem = root.Element("tasks") ?? (root.Name.LocalName == "tasks" ? root : null);
        if (tasksElem is not null)
        {
            foreach (var taskElem in tasksElem.Elements("task"))
            {
                schedule.Tasks.Add(ParseTask(taskElem));
            }
        }

        return schedule;
    }

    private static NwcTimeLinerTask ParseTask(XElement elem)
    {
        string id = elem.Attribute("id")?.Value ?? Guid.NewGuid().ToString("N");
        string name = elem.Attribute("name")?.Value ?? "Unnamed Task";

        string typeStr = elem.Attribute("type")?.Value ?? "Construct";
        var taskType = Enum.TryParse<NwcTimeLinerTaskType>(typeStr, true, out var parsedType)
            ? parsedType
            : NwcTimeLinerTaskType.Construct;

        double progress = 0.0;
        string? progressStr = elem.Attribute("progress")?.Value;
        if (!string.IsNullOrEmpty(progressStr) && double.TryParse(progressStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
        {
            progress = p > 1.0 ? p / 100.0 : p;
        }

        DateTimeOffset plannedStart = DateTimeOffset.UtcNow;
        DateTimeOffset plannedEnd = plannedStart.AddDays(7);
        decimal? plannedCost = null;

        var plannedElem = elem.Element("planned");
        if (plannedElem is not null)
        {
            string? startStr = plannedElem.Attribute("start")?.Value;
            if (!string.IsNullOrEmpty(startStr) && DateTimeOffset.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ps))
            {
                plannedStart = ps;
            }

            string? endStr = plannedElem.Attribute("end")?.Value;
            if (!string.IsNullOrEmpty(endStr) && DateTimeOffset.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var pe))
            {
                plannedEnd = pe;
            }

            string? costStr = plannedElem.Attribute("cost")?.Value;
            if (!string.IsNullOrEmpty(costStr) && decimal.TryParse(costStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pc))
            {
                plannedCost = pc;
            }
        }

        var task = new NwcTimeLinerTask(id, name, plannedStart, plannedEnd, taskType)
        {
            Progress = progress,
            PlannedCost = plannedCost,
        };

        var actualElem = elem.Element("actual");
        if (actualElem is not null)
        {
            string? startStr = actualElem.Attribute("start")?.Value;
            if (!string.IsNullOrEmpty(startStr) && DateTimeOffset.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var actStart))
            {
                task.ActualStart = actStart;
            }

            string? endStr = actualElem.Attribute("end")?.Value;
            if (!string.IsNullOrEmpty(endStr) && DateTimeOffset.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var actEnd))
            {
                task.ActualEnd = actEnd;
            }

            string? costStr = actualElem.Attribute("cost")?.Value;
            if (!string.IsNullOrEmpty(costStr) && decimal.TryParse(costStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var actCost))
            {
                task.ActualCost = actCost;
            }
        }

        task.AttachedSelectionSetName = elem.Element("selectionSet")?.Attribute("name")?.Value ?? elem.Attribute("selectionSet")?.Value;

        var nodesElem = elem.Element("attachedNodes");
        if (nodesElem is not null)
        {
            foreach (var nodeElem in nodesElem.Elements("node"))
            {
                if (int.TryParse(nodeElem.Attribute("id")?.Value, out int nodeId))
                {
                    task.AttachedNodeIds.Add(nodeId);
                }
            }
        }

        var subtasksElem = elem.Element("tasks");
        if (subtasksElem is not null)
        {
            foreach (var childElem in subtasksElem.Elements("task"))
            {
                task.Children.Add(ParseTask(childElem));
            }
        }

        return task;
    }
}
