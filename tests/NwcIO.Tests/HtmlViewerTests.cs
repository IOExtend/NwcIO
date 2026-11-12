using NwcIO.Model;
using NwcIO.Model.Clash;
using NwcIO.Model.Viewer;
using Xunit;

namespace NwcIO.Tests;

public sealed class HtmlViewerTests
{
    [Fact]
    public void SaveHtmlViewer_Document_GeneratesSelfContainedHtmlViewer()
    {
        var node = new NwcNode
        {
            Id = 1,
            Name = "Wall 100mm",
            Class = "BasicWall",
        };
        node.Properties.Add(new NwcPropertyRecord
        {
            FirstInt = 1,
            Tag = 10,
            Name = "Volume",
            Class = "Dimensions",
            Value = "15.4 m3",
        });

        var mesh = new NwcMesh
        {
            Id = 100,
            Name = "WallGeometry",
            Vertices =
            [
                new(0, 0, 0), new(5, 0, 0), new(5, 0.2, 0), new(0, 0.2, 0),
                new(0, 0, 3), new(5, 0, 3), new(5, 0.2, 3), new(0, 0.2, 3),
            ],
            Indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(5, 0.2, 3)),
            Material = new NwcMaterial { Id = 1, Name = "Concrete", Color = new RgbaColor(180, 180, 180) },
            Node = node,
        };

        var vp = new NwcSavedViewpoint
        {
            Name = "Overview Entrance",
            Camera = new NwcCamera
            {
                Position = new Vector3d(10, -10, 5),
                Rotation = Quaterniond.Identity,
                FocalDistance = 15.0,
                FieldOfView = 0.785,
                AspectRatio = 1.6,
                NearDistance = 0.1,
                FarDistance = 500.0,
                Projection = NwcCameraProjection.Perspective,
            },
        };

        var clashTest = new NwcClashTest("HardClashes");
        var group = new NwcClashGroup("Pipe Penetrations", NwcClashStatus.Active);
        group.AddClash(new NwcClashResult(
            name: "Clash #1",
            item1: new NwcClashObject { Name = "Wall", Path = "Root/Wall" },
            item2: new NwcClashObject { Name = "Pipe", Path = "Root/Pipe" },
            clashPoint: new Vector3d(2.5, 0.1, 1.5),
            distance: -0.05,
            status: NwcClashStatus.Active
        ));
        clashTest.Groups.Add(group);

        var doc = new NwcDocumentBuilder()
            .SetFileInfo("Model.rvt")
            .AddMesh(mesh)
            .AddSavedViewpoint(vp)
            .AddClashTest(clashTest)
            .Build();

