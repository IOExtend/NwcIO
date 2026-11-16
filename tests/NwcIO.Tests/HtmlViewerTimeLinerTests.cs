using System.Globalization;
using NwcIO.Model;
using NwcIO.Model.Sets;
using NwcIO.Model.TimeLiner;
using NwcIO.Model.Viewer;
using Xunit;

namespace NwcIO.Tests;

public class HtmlViewerTimeLinerTests
{
    private static readonly int[] FoundationNodes = [10];
    private static readonly int[] SteelNodes = [20];

    [Fact]
    public void ExportHtml_WithTimeLinerAndSelectionSets_IncludesControlsAndData()
    {
        var node1 = new NwcNode { Id = 10, Name = "Foundations", Class = "Substructure" };
        var node2 = new NwcNode { Id = 20, Name = "Steel Columns", Class = "Superstructure" };

        var mesh1 = new NwcMesh
        {
            Id = 1,
            Vertices = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) },
            Indices = new[] { 0, 1, 2 },
            Node = node1
        };
        var mesh2 = new NwcMesh
        {
            Id = 2,
            Vertices = new[] { new Vector3d(2, 0, 0), new Vector3d(3, 0, 0), new Vector3d(2, 1, 0) },
            Indices = new[] { 0, 1, 2 },
            Node = node2
        };

        var schedule = new NwcTimeLinerSchedule("4D Simulation");
        var task1 = new NwcTimeLinerTask("T1", "Pour Foundations",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-01-15T00:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct);
        task1.AttachedNodeIds.Add(10);

        var task2 = new NwcTimeLinerTask("T2", "Erect Steel",
            DateTimeOffset.Parse("2026-01-16T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-02-15T00:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct);
        task2.AttachedNodeIds.Add(20);

        schedule.Tasks.Add(task1);
        schedule.Tasks.Add(task2);

        var setFoundations = new NwcSelectionSet("Set Foundations", FoundationNodes);
        var setSteel = new NwcSelectionSet("Set Steel", SteelNodes);
        var folder = new NwcSelectionSetFolder("Structural Sets");
        folder.Children.Add(setFoundations);
        folder.Children.Add(setSteel);

        var doc = NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = "Project.nwc" },
            meshes: new[] { mesh1, mesh2 },
            selectionSets: new NwcSetItem[] { folder },
            timeLiner: schedule);

        string tempHtml = Path.Combine(Path.GetTempPath(), $"viewer_{Guid.NewGuid():N}.html");
        try
        {
            doc.SaveHtmlViewer(tempHtml);
            Assert.True(File.Exists(tempHtml));

            string html = File.ReadAllText(tempHtml);

            // Verify HTML markup and controls
            Assert.Contains("id=\"btn-timeliner\"", html);
            Assert.Contains("id=\"timeliner-bar\"", html);
            Assert.Contains("id=\"tl-play\"", html);
            Assert.Contains("id=\"tl-slider\"", html);
            Assert.Contains("id=\"tab-btn-sets\"", html);
            Assert.Contains("id=\"tab-sets\"", html);

            // Verify embedded JSON payload
            Assert.Contains("\"timeliner\":", html);
            Assert.Contains("Pour Foundations", html);
            Assert.Contains("Erect Steel", html);
            Assert.Contains("\"selectionSets\":", html);
            Assert.Contains("Structural Sets", html);
            Assert.Contains("Set Foundations", html);
        }
        finally
        {
            if (File.Exists(tempHtml)) File.Delete(tempHtml);
        }
    }

    [Fact]
    public void ExportHtml_TimeLinerWritesScheduleOffset()
    {
        string html = ExportHtml(MixedTypeSchedule("+03:00"));
        Assert.Contains("\"offsetMinutes\":180", html);
    }

    [NodeFact]
    public void ExportHtml_TimeLinerScript_ComputesNodeStateByDrivingTask()
    {
        // Node 10: Construct 1-10 Jun, Demolish 20-30 Jun. Node 20: Construct 1-10 Jun and 5-20 Jun.
        string html = ExportHtml(MixedTypeSchedule("Z"));
        int start = html.IndexOf("function tlEffectiveTime", StringComparison.Ordinal);
        int end = html.IndexOf("// end tl helpers", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        string helpers = html.Substring(start, end - start);

        string script = helpers + @"
const d = s => new Date('2026-06-' + s + 'T00:00:00Z').getTime();
const T = (type, a, b) => ({ taskType: type, start: new Date(d(a)).toISOString(), end: new Date(d(b)).toISOString() });
const mixed = [T('Construct', '01', '10'), T('Demolish', '20', '30')];
const overlap = [T('Construct', '01', '10'), T('Construct', '05', '20')];
const st = (tasks, day) => { const r = computeTlNodeState(tasks, d(day)); return r.type + ':' + r.state; };
const out = [
  st(mixed, '05'), st(mixed, '15'), st(mixed, '25'), st(mixed, '30'),
  computeTlNodeState(mixed, d('30') + 1).type + ':' + computeTlNodeState(mixed, d('30') + 1).state,
  st(overlap, '10'), st(overlap, '15'), st(overlap, '20'),
  tlEffectiveTime(new Date(d('30')), 1000) - d('30'), tlEffectiveTime(new Date(d('30')), 999) - d('30'),
];
console.log(JSON.stringify(out));
";
        string output = RunNode(script);
        Assert.Equal(
            "[\"Construct:InProgress\",\"Construct:Completed\",\"Demolish:InProgress\",\"Demolish:InProgress\"," +
            "\"Demolish:Completed\",\"Construct:InProgress\",\"Construct:InProgress\",\"Construct:InProgress\",1,0]",
            output.Trim());
    }

    private static NwcTimeLinerSchedule MixedTypeSchedule(string offset)
    {
        DateTimeOffset At(string day) =>
            DateTimeOffset.Parse($"2026-06-{day}T00:00:00{offset}", CultureInfo.InvariantCulture);

        var schedule = new NwcTimeLinerSchedule("Mixed");
        var build = new NwcTimeLinerTask("T1", "Build", At("01"), At("10"), NwcTimeLinerTaskType.Construct);
        var demolish = new NwcTimeLinerTask("T2", "Demolish", At("20"), At("30"), NwcTimeLinerTaskType.Demolish);
        build.AttachedNodeIds.Add(10);
        demolish.AttachedNodeIds.Add(10);
        schedule.Tasks.Add(build);
        schedule.Tasks.Add(demolish);
        return schedule;
    }

    private static string ExportHtml(NwcTimeLinerSchedule schedule)
    {
        var node = new NwcNode { Id = 10, Name = "Shared", Class = "Element" };
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            Node = node,
        };
        var doc = NwcDocument.Create(meshes: [mesh], timeLiner: schedule);
        string path = Path.Combine(Path.GetTempPath(), $"viewer_{Guid.NewGuid():N}.html");
        try
        {
            doc.SaveHtmlViewer(path);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string RunNode(string script)
    {
        string file = Path.Combine(Path.GetTempPath(), $"tl_{Guid.NewGuid():N}.js");
        File.WriteAllText(file, script);
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", $"\"{file}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stderr);
            return stdout;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class NodeFactAttribute : FactAttribute
    {
        public NodeFactAttribute()
        {
            try
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("node", "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                })!;
                p.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Skip = "node is not installed";
            }
        }
    }
}
