using System.Globalization;
using NwcIO.Model;
using NwcIO.Model.Sets;
using NwcIO.Model.TimeLiner;
using Xunit;

namespace NwcIO.Tests;

public class TimeLinerXmlTests
{
    private static readonly int[] Phase1Nodes = [101, 102, 103];
    private static readonly int[] Phase2Nodes = [201, 202, 203, 204];
    private static readonly int[] SetPilesNodes = [501, 502, 503];
    private static readonly int[] SetColumnsNodes = [601, 602];

    [Fact]
    public void Roundtrip_FullSchedule_MatchesOriginal()
    {
        var schedule = new NwcTimeLinerSchedule("Commercial Tower 4D");

        var phase1 = new NwcTimeLinerTask("T1", "Substructure",
            DateTimeOffset.Parse("2026-03-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-05-30T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 250000m,
            Progress = 1.0,
            ActualStart = DateTimeOffset.Parse("2026-03-02T08:00:00Z", CultureInfo.InvariantCulture),
            ActualEnd = DateTimeOffset.Parse("2026-05-28T17:00:00Z", CultureInfo.InvariantCulture),
            ActualCost = 245000m,
            AttachedSelectionSetName = "Foundation Piles"
        };
        phase1.AttachedNodeIds.AddRange(Phase1Nodes);

        var subtaskA = new NwcTimeLinerTask("T1.1", "Excavation",
            DateTimeOffset.Parse("2026-03-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-03-25T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Temporary);

        var subtaskB = new NwcTimeLinerTask("T1.2", "Piling Rig Demobilization",
            DateTimeOffset.Parse("2026-05-20T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-05-30T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Demolish);

        phase1.Children.Add(subtaskA);
        phase1.Children.Add(subtaskB);
        schedule.Tasks.Add(phase1);

        var phase2 = new NwcTimeLinerTask("T2", "Superstructure Concrete",
            DateTimeOffset.Parse("2026-06-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-10-31T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 1200000m,
            Progress = 0.35,
            AttachedSelectionSetName = "Columns and Slabs"
        };
        phase2.AttachedNodeIds.AddRange(Phase2Nodes);
        schedule.Tasks.Add(phase2);

        // Serialize to stream
        using var ms = new MemoryStream();
        NwcTimeLinerXmlSerializer.Write(schedule, ms);
        ms.Position = 0;

        // Deserialize back
        var roundtripped = NwcTimeLinerXmlSerializer.Read(ms);

        Assert.NotNull(roundtripped);
        Assert.Equal("Commercial Tower 4D", roundtripped.Name);
        Assert.Equal(2, roundtripped.Tasks.Count);

        var t1 = roundtripped.Tasks[0];
        Assert.Equal("T1", t1.Id);
        Assert.Equal("Substructure", t1.Name);
        Assert.Equal(NwcTimeLinerTaskType.Construct, t1.TaskType);
        Assert.Equal(250000m, t1.PlannedCost);
        Assert.Equal(245000m, t1.ActualCost);
        Assert.Equal(1.0, t1.Progress);
        Assert.Equal("Foundation Piles", t1.AttachedSelectionSetName);
        Assert.Equal(Phase1Nodes, t1.AttachedNodeIds);
        Assert.Equal(2, t1.Children.Count);

        var t1_1 = t1.Children[0];
        Assert.Equal("T1.1", t1_1.Id);
        Assert.Equal("Excavation", t1_1.Name);
        Assert.Equal(NwcTimeLinerTaskType.Temporary, t1_1.TaskType);

        var t1_2 = t1.Children[1];
        Assert.Equal("T1.2", t1_2.Id);
        Assert.Equal("Piling Rig Demobilization", t1_2.Name);
        Assert.Equal(NwcTimeLinerTaskType.Demolish, t1_2.TaskType);

        var t2 = roundtripped.Tasks[1];
        Assert.Equal("T2", t2.Id);
        Assert.Equal("Superstructure Concrete", t2.Name);
        Assert.Equal(1200000m, t2.PlannedCost);
        Assert.Equal(0.35, t2.Progress);
        Assert.Equal("Columns and Slabs", t2.AttachedSelectionSetName);
        Assert.Equal(Phase2Nodes, t2.AttachedNodeIds);
    }

    [Fact]
    public void AutoLink_WithSelectionSets_AttachesNodeIds()
    {
        var schedule = new NwcTimeLinerSchedule("AutoLink Test");
        var taskPiles = new NwcTimeLinerTask("1", "Piles",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(10))
        {
            AttachedSelectionSetName = "Set_Piles"
        };
        var taskColumns = new NwcTimeLinerTask("2", "Columns",
            DateTimeOffset.UtcNow.AddDays(11), DateTimeOffset.UtcNow.AddDays(30))
        {
            AttachedSelectionSetName = "Set_Columns"
        };
        schedule.Tasks.Add(taskPiles);
        schedule.Tasks.Add(taskColumns);

        var setPiles = new NwcSelectionSet("Set_Piles", SetPilesNodes);
        var setColumns = new NwcSelectionSet("Set_Columns", SetColumnsNodes);

        var sets = new List<NwcSetItem> { setPiles, setColumns };

        int linked = NwcTimeLinerLinker.AutoLink(schedule, sets);

        Assert.Equal(2, linked);
        Assert.Equal(SetPilesNodes, taskPiles.AttachedNodeIds);
        Assert.Equal(SetColumnsNodes, taskColumns.AttachedNodeIds);
    }

    [Fact]
    public void LinkByNodeName_FindsMatchingSceneNodes()
    {
        var schedule = new NwcTimeLinerSchedule("NameLink Test");
        var taskRoof = new NwcTimeLinerTask("R1", "Roof Truss",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(5));
        schedule.Tasks.Add(taskRoof);

        var root = new NwcNode { Id = 1, Name = "Model", Class = "Root" };
        var level1 = new NwcNode { Id = 2, Name = "Level 1", Class = "Storey" };
        var truss1 = new NwcNode { Id = 10, Name = "Steel Roof Truss A", Class = "Element" };
        var truss2 = new NwcNode { Id = 11, Name = "Steel Roof Truss B", Class = "Element" };
        var wall = new NwcNode { Id = 12, Name = "Curtain Wall", Class = "Element" };

        level1.Children.Add(truss1);
        level1.Children.Add(truss2);
        level1.Children.Add(wall);
        root.Children.Add(level1);

        int linked = NwcTimeLinerLinker.LinkByNodeName(schedule, root, exactMatch: false);

        Assert.Equal(1, linked);
        Assert.Equal(2, taskRoof.AttachedNodeIds.Count);
        Assert.Contains(10, taskRoof.AttachedNodeIds);
        Assert.Contains(11, taskRoof.AttachedNodeIds);
    }
}