        string tempPath = Path.Combine(Path.GetTempPath(), $"viewer_test_{Guid.NewGuid():N}.html");
        try
        {
            doc.SaveHtmlViewer(tempPath, new NwcHtmlViewerOptions
            {
                Title = "Test Building Viewer",
                IncludeProperties = true,
                IncludeViewpoints = true,
                IncludeClashes = true,
            });

            Assert.True(File.Exists(tempPath));
            var fileInfo = new FileInfo(tempPath);
            Assert.True(fileInfo.Length > 2000, "Generated HTML viewer file should be a complete web application.");

            string html = File.ReadAllText(tempPath);
            Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
            Assert.Contains("<title>Test Building Viewer</title>", html, StringComparison.Ordinal);
            Assert.Contains("<canvas id=\"glcanvas\">", html, StringComparison.Ordinal);
            Assert.Contains("id=\"nwc-data\"", html, StringComparison.Ordinal);
            Assert.Contains("Overview Entrance", html, StringComparison.Ordinal);
            Assert.Contains("Pipe Penetrations", html, StringComparison.Ordinal);
            Assert.Contains("BasicWall", html, StringComparison.Ordinal);
            Assert.Contains("15.4 m3", html, StringComparison.Ordinal);
            Assert.Contains("gl.createShader", html, StringComparison.Ordinal);
            Assert.Contains("btn-toggle-sec", html, StringComparison.Ordinal);
            Assert.Contains("tab-clashes", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SaveHtmlViewer_Assembly_GeneratesUnifiedFederatedViewer()
    {
        var meshA = new NwcMesh
        {
            Id = 1,
            Name = "BuildingA_Slab",
            Vertices = [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(10, 10, 0)),
        };

        var meshB = new NwcMesh
        {
            Id = 2,
            Name = "BuildingB_Roof",
            Vertices = [new(20, 0, 5), new(30, 0, 5), new(20, 10, 5)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(20, 0, 5), new(30, 10, 5)),
        };

        var docA = new NwcDocumentBuilder().SetFileInfo("BuildingA.rvt").AddMesh(meshA).Build();
        var docB = new NwcDocumentBuilder().SetFileInfo("BuildingB.rvt").AddMesh(meshB).Build();

        var assembly = NwcAssembly.Create("FederatedHospital", docA, docB);

        string tempPath = Path.Combine(Path.GetTempPath(), $"assembly_viewer_{Guid.NewGuid():N}.html");
        try
        {
            assembly.SaveHtmlViewer(tempPath, new NwcHtmlViewerOptions
            {
                Title = "Federated Hospital Complex",
            });

            Assert.True(File.Exists(tempPath));
            string html = File.ReadAllText(tempPath);
            Assert.Contains("Federated Hospital Complex", html, StringComparison.Ordinal);
            Assert.Contains("BuildingA_Slab", html, StringComparison.Ordinal);
            Assert.Contains("BuildingB_Roof", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Cli_HtmlViewerExport_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"nwc_cli_viewer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string nwcPath = Path.Combine(tempDir, "input.nwc");
            string jsonPath = Path.Combine(tempDir, "output.json");
            string viewerPath = Path.Combine(tempDir, "viewer.html");

            var mesh = new NwcMesh
            {
                Id = 1,
                Name = "Box",
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            };
            var doc = NwcDocument.Create(meshes: [mesh]);
            doc.Save(nwcPath);

            int exitCode = NwcIO.Cli.Program.Main([
                nwcPath,
                jsonPath,
                "--html-viewer", viewerPath,
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(viewerPath));
            string html = File.ReadAllText(viewerPath);
            Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
            Assert.Contains("<canvas id=\"glcanvas\">", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveHtmlViewer_DoesNotMutateCallerOptionsAcrossExports()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "nwcio-html-opts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            static NwcDocument BuildDoc(string fileInfo, string taskName, string setName)
            {
                var start = new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
                return new NwcDocumentBuilder().SetFileInfo(fileInfo)
                    .AddMesh(new NwcMesh
                    {
                        Id = 1,
                        Name = "m",
                        Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                        Indices = [0, 1, 2],
                        BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
                    })
                    .SetTimeLiner(new Model.TimeLiner.NwcTimeLinerSchedule(
                        "Schedule", [new Model.TimeLiner.NwcTimeLinerTask("1", taskName, start, start.AddDays(2))]))
                    .AddSelectionSet(new Model.Sets.NwcSelectionSet(setName, [1]))
                    .Build();
            }

            var a = BuildDoc("A.rvt", "TaskOfA", "SetOfA");
            var b = BuildDoc("B.rvt", "TaskOfB", "SetOfB");

            var opts = new NwcHtmlViewerOptions { Title = "batch" };
            string htmlA = Path.Combine(tempDir, "a.html");
            string htmlB = Path.Combine(tempDir, "b.html");
            a.SaveHtmlViewer(htmlA, opts);
            b.SaveHtmlViewer(htmlB, opts);

            // The caller's options instance must stay untouched after both saves.
            Assert.Null(opts.TimeLiner);
            Assert.Null(opts.SelectionSets);
            Assert.Null(opts.GridSystem);
            Assert.Equal("batch", opts.Title);

            string contentB = File.ReadAllText(htmlB);
            Assert.Contains("SetOfB", contentB);
            Assert.DoesNotContain("SetOfA", contentB);
            Assert.Contains("TaskOfB", contentB);
            Assert.DoesNotContain("TaskOfA", contentB);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Write_HostileModelStrings_AreNotAssignedToInnerHtmlInPropertiesPane()
    {
        const string hostile = "<img src=x onerror=alert(1)>";

        var node = new NwcNode { Id = 1, Name = "n", Class = hostile };
        node.Properties.Add(new NwcPropertyRecord
        {
            FirstInt = 1,
            Tag = 1,
            Class = hostile,
            Name = hostile,
            Value = hostile,
        });

        var mesh = new NwcMesh
        {
            Id = 1,
            Name = hostile,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
            Node = node,
        };

        var sw = new StringWriter();
        NwcHtmlViewerExporter.Write(
            sw, [mesh], [], [], new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)), new NwcHtmlViewerOptions());
        string html = sw.ToString();

        // The properties pane must be built with textContent, never innerHTML.
        int fnStart = html.IndexOf("function showProperties", StringComparison.Ordinal);
        Assert.True(fnStart >= 0, "showProperties is missing from the viewer script");
        int fnEnd = html.IndexOf("\n  }", fnStart, StringComparison.Ordinal);
        string body = fnEnd > fnStart ? html[fnStart..fnEnd] : html[fnStart..];

        Assert.DoesNotContain("innerHTML", body);
        Assert.Contains("pane.replaceChildren()", body);
        Assert.Contains("title.textContent", body);
        Assert.Contains("nameCell.textContent", body);
        Assert.Contains("valCell.textContent", body);

        // The hostile strings survive as data in the JSON payload (JSON-escaped markup).
        Assert.Contains("\\u003Cimg src=x onerror=alert(1)\\u003E", html);
    }
}
