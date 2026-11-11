using System.Globalization;
using NwcIO.Cli;
using NwcIO.Model;
using NwcIO.Model.TimeLiner;
using NwcIO.Model.Viewer;
using Xunit;

namespace NwcIO.Tests;

public class TimeLinerMsProjectAndPrimaveraTests
{
    [Fact]
    public void MsProjectXml_Roundtrip_PreservesHierarchyDatesCostsAndNotes()
    {
        var schedule = new NwcTimeLinerSchedule("Skyscraper Phase 1");

        var parentTask = new NwcTimeLinerTask("WBS-1", "Superstructure",
            DateTimeOffset.Parse("2026-04-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-09-30T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 500000m,
            ActualCost = 480000m,
            Progress = 0.85,
            ActualStart = DateTimeOffset.Parse("2026-04-02T08:00:00Z", CultureInfo.InvariantCulture),
            ActualEnd = DateTimeOffset.Parse("2026-09-28T17:00:00Z", CultureInfo.InvariantCulture),
            AttachedSelectionSetName = "Core Walls"
        };

        var childTask = new NwcTimeLinerTask("WBS-1.1", "Level 1 Columns",
            DateTimeOffset.Parse("2026-04-05T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-04-20T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Temporary)
        {
            PlannedCost = 120000m,
            Progress = 1.0,
            AttachedSelectionSetName = "L1-Cols"
        };

        parentTask.Children.Add(childTask);
        schedule.Tasks.Add(parentTask);

        using var ms = new MemoryStream();
        NwcTimeLinerMsProjectXmlSerializer.Write(schedule, ms);
        ms.Position = 0;

        string xmlContent = new StreamReader(ms).ReadToEnd();
        Assert.Contains("http://schemas.microsoft.com/project", xmlContent);
        Assert.Contains("<Name>Skyscraper Phase 1</Name>", xmlContent);
        Assert.Contains("<Name>Superstructure</Name>", xmlContent);
        Assert.Contains("<Name>Level 1 Columns</Name>", xmlContent);
        Assert.Contains("<OutlineLevel>1</OutlineLevel>", xmlContent);
        Assert.Contains("<OutlineLevel>2</OutlineLevel>", xmlContent);
        Assert.Contains("Set: Core Walls", xmlContent);
        Assert.Contains("Set: L1-Cols", xmlContent);

        // Deserialize back
        ms.Position = 0;
        var imported = NwcTimeLinerMsProjectXmlSerializer.Read(ms);

        Assert.NotNull(imported);
        Assert.Equal("Skyscraper Phase 1", imported.Name);
        Assert.Single(imported.Tasks);

        var t1 = imported.Tasks[0];
        Assert.Equal("Superstructure", t1.Name);
        Assert.Equal("WBS-1", t1.Id);
        Assert.Equal(500000m, t1.PlannedCost);
        Assert.Equal(480000m, t1.ActualCost);
        Assert.Equal(0.85, t1.Progress, precision: 2);
        Assert.Equal("Core Walls", t1.AttachedSelectionSetName);
        Assert.Equal(NwcTimeLinerTaskType.Construct, t1.TaskType);
        Assert.Single(t1.Children);

        var c1 = t1.Children[0];
        Assert.Equal("Level 1 Columns", c1.Name);
        Assert.Equal("WBS-1.1", c1.Id);
        Assert.Equal(120000m, c1.PlannedCost);
        Assert.Equal(1.0, c1.Progress, precision: 2);
        Assert.Equal("L1-Cols", c1.AttachedSelectionSetName);
        Assert.Equal(NwcTimeLinerTaskType.Temporary, c1.TaskType);
    }

    [Fact]
    public void PrimaveraXer_Roundtrip_PreservesTasksDatesAndCosts()
    {
        var schedule = new NwcTimeLinerSchedule("Hospital Block A");

        var task1 = new NwcTimeLinerTask("ACT-100", "Site Preparation",
            DateTimeOffset.Parse("2026-05-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-05-15T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 35000m,
            ActualCost = 34000m,
            Progress = 1.0,
            ActualStart = DateTimeOffset.Parse("2026-05-02T08:00:00Z", CultureInfo.InvariantCulture),
            ActualEnd = DateTimeOffset.Parse("2026-05-14T17:00:00Z", CultureInfo.InvariantCulture)
        };

        var task2 = new NwcTimeLinerTask("ACT-200", "Foundation Piling",
            DateTimeOffset.Parse("2026-05-16T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-06-30T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 180000m,
            Progress = 0.50
        };

        schedule.Tasks.Add(task1);
        schedule.Tasks.Add(task2);

        using var ms = new MemoryStream();
        NwcTimeLinerPrimaveraXerSerializer.Write(schedule, ms);
        ms.Position = 0;

        string xerContent = new StreamReader(ms).ReadToEnd();
        Assert.Contains("ERMHDR", xerContent);
        Assert.Contains("%T\tPROJECT", xerContent);
        Assert.Contains("%T\tPROJWBS", xerContent);
        Assert.Contains("%T\tTASK", xerContent);
        Assert.Contains("ACT-100", xerContent);
        Assert.Contains("Site Preparation", xerContent);
        Assert.Contains("ACT-200", xerContent);
        Assert.Contains("Foundation Piling", xerContent);

        // Deserialize back
        ms.Position = 0;
        var imported = NwcTimeLinerPrimaveraXerSerializer.Read(ms);

        Assert.NotNull(imported);
        Assert.Equal("Hospital Block A", imported.Name);
        Assert.Equal(2, imported.Tasks.Count);

        var r1 = imported.Tasks[0];
        Assert.Equal("ACT-100", r1.Id);
        Assert.Equal("Site Preparation", r1.Name);
        Assert.Equal(35000m, r1.PlannedCost);
        Assert.Equal(34000m, r1.ActualCost);
        Assert.Equal(1.0, r1.Progress, precision: 2);

        var r2 = imported.Tasks[1];
        Assert.Equal("ACT-200", r2.Id);
        Assert.Equal("Foundation Piling", r2.Name);
        Assert.Equal(180000m, r2.PlannedCost);
        Assert.Equal(0.50, r2.Progress, precision: 2);
    }

    [Fact]
    public void NwcDocument_ExportAndImportTimeLiner_MsProjectAndPrimavera()
    {
        var doc = NwcDocument.Create(
            fileInfo: new NwcFileInfo { SourcePath = "Test.nwc" },
            meshes: Array.Empty<NwcMesh>());

        var schedule = new NwcTimeLinerSchedule("Facility 4D");
        var task = new NwcTimeLinerTask("T01", "HVAC Rough-In",
            DateTimeOffset.Parse("2026-07-01T08:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-25T17:00:00Z", CultureInfo.InvariantCulture),
            NwcTimeLinerTaskType.Construct)
        {
            PlannedCost = 45000m,
            Progress = 0.4
        };
        schedule.Tasks.Add(task);
        doc.TimeLiner = schedule;

        // Test MS Project
        using var msMsp = new MemoryStream();
        doc.ExportTimeLinerMsProject(msMsp);
        msMsp.Position = 0;

        var tempMspPath = Path.Combine(Path.GetTempPath(), $"nwcio_test_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllBytes(tempMspPath, msMsp.ToArray());
            var doc2 = NwcDocument.Create(new NwcFileInfo { SourcePath = "Test2.nwc" }, Array.Empty<NwcMesh>());
            doc2.ImportTimeLinerMsProject(tempMspPath);

            Assert.NotNull(doc2.TimeLiner);
            Assert.Equal("Facility 4D", doc2.TimeLiner.Name);
            Assert.Single(doc2.TimeLiner.Tasks);
            Assert.Equal("HVAC Rough-In", doc2.TimeLiner.Tasks[0].Name);
            Assert.Equal(45000m, doc2.TimeLiner.Tasks[0].PlannedCost);
        }
        finally
        {
            if (File.Exists(tempMspPath)) File.Delete(tempMspPath);
        }

        // Test Primavera P6
        using var msP6 = new MemoryStream();
        doc.ExportTimeLinerPrimavera(msP6);
        msP6.Position = 0;

        var tempP6Path = Path.Combine(Path.GetTempPath(), $"nwcio_test_{Guid.NewGuid():N}.xer");
        try
        {
            File.WriteAllBytes(tempP6Path, msP6.ToArray());
            var doc3 = NwcDocument.Create(new NwcFileInfo { SourcePath = "Test3.nwc" }, Array.Empty<NwcMesh>());
            doc3.ImportTimeLinerPrimavera(tempP6Path);

            Assert.NotNull(doc3.TimeLiner);
            Assert.Equal("Facility 4D", doc3.TimeLiner.Name);
            Assert.Single(doc3.TimeLiner.Tasks);
            Assert.Equal("HVAC Rough-In", doc3.TimeLiner.Tasks[0].Name);
        }
        finally
        {
            if (File.Exists(tempP6Path)) File.Delete(tempP6Path);
        }
    }

    private static readonly string[] HelpArgs = ["--help"];

    [Fact]
    public void NwcHtmlViewer_ContainsSectionBoxAndCameraAnimations()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)],
            Indices = [0, 1, 2]
        };

        var vp1 = new NwcSavedViewpoint
        {
            Name = "Entrance View",
            Camera = new NwcCamera
            {
                Position = new Vector3d(10, 10, 10),
                Rotation = Quaterniond.Identity,
                FocalDistance = 10.0
            }
        };

        var vp2 = new NwcSavedViewpoint
        {
            Name = "Rooftop View",
            Camera = new NwcCamera
            {
                Position = new Vector3d(0, 0, 30),
                Rotation = Quaterniond.Identity,
                FocalDistance = 15.0
            }
        };

        var doc = new NwcDocumentBuilder()
            .SetFileInfo("ComplexModel.nwc")
            .AddMesh(mesh)
            .AddSavedViewpoint(vp1)
            .AddSavedViewpoint(vp2)
            .Build();

        string tempPath = Path.Combine(Path.GetTempPath(), $"nwcio_viewer_{Guid.NewGuid():N}.html");
        try
        {
            doc.SaveHtmlViewer(tempPath, new NwcHtmlViewerOptions { Title = "3D Viewer Showcase" });
            string html = File.ReadAllText(tempPath);

            // Section Box elements
            Assert.Contains("sec-mode-planes", html);
            Assert.Contains("sec-mode-box", html);
            Assert.Contains("clip-box-en", html);
            Assert.Contains("clip-box-invert", html);
            Assert.Contains("btn-fit-box", html);
            Assert.Contains("btn-reset-box", html);
            Assert.Contains("clip-box-scale", html);
            Assert.Contains("uClipBoxMin", html);
            Assert.Contains("uClipBoxMax", html);

            // Camera animation & turntable & walkthrough tour elements
            Assert.Contains("btn-turntable", html);
            Assert.Contains("toggleTurntable", html);
            Assert.Contains("animateTurntable", html);
            Assert.Contains("btn-play-tour", html);
            Assert.Contains("startTour", html);
            Assert.Contains("stopTour", html);
            Assert.Contains("playNextTourStop", html);
            Assert.Contains("animateCameraTo", html);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Cli_UsageHelp_DisplaysNewTimeLinerFlags()
    {
        using var sw = new StringWriter();
        var origOut = Console.Out;
        try
        {
            Console.SetOut(sw);
            int code = Program.Main(HelpArgs);
            Assert.Equal(0, code);
            string output = sw.ToString();
            Assert.Contains("--timeliner-msproject", output);
            Assert.Contains("--timeliner-primavera", output);
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }
}
