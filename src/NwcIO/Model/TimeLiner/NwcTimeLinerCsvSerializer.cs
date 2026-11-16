using System.Globalization;
using System.Text;

namespace NwcIO.Model.TimeLiner;

/// <summary>
/// Serializes and deserializes Navisworks 4D TimeLiner schedules to/from CSV format.
/// Fully compatible with Navisworks TimeLiner "Import/Export Tasks to CSV", Microsoft Project, and Primavera P6.
/// CSV has no UTC offset field: dates are written as UTC and read back as UTC, so instants round-trip.
/// </summary>
public static class NwcTimeLinerCsvSerializer
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    public static void Save(NwcTimeLinerSchedule schedule, string filePath)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(filePath);

        using var fs = File.Create(filePath);
        Write(schedule, fs);
    }

    public static void Write(NwcTimeLinerSchedule schedule, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(stream);

        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);

        // Header
        writer.WriteLine("Task ID,Task Name,Task Type,Planned Start,Planned End,Actual Start,Actual End,Planned Cost,ActualCost,Attached Set,Progress");

        foreach (var task in schedule.FlattenTasks())
        {
            string line = string.Join(",",
                EscapeCsv(task.Id),
                EscapeCsv(task.Name),
                task.TaskType.ToString(),
                task.PlannedStart.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture),
                task.PlannedEnd.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture),
                task.ActualStart?.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
                task.ActualEnd?.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
                task.PlannedCost?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty,
                task.ActualCost?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty,
                EscapeCsv(task.AttachedSelectionSetName ?? string.Empty),
                task.Progress.ToString("P0", CultureInfo.InvariantCulture));

            writer.WriteLine(line);
        }
    }

    public static NwcTimeLinerSchedule Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        using var fs = File.OpenRead(filePath);
        return Read(fs);
    }

    public static NwcTimeLinerSchedule Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return Read(reader);
    }

    public static NwcTimeLinerSchedule Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var schedule = new NwcTimeLinerSchedule();
        string? header = reader.ReadLine();
        if (header is null) return schedule;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = ParseCsvLine(line);
            if (parts.Count < 5) continue;

            string id = parts[0];
            string name = parts[1];
            string typeStr = parts[2];
            string plannedStartStr = parts[3];
            string plannedEndStr = parts[4];

            if (!Enum.TryParse<NwcTimeLinerTaskType>(typeStr, ignoreCase: true, out var taskType))
            {
                taskType = NwcTimeLinerTaskType.Construct;
            }

            if (!DateTimeOffset.TryParse(plannedStartStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var pStart))
            {
                pStart = DateTimeOffset.UtcNow;
            }
            if (!DateTimeOffset.TryParse(plannedEndStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var pEnd))
            {
                pEnd = pStart.AddDays(7);
            }

            var task = new NwcTimeLinerTask(id, name, pStart, pEnd, taskType);

            if (parts.Count > 5 && DateTimeOffset.TryParse(parts[5], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var aStart))
            {
                task.ActualStart = aStart;
            }
            if (parts.Count > 6 && DateTimeOffset.TryParse(parts[6], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var aEnd))
            {
                task.ActualEnd = aEnd;
            }
            if (parts.Count > 7 && decimal.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var pCost))
            {
                task.PlannedCost = pCost;
            }
            if (parts.Count > 8 && decimal.TryParse(parts[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var aCost))
            {
                task.ActualCost = aCost;
            }
            if (parts.Count > 9 && !string.IsNullOrWhiteSpace(parts[9]))
            {
                task.AttachedSelectionSetName = parts[9];
            }

            schedule.Tasks.Add(task);
        }

        return schedule;
    }

    private static string EscapeCsv(string text)
    {
        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }
        return text;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString().Trim());
        return result;
    }
}
