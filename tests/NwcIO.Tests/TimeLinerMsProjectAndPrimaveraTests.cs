using System.Globalization;
using NwcIO.Cli;
using NwcIO.Model;
using NwcIO.Model.Sets;
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

    [Theory]
    [InlineData("--timeliner-xml", false)]
    [InlineData("--timeliner-xml", true)]
    [InlineData("--timeliner-msproject", false)]
    [InlineData("--timeliner-msproject", true)]
    [InlineData("--timeliner-primavera", false)]
    [InlineData("--timeliner-primavera", true)]
    public void Cli_ImportsAndLinksScheduleBeforeCsvAndNwcExports(string importOption, bool hasEmbeddedSchedule)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "nwcio-cli-timeliner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
            var imported = new NwcTimeLinerSchedule("Imported", [new NwcTimeLinerTask("1", "Imported Task", start, start.AddDays(2))]);
            var input = new NwcDocumentBuilder()
                .AddSelectionSet(new NwcSelectionSet("Imported Task", [42]))
                .Build();
            if (hasEmbeddedSchedule)
            {
                input.TimeLiner = new NwcTimeLinerSchedule("Stale", [new NwcTimeLinerTask("2", "Stale Task", start, start.AddDays(1))]);
            }
            string inputPath = Path.Combine(tempDir, "input.nwc");
            input.Save(inputPath);

            string schedulePath = Path.Combine(tempDir, importOption == "--timeliner-primavera" ? "schedule.xer" : "schedule.xml");
            switch (importOption)
            {
                case "--timeliner-xml": NwcTimeLinerXmlSerializer.Save(imported, schedulePath); break;
                case "--timeliner-msproject": NwcTimeLinerMsProjectXmlSerializer.Save(imported, schedulePath); break;
                case "--timeliner-primavera": NwcTimeLinerPrimaveraXerSerializer.Save(imported, schedulePath); break;
                default: throw new ArgumentException("Unknown import option.", nameof(importOption));
            }

            string primaryCsv = Path.Combine(tempDir, "primary.csv");
            string extraCsv = Path.Combine(tempDir, "extra.csv");
            string outputNwc = Path.Combine(tempDir, "linked.nwc");
            int code = Program.Main([inputPath, primaryCsv, importOption, schedulePath,
                "--link-timeliner", "--timeliner-csv", extraCsv, "--nwc", outputNwc]);

            Assert.Equal(0, code);
            foreach (string csv in new[] { primaryCsv, extraCsv })
            {
                var task = Assert.Single(NwcTimeLinerCsvSerializer.Open(csv).Tasks);
                Assert.Equal("Imported Task", task.Name);
                Assert.Equal("Imported Task", task.AttachedSelectionSetName);
            }
            var linked = NwcDocument.Open(outputNwc);
            Assert.NotNull(linked.TimeLiner);
            var linkedTask = Assert.Single(linked.TimeLiner.Tasks);
            Assert.Equal("Imported Task", linkedTask.Name);
            Assert.Equal("Imported Task", linkedTask.AttachedSelectionSetName);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Cli_InvalidTimeLinerXml_DoesNotOverwriteOutput()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "nwcio-cli-invalid-timeliner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string input = Path.Combine(tempDir, "input.nwc");
            string schedule = Path.Combine(tempDir, "schedule.xml");
            string output = Path.Combine(tempDir, "output.csv");
            NwcDocument.Create().Save(input);
            File.WriteAllText(schedule, "<invalid");
            File.WriteAllText(output, "existing output");

            Assert.Equal(3, Program.Main([input, output, "--timeliner-xml", schedule]));
            Assert.Equal("existing output", File.ReadAllText(output));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Csv_NonZeroOffset_WritesAndReadsUtcInstants()
    {
        var start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(3));
        var schedule = new NwcTimeLinerSchedule();
        schedule.Tasks.Add(new NwcTimeLinerTask("T1", "A", start, start.AddHours(9))
        {
            ActualStart = start.AddHours(1),
            ActualEnd = start.AddHours(4),
        });

        using var ms = new MemoryStream();
        NwcTimeLinerCsvSerializer.Write(schedule, ms);
        ms.Position = 0;
        string csv = new StreamReader(ms, leaveOpen: true).ReadToEnd();
        Assert.Contains("2026-06-01 05:00:00", csv);

        ms.Position = 0;
        var imported = NwcTimeLinerCsvSerializer.Read(ms);
        var task = Assert.Single(imported.Tasks);
        Assert.Equal(start, task.PlannedStart);
        Assert.Equal(start.AddHours(9), task.PlannedEnd);
        Assert.Equal(start.AddHours(1), task.ActualStart);
        Assert.Equal(start.AddHours(4), task.ActualEnd);
    }

    [Fact]
    public void PrimaveraXer_NonZeroOffset_WritesAndReadsUtcInstants()
    {
        var start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(3));
        var schedule = new NwcTimeLinerSchedule("Offset project");
        schedule.Tasks.Add(new NwcTimeLinerTask("T1", "A", start, start.AddHours(9))
        {
            ActualStart = start.AddHours(1),
            ActualEnd = start.AddHours(4),
        });

        using var ms = new MemoryStream();
        NwcTimeLinerPrimaveraXerSerializer.Write(schedule, ms);
        ms.Position = 0;
        string xer = new StreamReader(ms, leaveOpen: true).ReadToEnd();
        Assert.Contains("\t2026-06-01 05:00\t2026-06-01 14:00\tActive", xer); // PROJECT plan dates
        Assert.Contains("\t2026-06-01 05:00\t2026-06-01 14:00\t2026-06-01 06:00\t2026-06-01 09:00\t", xer); // TASK dates

        ms.Position = 0;
        var imported = NwcTimeLinerPrimaveraXerSerializer.Read(ms);
        var task = Assert.Single(imported.Tasks);
        Assert.Equal(start, task.PlannedStart);
        Assert.Equal(start.AddHours(9), task.PlannedEnd);
        Assert.Equal(start.AddHours(1), task.ActualStart);
        Assert.Equal(start.AddHours(4), task.ActualEnd);
    }

    [Fact]
    public void MsProjectXml_NonZeroOffset_WritesAndReadsUtcInstants()
    {
        var start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(3));
        var schedule = new NwcTimeLinerSchedule("Offset project");
        schedule.Tasks.Add(new NwcTimeLinerTask("T1", "A", start, start.AddHours(9))
        {
            ActualStart = start.AddHours(1),
            ActualEnd = start.AddHours(4),
        });

        using var ms = new MemoryStream();
        NwcTimeLinerMsProjectXmlSerializer.Write(schedule, ms);
        ms.Position = 0;
        string xml = new StreamReader(ms, leaveOpen: true).ReadToEnd();
        Assert.Contains("<StartDate>2026-06-01T05:00:00</StartDate>", xml);
        Assert.Contains("<FinishDate>2026-06-01T14:00:00</FinishDate>", xml);
        Assert.Contains("<Start>2026-06-01T05:00:00</Start>", xml);
        Assert.Contains("<Finish>2026-06-01T14:00:00</Finish>", xml);
        Assert.Contains("<ActualStart>2026-06-01T06:00:00</ActualStart>", xml);
        Assert.Contains("<ActualFinish>2026-06-01T09:00:00</ActualFinish>", xml);

        ms.Position = 0;
        var imported = NwcTimeLinerMsProjectXmlSerializer.Read(ms);
        var task = Assert.Single(imported.Tasks);
        Assert.Equal(start, task.PlannedStart);
        Assert.Equal(start.AddHours(9), task.PlannedEnd);
        Assert.Equal(start.AddHours(1), task.ActualStart);
        Assert.Equal(start.AddHours(4), task.ActualEnd);
    }

    [Fact]
    public void Cli_UsageHelp_DisplaysNewTimeLinerFlags()
    {
        using var sw = new StringWriter();
        int code = Program.Run(HelpArgs, outWriter: sw);
        Assert.Equal(0, code);
        string output = sw.ToString();
        Assert.Contains("--timeliner-msproject", output);
        Assert.Contains("--timeliner-primavera", output);
    }

    [Fact]
    public void MsProjectXml_MissingDatesAndId_UseDeterministicFallbacks()
    {
        const string xml = """
            <Project xmlns="http://schemas.microsoft.com/project">
              <Tasks>
                <Task><Name>A</Name></Task>
              </Tasks>
            </Project>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var read = NwcTimeLinerMsProjectXmlSerializer.Read(stream);

        var task = Assert.Single(read.Tasks);
        Assert.Equal("task-1", task.Id);
        Assert.Equal(DateTimeOffset.UnixEpoch, task.PlannedStart);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), task.PlannedEnd);
        Assert.Contains(read.Warnings, w => w.Contains("id is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(read.Warnings, w => w.Contains("start", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(read.Warnings, w => w.Contains("finish", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrimaveraXer_MissingDatesAndId_UseDeterministicFallbacks()
    {
        const string xer = "%T\tTASK\n%F\ttask_code\ttask_name\ttarget_start_date\ttarget_end_date\n%R\t\tA\t\t\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xer));

        var read = NwcTimeLinerPrimaveraXerSerializer.Read(stream);

        var task = Assert.Single(read.Tasks);
        Assert.Equal("task-1", task.Id);
        Assert.Equal(DateTimeOffset.UnixEpoch, task.PlannedStart);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), task.PlannedEnd);
        Assert.Contains(read.Warnings, w => w.Contains("id is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(read.Warnings, w => w.Contains("target start", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(read.Warnings, w => w.Contains("target end", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PrimaveraXer_WritesNoBomAndEndsWithTerminator()
    {
        var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule("P");
        schedule.Tasks.Add(new NwcTimeLinerTask("A", "A", start, start.AddDays(1)));

        using var ms = new MemoryStream();
        NwcTimeLinerPrimaveraXerSerializer.Write(schedule, ms);
        var bytes = ms.ToArray();

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("ERMHDR", text, StringComparison.Ordinal);
        Assert.EndsWith("%E", text.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaveraXer_NestedTasksAndTypes_Roundtrip()
    {
        var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var parent = new NwcTimeLinerTask("A", "Parent", start, start.AddDays(10));
        parent.Children.Add(new NwcTimeLinerTask("A1", "Child 1", start.AddDays(1), start.AddDays(3), NwcTimeLinerTaskType.Demolish));
        parent.Children.Add(new NwcTimeLinerTask("A2", "Child 2", start.AddDays(4), start.AddDays(6), NwcTimeLinerTaskType.Temporary));
        var schedule = new NwcTimeLinerSchedule("P");
        schedule.Tasks.Add(parent);

        using var ms = new MemoryStream();
        NwcTimeLinerPrimaveraXerSerializer.Write(schedule, ms);
        ms.Position = 0;
        var read = NwcTimeLinerPrimaveraXerSerializer.Read(ms);

        var root = Assert.Single(read.Tasks);
        Assert.Equal("Parent", root.Name);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("Child 1", root.Children[0].Name);
        Assert.Equal(NwcTimeLinerTaskType.Demolish, root.Children[0].TaskType);
        Assert.Equal("Child 2", root.Children[1].Name);
        Assert.Equal(NwcTimeLinerTaskType.Temporary, root.Children[1].TaskType);
    }

    [Fact]
    public void PrimaveraXer_ReadWithEncoding_DecodesNonUtf8File()
    {
        var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule("Café");
        schedule.Tasks.Add(new NwcTimeLinerTask("A", "Café", start, start.AddDays(1)));

        using var ms = new MemoryStream();
        NwcTimeLinerPrimaveraXerSerializer.Write(schedule, ms, System.Text.Encoding.Latin1);
        ms.Position = 0;
        var read = NwcTimeLinerPrimaveraXerSerializer.Read(ms, System.Text.Encoding.Latin1);

        Assert.Equal("Café", read.Name);
        Assert.Equal("Café", Assert.Single(read.Tasks).Name);
    }

    private static NwcTimeLinerSchedule ReadXer(string text)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return NwcTimeLinerPrimaveraXerSerializer.Read(ms);
    }

    private const string XerTaskFields = "%F\ttask_id\twbs_id\ttask_code\ttask_name\ttarget_start_date\ttarget_end_date";

    [Fact]
    public void PrimaveraXer_ForeignFileWithSharedWbs_StaysFlatWithWarning()
    {
        string xer = string.Join("\n",
            "%T\tPROJWBS", "%F\twbs_id\tparent_wbs_id\twbs_name",
            "%R\t100\t\tRoot", "%R\t101\t100\tPhase",
            "%T\tTASK", XerTaskFields,
            "%R\t1\t100\tMS1\tStart\t2026-06-01 00:00\t2026-06-01 00:00",
            "%R\t2\t100\tMS2\tFinish\t2026-06-10 00:00\t2026-06-10 00:00",
            "%R\t3\t101\tA1\tDig\t2026-06-02 00:00\t2026-06-03 00:00",
            "%E");

        var read = ReadXer(xer);

        Assert.Equal(["Start", "Finish", "Dig"], read.Tasks.Select(t => t.Name));
        Assert.All(read.Tasks, t => Assert.Empty(t.Children));
        Assert.Contains(read.Warnings, w => w.Contains("nwcio_task_type", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimaveraXer_SharedWbsIdInNwcioFile_StaysFlatWithWarning()
    {
        string xer = string.Join("\n",
            "%T\tPROJWBS", "%F\twbs_id\tparent_wbs_id\twbs_name",
            "%R\t100\t\tRoot", "%R\t101\t100\tA",
            "%T\tTASK", XerTaskFields + "\tnwcio_task_type",
            "%R\t1\t101\tA\tA\t2026-06-01 00:00\t2026-06-02 00:00\tConstruct",
            "%R\t2\t101\tB\tB\t2026-06-03 00:00\t2026-06-04 00:00\tConstruct",
            "%E");

        var read = ReadXer(xer);

        Assert.Equal(2, read.Tasks.Count);
        Assert.Contains(read.Warnings, w => w.Contains("shared by several tasks", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimaveraXer_ParentCycle_KeepsAllTasksAndWarns()
    {
        string xer = string.Join("\n",
            "%T\tPROJWBS", "%F\twbs_id\tparent_wbs_id\twbs_name",
            "%R\t1\t2\tX", "%R\t2\t1\tY",
            "%T\tTASK", XerTaskFields + "\tnwcio_task_type",
            "%R\t1\t1\tX\tX\t2026-06-01 00:00\t2026-06-02 00:00\tConstruct",
            "%R\t2\t2\tY\tY\t2026-06-03 00:00\t2026-06-04 00:00\tConstruct",
            "%E");

        var read = ReadXer(xer);

        int total = read.Tasks.Count + read.Tasks.Sum(t => t.Children.Count);
        Assert.Equal(2, total);
        Assert.NotEmpty(read.Tasks);
        Assert.Contains(read.Warnings, w => w.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimaveraXer_UndecodableBytes_AddWarning()
    {
        var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule("P");
        schedule.Tasks.Add(new NwcTimeLinerTask("A", "Café", start, start.AddDays(1)));
        using var ms = new MemoryStream();
        NwcTimeLinerPrimaveraXerSerializer.Write(schedule, ms, System.Text.Encoding.Latin1);
        ms.Position = 0;

        var read = NwcTimeLinerPrimaveraXerSerializer.Read(ms);

        Assert.Contains(read.Warnings, w => w.Contains("U+FFFD", StringComparison.Ordinal));
    }

    [Fact]
    public void PrimaveraXer_WriteUnrepresentableCharacter_Throws()
    {
        var start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule("P");
        schedule.Tasks.Add(new NwcTimeLinerTask("A", "Бетон", start, start.AddDays(1)));

        using var ms = new MemoryStream();
        Assert.ThrowsAny<System.Text.EncoderFallbackException>(() =>
            NwcTimeLinerPrimaveraXerSerializer.Write(schedule, ms, System.Text.Encoding.Latin1));
    }

    [Fact]
    public void MsProjectXml_ProjectSummaryTask_IsSkipped()
    {
        const string xml = """
            <Project xmlns="http://schemas.microsoft.com/project">
              <Name>P</Name>
              <Tasks>
                <Task><UID>0</UID><Name>P</Name><OutlineLevel>0</OutlineLevel><Start>2025-01-01T08:00:00</Start><Finish>2025-01-10T17:00:00</Finish></Task>
                <Task><UID>1</UID><Name>T1</Name><OutlineLevel>1</OutlineLevel><Start>2025-01-01T08:00:00</Start><Finish>2025-01-02T17:00:00</Finish></Task>
                <Task><UID>2</UID><Name>T2</Name><OutlineLevel>1</OutlineLevel><Start>2025-01-03T08:00:00</Start><Finish>2025-01-10T17:00:00</Finish></Task>
              </Tasks>
            </Project>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var schedule = NwcTimeLinerMsProjectXmlSerializer.Read(stream);

        Assert.Equal(["T1", "T2"], schedule.Tasks.Select(t => t.Name));
    }

    [Fact]
    public void MsProjectXml_Duration_KeepsHoursMinutesAndSeconds()
    {
        var start = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule("P");
        schedule.Tasks.Add(new NwcTimeLinerTask("A", "A", start, start.AddMinutes(90).AddSeconds(30)));

        using var ms = new MemoryStream();
        NwcTimeLinerMsProjectXmlSerializer.Write(schedule, ms);
        ms.Position = 0;
        string xml = new StreamReader(ms, leaveOpen: true).ReadToEnd();

        Assert.Contains("<Duration>PT1H30M30S</Duration>", xml);
    }

    [Fact]
    public void MsProjectXml_SetNameContainingPipe_Roundtrips()
    {
        var start = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero);
        var schedule = new NwcTimeLinerSchedule("P");
        schedule.Tasks.Add(new NwcTimeLinerTask("A", "A", start, start.AddHours(1))
        {
            AttachedSelectionSetName = "Level 1 | Walls",
            TaskType = NwcTimeLinerTaskType.Demolish,
        });

        using var ms = new MemoryStream();
        NwcTimeLinerMsProjectXmlSerializer.Write(schedule, ms);
        ms.Position = 0;
        var read = NwcTimeLinerMsProjectXmlSerializer.Read(ms);

        var task = Assert.Single(read.Tasks);
        Assert.Equal("Level 1 | Walls", task.AttachedSelectionSetName);
        Assert.Equal(NwcTimeLinerTaskType.Demolish, task.TaskType);
    }

    [Theory]
    [InlineData("Set: Core Walls | Type: Demolish", "Core Walls", NwcTimeLinerTaskType.Demolish)]
    [InlineData("Set: Core Walls", "Core Walls", NwcTimeLinerTaskType.Construct)]
    [InlineData("Type: Temporary", null, NwcTimeLinerTaskType.Temporary)]
    [InlineData(@"Set: Level\1 Walls", @"Level\1 Walls", NwcTimeLinerTaskType.Construct)]
    public void MsProjectXml_LegacyAndUnknownEscapeNotes_AreRead(string notes, string? expectedSet, NwcTimeLinerTaskType expectedType)
    {
        string xml = "<?xml version=\"1.0\"?><Project xmlns=\"http://schemas.microsoft.com/project\"><Tasks>"
            + "<Task><UID>1</UID><Name>A</Name><OutlineLevel>1</OutlineLevel>"
            + "<Start>2025-01-01T08:00:00</Start><Finish>2025-01-01T09:00:00</Finish>"
            + "<Notes>" + notes + "</Notes></Task></Tasks></Project>";
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

        var task = Assert.Single(NwcTimeLinerMsProjectXmlSerializer.Read(ms).Tasks);

        Assert.Equal(expectedSet, task.AttachedSelectionSetName);
        Assert.Equal(expectedType, task.TaskType);
    }

    [Fact]
    public void DateFallbackArithmetic_AtExtremes_Clamps()
    {
        Assert.Equal(DateTimeOffset.MaxValue, NwcTimeLinerDates.AddDays(DateTimeOffset.MaxValue, 1));
        Assert.Equal(DateTimeOffset.MinValue, NwcTimeLinerDates.AddDays(DateTimeOffset.MinValue, -1));
    }
}
