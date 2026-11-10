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
}
