using System.Globalization;
using System.Text;

namespace NwcIO.Model.TimeLiner;

/// <summary>
/// Serializes and deserializes Navisworks 4D TimeLiner construction simulation schedules
/// to and from Primavera P6 XER format (.xer).
/// </summary>
public static class NwcTimeLinerPrimaveraXerSerializer
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>
    /// Serializes a 4D TimeLiner schedule to a Primavera P6 XER file (.xer).
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
    /// Serializes a 4D TimeLiner schedule to a Primavera P6 XER stream.
    /// </summary>
    public static void Write(NwcTimeLinerSchedule schedule, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(stream);

        using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 4096, leaveOpen: true);

        // 1. Primavera XER Header
        string exportDate = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        writer.WriteLine($"ERMHDR\t21.12\t{exportDate}\tPROJECT\tEXPORT\tNwcIO 4D TimeLiner");

        // 2. PROJECT Table
        var overallStart = schedule.OverallStart ?? DateTimeOffset.UtcNow;
        var overallEnd = schedule.OverallEnd ?? NwcTimeLinerDates.AddDays(overallStart, 30);

        writer.WriteLine("%T\tPROJECT");
        writer.WriteLine("%F\tproj_id\tproj_short_name\tplan_start_date\tplan_end_date\tstatus_code");
        writer.WriteLine($"%R\t1\t{CleanField(schedule.Name)}\t{overallStart.ToString(DateFormat, CultureInfo.InvariantCulture)}\t{overallEnd.ToString(DateFormat, CultureInfo.InvariantCulture)}\tActive");

        // 3. PROJWBS Table
        writer.WriteLine("%T\tPROJWBS");
        writer.WriteLine("%F\twbs_id\tproj_id\tparent_wbs_id\twbs_short_name\twbs_name");
        writer.WriteLine($"%R\t100\t1\t\tROOT\t{CleanField(schedule.Name)}");

        // 4. TASK Table
        writer.WriteLine("%T\tTASK");
        writer.WriteLine("%F\ttask_id\tproj_id\twbs_id\ttask_code\ttask_name\ttask_type\tstatus_code\ttarget_start_date\ttarget_end_date\tact_start_date\tact_end_date\ttarget_drtn_hr_cnt\tphys_complete_pct\ttarget_cost\tact_cost");

        int taskId = 1000;
        foreach (var task in schedule.FlattenTasks())
        {
            taskId++;
            string status = task.Progress >= 1.0 ? "TK_Complete" : (task.Progress > 0.0 ? "TK_Active" : "TK_NotStart");
            string actStart = task.ActualStart?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";
            string actEnd = task.ActualEnd?.ToString(DateFormat, CultureInfo.InvariantCulture) ?? "";
            string durationHours = Math.Max(0.0, task.PlannedDuration.TotalHours).ToString("F1", CultureInfo.InvariantCulture);
            string pct = (Math.Clamp(task.Progress, 0.0, 1.0) * 100.0).ToString("F1", CultureInfo.InvariantCulture);
            string cost = task.PlannedCost?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
            string actCost = task.ActualCost?.ToString("F2", CultureInfo.InvariantCulture) ?? "";
            string code = string.IsNullOrEmpty(task.Id) ? $"T{taskId}" : task.Id;

            writer.WriteLine($"%R\t{taskId}\t1\t100\t{CleanField(code)}\t{CleanField(task.Name)}\tTT_Task\t{status}\t{task.PlannedStart.ToString(DateFormat, CultureInfo.InvariantCulture)}\t{task.PlannedEnd.ToString(DateFormat, CultureInfo.InvariantCulture)}\t{actStart}\t{actEnd}\t{durationHours}\t{pct}\t{cost}\t{actCost}");
        }

        writer.Flush();
    }

    /// <summary>
    /// Loads a 4D TimeLiner schedule from a Primavera P6 XER file (.xer).
    /// </summary>
    public static NwcTimeLinerSchedule Open(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath)) throw new FileNotFoundException($"Primavera P6 file '{filePath}' not found.", filePath);

        using var fs = File.OpenRead(filePath);
        return Read(fs);
    }

    /// <summary>
    /// Reads a 4D TimeLiner schedule from a Primavera P6 XER stream.
    /// </summary>
    public static NwcTimeLinerSchedule Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string scheduleName = "Primavera P6 Schedule";
        var tasks = new List<NwcTimeLinerTask>();
        var warnings = new List<string>();

        string? currentTable = null;
        string[]? fieldNames = null;
        int taskRow = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] tokens = line.Split('\t');
            if (tokens.Length == 0) continue;

            string recordType = tokens[0];

            if (recordType == "%T" && tokens.Length > 1)
            {
                currentTable = tokens[1].Trim().ToUpperInvariant();
                fieldNames = null;
            }
            else if (recordType == "%F" && currentTable is not null)
            {
                fieldNames = tokens.Skip(1).Select(f => f.Trim().ToLowerInvariant()).ToArray();
            }
            else if (recordType == "%R" && currentTable is not null && fieldNames is not null)
            {
                var values = tokens.Skip(1).ToArray();
                var rowMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < fieldNames.Length && i < values.Length; i++)
                {
                    rowMap[fieldNames[i]] = values[i];
                }

                if (currentTable == "PROJECT")
                {
                    if (rowMap.TryGetValue("proj_short_name", out var pName) && !string.IsNullOrEmpty(pName))
                    {
                        scheduleName = pName;
                    }
                }
                else if (currentTable == "TASK")
                {
                    taskRow++;
                    string id = rowMap.GetValueOrDefault("task_code") ?? rowMap.GetValueOrDefault("task_id") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        id = $"task-{taskRow}";
                        warnings.Add($"Task {taskRow}: id is missing; using '{id}'.");
                    }
                    string name = rowMap.GetValueOrDefault("task_name") ?? "Task";

                    DateTimeOffset start;
                    if (!rowMap.TryGetValue("target_start_date", out var startStr) ||
                        string.IsNullOrWhiteSpace(startStr) ||
                        !DateTimeOffset.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out start))
                    {
                        start = DateTimeOffset.UnixEpoch;
                        warnings.Add($"Task '{id}': target start '{startStr}' is missing or invalid; using {start:o}.");
                    }

                    DateTimeOffset end;
                    if (!rowMap.TryGetValue("target_end_date", out var endStr) ||
                        string.IsNullOrWhiteSpace(endStr) ||
                        !DateTimeOffset.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out end))
                    {
                        end = NwcTimeLinerDates.AddDays(start, 1);
                        warnings.Add($"Task '{id}': target end '{endStr}' is missing or invalid; using {end:o}.");
                    }

                    var task = new NwcTimeLinerTask(id, name, start, end);

                    if (rowMap.TryGetValue("phys_complete_pct", out var pctStr) && double.TryParse(pctStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
                    {
                        task.Progress = Math.Clamp(pct / 100.0, 0.0, 1.0);
                    }

                    if (rowMap.TryGetValue("target_cost", out var costStr) && decimal.TryParse(costStr, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal cost))
                    {
                        task.PlannedCost = cost;
                    }

                    if (rowMap.TryGetValue("act_cost", out var actCostStr) && decimal.TryParse(actCostStr, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal actCost))
                    {
                        task.ActualCost = actCost;
                    }

                    if (rowMap.TryGetValue("act_start_date", out var actStartStr) && !string.IsNullOrWhiteSpace(actStartStr))
                    {
                        if (DateTimeOffset.TryParse(actStartStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var pActStart))
                        {
                            task.ActualStart = pActStart;
                        }
                        else
                        {
                            warnings.Add($"Task '{id}': actual start '{actStartStr}' is invalid; ignored.");
                        }
                    }

                    if (rowMap.TryGetValue("act_end_date", out var actEndStr) && !string.IsNullOrWhiteSpace(actEndStr))
                    {
                        if (DateTimeOffset.TryParse(actEndStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var pActEnd))
                        {
                            task.ActualEnd = pActEnd;
                        }
                        else
                        {
                            warnings.Add($"Task '{id}': actual end '{actEndStr}' is invalid; ignored.");
                        }
                    }

                    tasks.Add(task);
                }
            }
        }

        var schedule = new NwcTimeLinerSchedule(scheduleName, tasks);
        schedule.Warnings.AddRange(warnings);
        return schedule;
    }

    private static string CleanField(string? field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        return field.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
