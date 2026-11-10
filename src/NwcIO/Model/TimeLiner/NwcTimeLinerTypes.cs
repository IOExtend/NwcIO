namespace NwcIO.Model.TimeLiner;

/// <summary>
/// Construction task simulation type in Navisworks 4D TimeLiner.
/// </summary>
public enum NwcTimeLinerTaskType
{
    /// <summary>Element is hidden before start, highlighted during construction, visible on completion.</summary>
    Construct,
    /// <summary>Element is visible before demolition, highlighted during task, hidden on completion.</summary>
    Demolish,
    /// <summary>Element is hidden before task, highlighted during task, hidden on completion.</summary>
    Temporary,
}

/// <summary>
/// Simulation state of a construction element or task at a specific simulation timestamp.
/// </summary>
public enum NwcTimeLinerSimulationState
{
    NotStarted,
    InProgress,
    Completed,
}

/// <summary>
/// A construction activity or work breakdown structure (WBS) task in a 4D TimeLiner schedule.
/// </summary>
public sealed class NwcTimeLinerTask
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public NwcTimeLinerTaskType TaskType { get; set; } = NwcTimeLinerTaskType.Construct;

    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }

    public decimal? PlannedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public double Progress { get; set; }

    /// <summary>Optional name of an attached Selection Set or Search Set linking model geometry.</summary>
    public string? AttachedSelectionSetName { get; set; }

    /// <summary>IDs of specific scene graph nodes directly bound to this task.</summary>
    public List<int> AttachedNodeIds { get; } = [];

    /// <summary>Subtasks in the hierarchical Work Breakdown Structure (WBS).</summary>
    public List<NwcTimeLinerTask> Children { get; } = [];

    public TimeSpan PlannedDuration => PlannedEnd - PlannedStart;
    public TimeSpan? ActualDuration => (ActualStart.HasValue && ActualEnd.HasValue) ? ActualEnd.Value - ActualStart.Value : null;

    public NwcTimeLinerTask() { }

    public NwcTimeLinerTask(string id, string name, DateTimeOffset plannedStart, DateTimeOffset plannedEnd, NwcTimeLinerTaskType type = NwcTimeLinerTaskType.Construct)
    {
        Id = id;
        Name = name;
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
        TaskType = type;
    }

    /// <summary>Checks whether this task is actively executing at the given simulation date.</summary>
    public bool IsActiveAt(DateTimeOffset date) => date >= PlannedStart && date <= PlannedEnd;

    /// <summary>Returns the simulation state (NotStarted, InProgress, Completed) at the given timestamp.</summary>
    public NwcTimeLinerSimulationState GetStateAt(DateTimeOffset date)
    {
        if (date < PlannedStart) return NwcTimeLinerSimulationState.NotStarted;
        if (date > PlannedEnd) return NwcTimeLinerSimulationState.Completed;
        return NwcTimeLinerSimulationState.InProgress;
    }
}

/// <summary>
/// Represents a complete 4D construction schedule project (TimeLiner) with tasks, WBS hierarchy, and simulation logic.
/// </summary>
public sealed class NwcTimeLinerSchedule
{
    public string Name { get; set; } = "TimeLiner Schedule";
    public List<NwcTimeLinerTask> Tasks { get; } = [];

    public NwcTimeLinerSchedule() { }

    public NwcTimeLinerSchedule(string name)
    {
        Name = name;
    }

    public NwcTimeLinerSchedule(IEnumerable<NwcTimeLinerTask> tasks)
    {
        Tasks.AddRange(tasks);
    }

    public NwcTimeLinerSchedule(string name, IEnumerable<NwcTimeLinerTask> tasks)
    {
        Name = name;
        Tasks.AddRange(tasks);
    }

    /// <summary>Returns a flat enumerable of all tasks including nested child tasks.</summary>
    public IEnumerable<NwcTimeLinerTask> FlattenTasks()
    {
        foreach (var t in Tasks)
        {
            yield return t;
            foreach (var child in FlattenTask(t))
            {
                yield return child;
            }
        }

        static IEnumerable<NwcTimeLinerTask> FlattenTask(NwcTimeLinerTask parent)
        {
            foreach (var c in parent.Children)
            {
                yield return c;
                foreach (var grandchild in FlattenTask(c))
                {
                    yield return grandchild;
                }
            }
        }
    }

    /// <summary>Gets the earliest planned start date across all schedule tasks.</summary>
    public DateTimeOffset? OverallStart => Tasks.Count > 0 ? FlattenTasks().Min(t => t.PlannedStart) : null;

    /// <summary>Gets the latest planned end date across all schedule tasks.</summary>
    public DateTimeOffset? OverallEnd => Tasks.Count > 0 ? FlattenTasks().Max(t => t.PlannedEnd) : null;

    /// <summary>Total planned budget across all schedule tasks.</summary>
    public decimal TotalPlannedCost => FlattenTasks().Sum(t => t.PlannedCost ?? 0m);

    /// <summary>Total actual expenditure across all schedule tasks.</summary>
    public decimal TotalActualCost => FlattenTasks().Sum(t => t.ActualCost ?? 0m);

    /// <summary>Returns all tasks actively occurring at the specified simulation date.</summary>
    public IReadOnlyList<NwcTimeLinerTask> GetActiveTasksAt(DateTimeOffset date) =>
        FlattenTasks().Where(t => t.IsActiveAt(date)).ToList();

    /// <summary>
    /// Computes the overall timeline simulation completion progress [0.0..1.0] at a given timestamp.
    /// </summary>
    public double GetSimulationProgressAt(DateTimeOffset date)
    {
        var start = OverallStart;
        var end = OverallEnd;
        if (!start.HasValue || !end.HasValue || end <= start) return 0.0;

        if (date <= start.Value) return 0.0;
        if (date >= end.Value) return 1.0;

        double totalMs = (end.Value - start.Value).TotalMilliseconds;
        double currentMs = (date - start.Value).TotalMilliseconds;
        return Math.Clamp(currentMs / totalMs, 0.0, 1.0);
    }

    /// <summary>
    /// Evaluates the 4D state of an individual scene graph node at a given simulation date based on attached tasks.
    /// </summary>
    public NwcTimeLinerSimulationState GetNodeStateAt(int nodeId, DateTimeOffset date)
    {
        var relatedTasks = FlattenTasks().Where(t => t.AttachedNodeIds.Contains(nodeId)).ToList();
        if (relatedTasks.Count == 0) return NwcTimeLinerSimulationState.Completed;

        if (relatedTasks.Any(t => t.GetStateAt(date) == NwcTimeLinerSimulationState.InProgress))
        {
            return NwcTimeLinerSimulationState.InProgress;
        }

        if (relatedTasks.All(t => t.GetStateAt(date) == NwcTimeLinerSimulationState.Completed))
        {
            return NwcTimeLinerSimulationState.Completed;
        }

        return NwcTimeLinerSimulationState.NotStarted;
    }
}
