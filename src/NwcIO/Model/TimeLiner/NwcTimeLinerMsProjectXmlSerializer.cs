using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace NwcIO.Model.TimeLiner;

/// <summary>
/// Serializes and deserializes Navisworks 4D TimeLiner construction simulation schedules
/// to and from Microsoft Project XML format (.xml).
/// </summary>
public static class NwcTimeLinerMsProjectXmlSerializer
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/project";

    /// <summary>
    /// Serializes a 4D TimeLiner schedule to a Microsoft Project XML file (.xml).
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
    /// Serializes a 4D TimeLiner schedule to a Microsoft Project XML stream.
    /// </summary>
    public static void Write(NwcTimeLinerSchedule schedule, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(stream);

        var root = new XElement(Ns + "Project",
            new XElement(Ns + "Name", schedule.Name),
            new XElement(Ns + "StartDate", (schedule.OverallStart ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(Ns + "FinishDate", (schedule.OverallEnd ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));

        var tasksElem = new XElement(Ns + "Tasks");
        root.Add(tasksElem);

        int uidCounter = 1;
        SerializeTasksRecursive(tasksElem, schedule.Tasks, outlineLevel: 1, prefix: "", ref uidCounter);

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);
        doc.Save(writer);
    }

    private static void SerializeTasksRecursive(
        XElement tasksContainer,
        List<NwcTimeLinerTask> tasks,
        int outlineLevel,
        string prefix,
        ref int uidCounter)
    {
        for (int i = 0; i < tasks.Count; i++)
        {
            var task = tasks[i];
            int uid = uidCounter++;
            string outlineNum = string.IsNullOrEmpty(prefix) ? $"{i + 1}" : $"{prefix}.{i + 1}";

            var taskElem = new XElement(Ns + "Task",
                new XElement(Ns + "UID", uid),
                new XElement(Ns + "ID", uid),
                new XElement(Ns + "Name", task.Name),
                new XElement(Ns + "Active", 1),
                new XElement(Ns + "Type", 0),
                new XElement(Ns + "IsNull", 0),
                new XElement(Ns + "OutlineLevel", outlineLevel),
                new XElement(Ns + "OutlineNumber", outlineNum),
                new XElement(Ns + "WBS", string.IsNullOrEmpty(task.Id) ? outlineNum : task.Id),
                new XElement(Ns + "Start", task.PlannedStart.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement(Ns + "Finish", task.PlannedEnd.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement(Ns + "Duration", FormatDuration(task.PlannedDuration)),
                new XElement(Ns + "PercentComplete", (int)Math.Round(Math.Clamp(task.Progress, 0.0, 1.0) * 100)));

            if (task.PlannedCost.HasValue)
            {
                taskElem.Add(new XElement(Ns + "Cost", task.PlannedCost.Value.ToString("F2", CultureInfo.InvariantCulture)));
            }

            if (task.ActualStart.HasValue)
            {
                taskElem.Add(new XElement(Ns + "ActualStart", task.ActualStart.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
            }

            if (task.ActualEnd.HasValue)
            {
                taskElem.Add(new XElement(Ns + "ActualFinish", task.ActualEnd.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)));
            }

            if (task.ActualCost.HasValue)
            {
                taskElem.Add(new XElement(Ns + "ActualCost", task.ActualCost.Value.ToString("F2", CultureInfo.InvariantCulture)));
            }

            var notesBuilder = new StringBuilder();
            if (!string.IsNullOrEmpty(task.AttachedSelectionSetName))
            {
                notesBuilder.Append("Set: ").Append(EscapeNoteValue(task.AttachedSelectionSetName));
            }
            if (task.TaskType != NwcTimeLinerTaskType.Construct)
            {
                if (notesBuilder.Length > 0) notesBuilder.Append('\n');
                notesBuilder.Append("Type: ").Append(task.TaskType.ToString());
            }
            if (notesBuilder.Length > 0)
            {
                taskElem.Add(new XElement(Ns + "Notes", notesBuilder.ToString()));
            }

            tasksContainer.Add(taskElem);

            if (task.Children.Count > 0)
            {
                SerializeTasksRecursive(tasksContainer, task.Children, outlineLevel + 1, outlineNum, ref uidCounter);
            }
        }
    }

    /// <summary>
    /// Loads a 4D TimeLiner schedule from a Microsoft Project XML file (.xml).
    /// </summary>
    public static NwcTimeLinerSchedule Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath)) throw new FileNotFoundException($"MS Project XML file '{filePath}' not found.", filePath);

        using var fs = File.OpenRead(filePath);
        return Read(fs);
    }

    /// <summary>
    /// Reads a 4D TimeLiner schedule from a Microsoft Project XML stream.
    /// </summary>
    public static NwcTimeLinerSchedule Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var doc = XDocument.Load(stream);
        var root = doc.Root;
        if (root is null) throw new InvalidDataException("Invalid or empty XML document.");

        XNamespace ns = root.Name.Namespace;
        string projName = root.Element(ns + "Name")?.Value ?? "MS Project Schedule";
        var schedule = new NwcTimeLinerSchedule(projName);

        var tasksElem = root.Element(ns + "Tasks");
        if (tasksElem is null) return schedule;

        var parsedTasks = new List<(NwcTimeLinerTask task, int outlineLevel)>();
        int taskIndex = 0;

        foreach (var taskElem in tasksElem.Elements(ns + "Task"))
        {
            taskIndex++;
            string? isNull = taskElem.Element(ns + "IsNull")?.Value;
            if (isNull == "1" || isNull?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
            {
                continue;
            }

            string? uid = taskElem.Element(ns + "UID")?.Value;
            string? levelStr = taskElem.Element(ns + "OutlineLevel")?.Value;
            int outlineLevel = 1;
            if (int.TryParse(levelStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLevel))
            {
                outlineLevel = parsedLevel;
            }

            // The MS Project project summary task is UID 0 / OutlineLevel 0 and
            // must not become an extra root next to the real top-level tasks.
            if (outlineLevel <= 0 || uid == "0")
            {
                continue;
            }

            string name = taskElem.Element(ns + "Name")?.Value ?? "Task";
            string? wbs = taskElem.Element(ns + "WBS")?.Value;
            string id = !string.IsNullOrWhiteSpace(wbs) ? wbs : uid ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = $"task-{taskIndex}";
                schedule.Warnings.Add($"Task {taskIndex}: id is missing; using '{id}'.");
            }

            string? startStr = taskElem.Element(ns + "Start")?.Value;
            DateTimeOffset start;
            if (string.IsNullOrEmpty(startStr) ||
                !DateTimeOffset.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out start))
            {
                start = DateTimeOffset.UnixEpoch;
                schedule.Warnings.Add(
                    $"Task '{id}': start '{startStr}' is missing or invalid; using {start:o}.");
            }

            string? finishStr = taskElem.Element(ns + "Finish")?.Value;
            DateTimeOffset finish;
            if (string.IsNullOrEmpty(finishStr) ||
                !DateTimeOffset.TryParse(finishStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out finish))
            {
                finish = start.AddDays(1);
                schedule.Warnings.Add(
                    $"Task '{id}': finish '{finishStr}' is missing or invalid; using {finish:o}.");
            }

            var task = new NwcTimeLinerTask(id, name, start, finish);

            string? pctStr = taskElem.Element(ns + "PercentComplete")?.Value;
            if (double.TryParse(pctStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                task.Progress = Math.Clamp(pct / 100.0, 0.0, 1.0);
            }

            string? costStr = taskElem.Element(ns + "Cost")?.Value;
            if (decimal.TryParse(costStr, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal cost))
            {
                task.PlannedCost = cost;
            }

            string? actStartStr = taskElem.Element(ns + "ActualStart")?.Value;
            if (!string.IsNullOrEmpty(actStartStr))
            {
                if (DateTimeOffset.TryParse(actStartStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var actStart))
                {
                    task.ActualStart = actStart;
                }
                else
                {
                    schedule.Warnings.Add($"Task '{id}': actual start '{actStartStr}' is invalid; ignored.");
                }
            }

            string? actFinishStr = taskElem.Element(ns + "ActualFinish")?.Value;
            if (!string.IsNullOrEmpty(actFinishStr))
            {
                if (DateTimeOffset.TryParse(actFinishStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var actFinish))
                {
                    task.ActualEnd = actFinish;
                }
                else
                {
                    schedule.Warnings.Add($"Task '{id}': actual finish '{actFinishStr}' is invalid; ignored.");
                }
            }

            string? actCostStr = taskElem.Element(ns + "ActualCost")?.Value;
            if (decimal.TryParse(actCostStr, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal actCost))
            {
                task.ActualCost = actCost;
            }

            string? notes = taskElem.Element(ns + "Notes")?.Value;
            if (!string.IsNullOrEmpty(notes))
            {
                ExtractNotesMetadata(notes, task);
            }

            parsedTasks.Add((task, outlineLevel));
        }

        // Reconstruct hierarchical WBS from outline levels
        var stack = new Stack<(int level, NwcTimeLinerTask task)>();
        foreach (var (t, level) in parsedTasks)
        {
            while (stack.Count > 0 && stack.Peek().level >= level)
            {
                stack.Pop();
            }

            if (stack.Count > 0)
            {
                stack.Peek().task.Children.Add(t);
            }
            else
            {
                schedule.Tasks.Add(t);
            }

            stack.Push((level, t));
        }

        return schedule;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        // Duration is elapsed wall-clock time, not MS Project working time on a
        // calendar; no Calendars element is written, so keep every component.
        var totalSeconds = (long)Math.Round(Math.Max(0, duration.TotalSeconds));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"PT{totalSeconds / 3600}H{totalSeconds % 3600 / 60}M{totalSeconds % 60}S");
    }

    private static string EscapeNoteValue(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string UnescapeNoteValue(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var escaped = value[++i];
                result.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    '\\' => '\\',
                    _ => escaped,
                });
            }
            else
            {
                result.Append(value[i]);
            }
        }
        return result.ToString();
    }

    private static void ExtractNotesMetadata(string notes, NwcTimeLinerTask task)
    {
        // Notes written by this serializer put each metadata entry on its own
        // line, so values may contain '|'. Older files used " | " as separator.
        var structured = false;
        foreach (var rawLine in notes.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("Set:", StringComparison.OrdinalIgnoreCase))
            {
                task.AttachedSelectionSetName = UnescapeNoteValue(line["Set:".Length..].Trim());
                structured = true;
            }
            else if (line.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
            {
                string typeStr = line["Type:".Length..].Trim();
                if (Enum.TryParse<NwcTimeLinerTaskType>(typeStr, ignoreCase: true, out var parsedType))
                {
                    task.TaskType = parsedType;
                }
                structured = true;
            }
        }

        if (structured)
        {
            return;
        }

        foreach (var part in notes.Split('|', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("Set:", StringComparison.OrdinalIgnoreCase))
            {
                task.AttachedSelectionSetName = part["Set:".Length..].Trim();
            }
            else if (part.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
            {
                string typeStr = part["Type:".Length..].Trim();
                if (Enum.TryParse<NwcTimeLinerTaskType>(typeStr, ignoreCase: true, out var parsedType))
                {
                    task.TaskType = parsedType;
                }
            }
        }
    }
}
