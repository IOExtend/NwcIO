using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model.TimeLiner;
using Xunit;

namespace NwcIO.Tests;

public class TimeLinerTests
{
    [Fact]
    public void TimeLinerSchedule_NwcRoundtrip_PreservesTaskTreeAndMetadata()
    {
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var parent = new NwcTimeLinerTask("P", "Phase 1", start, start.AddDays(30))
        {
            Progress = 0.5,
        };
        parent.AttachedNodeIds.AddRange([7, 8]);
        var child = new NwcTimeLinerTask("C", "Child", start, start.AddDays(10))
        {
            Progress = 1.0,
        };
        child.AttachedNodeIds.Add(9);
        parent.Children.Add(child);
        var multiline = new NwcTimeLinerTask("M", "Line1\nLine2\r\nLine3", start, start.AddDays(5));
        var schedule = new NwcTimeLinerSchedule("My Schedule", [parent, multiline]);
        var doc = new NwcDocumentBuilder()
            .SetFileInfo("ScheduleModel.nwc")
            .SetTimeLiner(schedule)
            .Build();

        using var ms = new MemoryStream();
        doc.Save(ms);
        ms.Position = 0;

        var loaded = NwcDocument.Open(ms).TimeLiner;
        Assert.NotNull(loaded);
        Assert.Equal(schedule.Name, loaded.Name);
        Assert.Equal(2, loaded.Tasks.Count);
        Assert.Equal(3, loaded.FlattenTasks().Count());
        var loadedParent = loaded.Tasks[0];
        Assert.Equal(parent.Id, loadedParent.Id);
        Assert.Equal(parent.Name, loadedParent.Name);
        Assert.Equal(parent.Progress, loadedParent.Progress);
        Assert.Equal(parent.AttachedNodeIds, loadedParent.AttachedNodeIds);
        var loadedChild = Assert.Single(loadedParent.Children);
        Assert.Equal(child.Id, loadedChild.Id);
        Assert.Equal(child.Name, loadedChild.Name);
        Assert.Equal(child.Progress, loadedChild.Progress);
        Assert.Equal(child.AttachedNodeIds, loadedChild.AttachedNodeIds);
        Assert.Empty(loadedChild.Children);
        Assert.Equal(multiline.Id, loaded.Tasks[1].Id);
        Assert.Equal(multiline.Name, loaded.Tasks[1].Name);
    }

    [Fact]
    public void TimeLinerSchedule_OpensLegacyEmbeddedCsv()
    {
        // Construct the old binary payload independently of the current writer.
        var csv = "\uFEFFTask ID,Task Name,Task Type,Planned Start,Planned End,Actual Start,Actual End,Planned Cost,ActualCost,Attached Set,Progress\r\n"
            + "A1,Groundworks,Construct,2026-09-01 00:00:00,2026-09-15 00:00:00,,,75000.00,,Groundwork Elements,50 %\r\n";
        var csvBytes = Encoding.UTF8.GetBytes(csv);
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(100);
            writer.Write(106);
            writer.Write(1);
            writer.Write(csvBytes.Length);
            writer.Write(csvBytes);
        }

        using var ms = new MemoryStream();
        NwcContainerWriter.Write(ms, [payload.ToArray()]);
        ms.Position = 0;

