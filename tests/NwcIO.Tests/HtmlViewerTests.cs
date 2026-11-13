using System.Text.Json;
using NwcIO.Model;
using NwcIO.Model.Clash;
using NwcIO.Model.Viewer;
using Xunit;

namespace NwcIO.Tests;

public sealed class HtmlViewerTests
{
    [Fact]
    public void Write_SplitSourceMesh_KeepsOneHierarchyEntryAndBoundsOverAllChunks()
    {
        var vertices = Enumerable.Range(0, 21846)
            .SelectMany(i => new[] { new Vector3d(i, 0, 0), new Vector3d(i, 1, 0), new Vector3d(i, 0, 1) }).ToArray();
        var node = new NwcNode { Id = 5, Name = "Node", Class = "Layer" };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Class = "General", Name = "Source", Value = "68" });
        var mesh = new NwcMesh
        {
            Id = 68,
            Name = "Split source",
            Node = node,
            Material = new NwcMaterial { Id = 1, Name = "Color", Color = new RgbaColor(0.25f, 0.5f, 0.75f) },
            Vertices = vertices,
            Indices = Enumerable.Range(0, vertices.Length).ToArray(),
        };

        // Exercise the public exporter, including a source with no precomputed bounds.
        var html = ExportMesh(mesh);
        using var payload = ReadPayload(html);
        var source = Assert.Single(payload.RootElement.GetProperty("meshes").EnumerateArray());
        Assert.Equal(68, source.GetProperty("id").GetInt32());
        Assert.Equal(5, source.GetProperty("nodeId").GetInt32());
        Assert.Equal("Split source", source.GetProperty("name").GetString());
        Assert.Equal("Layer", source.GetProperty("layer").GetString());
        Assert.Equal<double>([0.25 / 255.0, 0.5 / 255.0, 0.75 / 255.0, 1 / 255.0],
            source.GetProperty("color").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal("68", source.GetProperty("properties").GetProperty("General").GetProperty("Source").GetString());
        var chunks = source.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal<int>([65535, 3], chunks.Select(c => c.GetProperty("vertices").GetArrayLength() / 3));
        var reconstructed = new List<Vector3d>();
        foreach (var chunk in chunks)
        {
            var positions = chunk.GetProperty("vertices").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            foreach (var index in chunk.GetProperty("indices").EnumerateArray().Select(v => v.GetInt32()))
            {
                Assert.InRange(index, 0, positions.Length / 3 - 1);
                reconstructed.Add(new Vector3d(positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2]));
            }
        }
        Assert.Equal(vertices, reconstructed);
        var bounds = source.GetProperty("bounds");
        Assert.Equal<double>([0, 0, 0], bounds.GetProperty("min").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Equal<double>([21845, 1, 1], bounds.GetProperty("max").EnumerateArray().Select(v => v.GetDouble()));
        Assert.Contains("modelData.meshes.forEach((m, idx) =>", html);
    }

    [Fact]
    public void Write_SplitSourceMesh_ScriptDrawsChunksAndFitsSourceBounds()
    {
        var html = ExportMesh(new NwcMesh { Id = 68, Vertices = [], Indices = [] }).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("const gpuMeshes = modelData.meshes.map((m, idx) =>", html);
        Assert.Contains("const chunks = m.chunks.map(chunk =>", html);
        Assert.Contains("raw: m, index: idx, bounds: m.bounds", html);
        Assert.Contains("for (const chunk of gm.chunks)", html);
        Assert.Contains("new Float32Array(chunk.vertices)", html);
        Assert.Contains("new Uint16Array(chunk.indices)", html);
        Assert.Contains("selectedMeshIndex = idx;", html);
        Assert.Contains("gm.index === selectedMeshIndex", html);
        Assert.Contains("const gm = gpuMeshes[selectedMeshIndex];\n        const b = gm.bounds;", html);
        Assert.Contains("clipBox.min = [...b.min]", html);
        Assert.Contains("clipBox.max = [...b.max]", html);
    }

    [Fact]
    public void Write_SelectionSetClick_MatchesMeshesByNodeIdBeforeMeshId()
    {
        var html = ExportMesh(new NwcMesh { Id = 68, Vertices = [], Indices = [] });
        Assert.Contains("const nid = gm.raw.nodeId !== undefined ? gm.raw.nodeId : gm.raw.id;\n                if (idSet.has(nid))", html.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.DoesNotContain("|| idSet.has(gm.raw.id)", html);
    }

    [Theory]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(70000)]
    [InlineData(200000)]
    public void Write_LargeMesh_Uses16BitChunksPreservingTriangles(int count)
    {
        var vertices = Enumerable.Range(0, count).Select(i => new Vector3d(i, i % 7, i % 11)).ToArray();
        var indices = Enumerable.Range(0, vertices.Length - 2).SelectMany(i => new[] { i, i + 1, i + 2 }).ToArray();
        var mesh = new NwcMesh
        {
            Id = 68,
            Name = "Large mesh",
            Node = new NwcNode { Id = 5, Name = "Node", Class = "Layer" },
            Vertices = vertices,
            Indices = indices,
        };

        var html = ExportMesh(mesh);
        using var payload = ReadPayload(html);
        var source = Assert.Single(payload.RootElement.GetProperty("meshes").EnumerateArray());
        var chunks = source.GetProperty("chunks").EnumerateArray().ToArray();
        if (count > 65535)
        {
            Assert.True(chunks.Length > 1, "Large meshes must be split into 16-bit-safe chunks.");
        }
        else
        {
            Assert.Single(chunks);
        }
        Assert.Equal(68, source.GetProperty("id").GetInt32());
        Assert.Equal(5, source.GetProperty("nodeId").GetInt32());
        Assert.Equal("Large mesh", source.GetProperty("name").GetString());
        Assert.Equal("Layer", source.GetProperty("layer").GetString());
        var reconstructed = new List<Vector3d>();
        foreach (var chunk in chunks)
        {
            var positions = chunk.GetProperty("vertices").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var vertexCount = positions.Length / 3;
            Assert.InRange(vertexCount, 1, 65535);
            var triangles = chunk.GetProperty("indices").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            Assert.Equal(0, triangles.Length % 3);
            foreach (var index in triangles)
            {
                Assert.InRange(index, 0, vertexCount - 1);
                reconstructed.Add(new Vector3d(positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2]));
            }
            Assert.False(chunk.TryGetProperty("lineIndices", out _));
        }

        Assert.Equal(indices.Select(i => vertices[i]), reconstructed);
        Assert.Equal(count, mesh.Vertices.Count);
        Assert.Equal(indices, mesh.Indices);
    }

    [Fact]
    public void Write_DisconnectedTriangles_BuildsWireframeEdgesInScript()
    {
        var mesh = new NwcMesh
        {
            Id = 68,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(10, 0, 0), new(11, 0, 0), new(10, 1, 0)],
            Indices = [0, 1, 2, 3, 4, 5],
        };

        var html = ExportMesh(mesh);
        using var payload = ReadPayload(html);
        var source = Assert.Single(payload.RootElement.GetProperty("meshes").EnumerateArray());
        var exported = Assert.Single(source.GetProperty("chunks").EnumerateArray());
        Assert.Equal(mesh.Indices, exported.GetProperty("indices").EnumerateArray().Select(v => v.GetInt32()));
        Assert.False(exported.TryGetProperty("lineIndices", out _));
        Assert.Contains("const lineIndices = new Uint16Array(Math.floor(chunk.indices.length / 3) * 6)", html);
        Assert.Contains("i + 2 < chunk.indices.length; i += 3", html);
        Assert.Contains("const a = chunk.indices[i], b = chunk.indices[i + 1], c = chunk.indices[i + 2]", html);
        Assert.Contains("lineIndices.set([a, b, b, c, c, a], i * 2)", html);
        Assert.Contains("gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, lineIndices, gl.STATIC_DRAW)", html);
        Assert.Contains("lineCount: lineIndices.length", html);
        Assert.Contains("gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, wireframeMode ? chunk.lineIbo : chunk.ibo)", html);
        Assert.Contains("wireframeMode ? chunk.lineCount : chunk.count, gl.UNSIGNED_SHORT", html);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(-1, 2)]
    [InlineData(65536, 1)]
    [InlineData(65536, 2)]
    [InlineData(70000, 2)]
    public void Write_LargeMesh_SkipsInvalidAndIncompleteTriangles(int invalidIndex, int trailingCount)
    {
        var vertices = Enumerable.Range(0, 65536).Select(i => new Vector3d(i, i % 7, i % 11)).ToArray();
        int[] validIndices = [0, 1, 65535, 2, 3, 4];
        int[] trailingIndices = [9, 10];
        var indices = new[] { 5, invalidIndex, 6 }
            .Concat(validIndices.Take(3))
            .Concat(new[] { 7, 8, invalidIndex })
            .Concat(validIndices.Skip(3))
            .Concat(trailingIndices.Take(trailingCount)).ToArray();
        var mesh = new NwcMesh { Id = 68, Vertices = vertices, Indices = indices };

        using var payload = ReadPayload(ExportMesh(mesh));
        var source = Assert.Single(payload.RootElement.GetProperty("meshes").EnumerateArray());
        var chunk = Assert.Single(source.GetProperty("chunks").EnumerateArray());
        var positions = chunk.GetProperty("vertices").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var triangles = chunk.GetProperty("indices").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        Assert.Equal(validIndices.Length * 3, positions.Length);
        Assert.Equal(validIndices.Length, triangles.Length);
        var reconstructed = triangles.Select(index =>
        {
            Assert.InRange(index, 0, positions.Length / 3 - 1);
            return new Vector3d(positions[index * 3], positions[index * 3 + 1], positions[index * 3 + 2]);
        });
        Assert.Equal(validIndices.Select(index => vertices[index]), reconstructed);
        Assert.False(chunk.TryGetProperty("lineIndices", out _));
        Assert.Equal(indices, mesh.Indices);
        Assert.Equal(65536, mesh.Vertices.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Write_LargeMesh_SkipsIncompleteTrailingTriangle(int trailingCount)
    {
        var vertices = Enumerable.Range(0, 65536).Select(i => new Vector3d(i, 0, 0)).ToArray();
        int[] triangle = [0, 1, 65535];
        var mesh = new NwcMesh
        {
            Id = 68,
            Vertices = vertices,
            Indices = triangle.Concat(triangle.Take(trailingCount)).ToArray(),
        };

        using var payload = ReadPayload(ExportMesh(mesh));
        var source = Assert.Single(payload.RootElement.GetProperty("meshes").EnumerateArray());
        var chunk = Assert.Single(source.GetProperty("chunks").EnumerateArray());
        Assert.Equal(9, chunk.GetProperty("vertices").GetArrayLength());
        Assert.Equal(3, chunk.GetProperty("indices").GetArrayLength());
    }

    private static string ExportMesh(NwcMesh mesh)
    {
        using var writer = new StringWriter();
        NwcHtmlViewerExporter.Write(writer, [mesh], [], [], BoundingBox3d.Empty, new NwcHtmlViewerOptions());
        return writer.ToString();
    }

    private static JsonDocument ReadPayload(string html)
    {
        var start = html.IndexOf('>', html.IndexOf("id=\"nwc-data\"", StringComparison.Ordinal)) + 1;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        return JsonDocument.Parse(html[start..end]);
    }

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