        var loaded = NwcDocument.Open(ms).TimeLiner;
        Assert.NotNull(loaded);
        Assert.Equal("TimeLiner Schedule", loaded.Name);
        var task = Assert.Single(loaded.Tasks);
        Assert.Equal("A1", task.Id);
        Assert.Equal("Groundworks", task.Name);
        Assert.Equal(NwcTimeLinerTaskType.Construct, task.TaskType);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), task.PlannedStart);
        Assert.Equal(task.PlannedStart.AddDays(14), task.PlannedEnd);
        Assert.Equal(75000m, task.PlannedCost);
        Assert.Equal("Groundwork Elements", task.AttachedSelectionSetName);
    }

    [Fact]
    public void TimeLinerSchedule_ComputesDatesAndProgress()
    {
        var baseDate = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

        var task1 = new NwcTimeLinerTask("T1", "Excavation", baseDate, baseDate.AddDays(10), NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 50000m,
        };
        task1.AttachedNodeIds.Add(101);

        var task2 = new NwcTimeLinerTask("T2", "Foundations", baseDate.AddDays(10), baseDate.AddDays(25), NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 120000m,
        };
        task2.AttachedNodeIds.Add(102);

        var schedule = new NwcTimeLinerSchedule([task1, task2]);

        Assert.Equal(baseDate, schedule.OverallStart);
        Assert.Equal(baseDate.AddDays(25), schedule.OverallEnd);
        Assert.Equal(170000m, schedule.TotalPlannedCost);

        // Simulation query on day 5 (during task 1)
        var midDate = baseDate.AddDays(5);
        var active = schedule.GetActiveTasksAt(midDate);
        var activeTask = Assert.Single(active);
        Assert.Equal("Excavation", activeTask.Name);

        // Simulation states for node 101 and 102
        Assert.Equal(NwcTimeLinerSimulationState.InProgress, schedule.GetNodeStateAt(101, midDate));
        Assert.Equal(NwcTimeLinerSimulationState.NotStarted, schedule.GetNodeStateAt(102, midDate));

        // Simulation query on day 15 (after task 1, during task 2)
        var laterDate = baseDate.AddDays(15);
        Assert.Equal(NwcTimeLinerSimulationState.Completed, schedule.GetNodeStateAt(101, laterDate));
        Assert.Equal(NwcTimeLinerSimulationState.InProgress, schedule.GetNodeStateAt(102, laterDate));

        // Simulation overall progress: 5 days / 25 days = 20%
        double progress = schedule.GetSimulationProgressAt(midDate);
        Assert.Equal(0.2, progress, precision: 2);
    }

    [Fact]
    public void TimeLinerSchedule_RoundtripsThroughCsv()
    {
        var baseDate = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule(
        [
            new NwcTimeLinerTask("1", "Demolish Old Shed", baseDate, baseDate.AddDays(3), NwcTimeLinerTaskType.Demolish)
            {
                PlannedCost = 15000m,
                ActualCost = 14200m,
                AttachedSelectionSetName = "ShedElements"
            },
            new NwcTimeLinerTask("2", "Erect Steel Frame", baseDate.AddDays(4), baseDate.AddDays(20), NwcTimeLinerTaskType.Construct)
            {
                PlannedCost = 350000m,
                AttachedSelectionSetName = "SteelStructure"
            }
        ]);

        using var ms = new MemoryStream();
        NwcTimeLinerCsvSerializer.Write(schedule, ms);
        ms.Position = 0;

        var loaded = NwcTimeLinerCsvSerializer.Read(ms);
        Assert.Equal(2, loaded.Tasks.Count);

        var t1 = loaded.Tasks[0];
        Assert.Equal("1", t1.Id);
        Assert.Equal("Demolish Old Shed", t1.Name);
        Assert.Equal(NwcTimeLinerTaskType.Demolish, t1.TaskType);
        Assert.Equal(15000m, t1.PlannedCost);
        Assert.Equal(14200m, t1.ActualCost);
        Assert.Equal("ShedElements", t1.AttachedSelectionSetName);

        var t2 = loaded.Tasks[1];
        Assert.Equal("2", t2.Id);
        Assert.Equal("Erect Steel Frame", t2.Name);
        Assert.Equal(NwcTimeLinerTaskType.Construct, t2.TaskType);
        Assert.Equal(350000m, t2.PlannedCost);
    }

    [Fact]
    public void TimeLinerSchedule_RoundtripsThroughNwcFile()
    {
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule(
        [
            new NwcTimeLinerTask("A1", "Groundworks", start, start.AddDays(14)) { PlannedCost = 75000m }
        ]);

        var doc = new NwcDocumentBuilder()
            .SetFileInfo("ScheduleModel.nwc")
            .SetTimeLiner(schedule)
            .Build();

        using var ms = new MemoryStream();
        doc.Save(ms);
        ms.Position = 0;

        var reopened = NwcDocument.Open(ms);
        Assert.NotNull(reopened.TimeLiner);
        var task = Assert.Single(reopened.TimeLiner.Tasks);
        Assert.Equal("A1", task.Id);
        Assert.Equal("Groundworks", task.Name);
        Assert.Equal(75000m, task.PlannedCost);
    }
}
