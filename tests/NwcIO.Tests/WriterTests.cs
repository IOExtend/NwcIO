using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class WriterTests
{
    [Fact]
    public void Document_TocShapedGuidTable_ReopensWithGuids()
    {
        var guids = new[]
        {
            Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"),
        };
        var data = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(data, 6);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 2);
        guids[0].TryWriteBytes(data.AsSpan(8, 16));
        guids[1].TryWriteBytes(data.AsSpan(24, 16));
        var bytes = NwcContainerWriter.Write([data]);
        var container = NwcContainerReader.Read(bytes);
        var stream = Assert.Single(container.Streams);

        Assert.True(GuidTableParser.TryParse(stream, out var parsedGuids));
        Assert.Equal(guids, parsedGuids);

        using var ms = new MemoryStream(bytes);
        var reopened = NwcDocument.Open(ms);

        Assert.Equal(guids, reopened.Guids);
        Assert.Equal(NwcStreamKind.Guids, StreamClassifier.Classify(stream));
    }

    [Theory]
    [InlineData(3, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(1, 124, false)]
    [InlineData(1, 124, true)]
    public void Document_GuidShapedGeometryAndToc_RoundtripPreservesGuids(int meshCount, int vertexCount, bool withGuids)
    {
        var meshes = Enumerable.Range(1, meshCount).Select(id => new NwcMesh
        {
            Id = id,
            Vertices = Enumerable.Range(0, vertexCount).Select(v => new Vector3d(v, v % 2, id)).ToArray(),
            Indices = [0, 1, 2],
        }).ToArray();
        var guids = withGuids
            ? new[] { Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100") }
            : Array.Empty<Guid>();
        var doc = NwcDocument.Create(meshes: meshes, guids: guids);
        var bytes = doc.ToByteArray();

        using var ms = new MemoryStream(bytes);
        var reopened = NwcDocument.Open(ms);

        Assert.Equal(guids, reopened.Guids);
        Assert.Equal(meshCount, reopened.Meshes.Count);
        for (var i = 0; i < meshCount; i++)
        {
            Assert.Equal(meshes[i].Vertices, reopened.Meshes[i].Vertices);
        }

        var container = NwcContainerReader.Read(bytes);
        StreamClassifier.ClassifyAll(container.Streams);
        var toc = Assert.Single(container.Streams, s => s.Kind == NwcStreamKind.Toc);
        var geometry = Assert.Single(container.Streams, s => s.Kind == NwcStreamKind.Geometry);
        Assert.Equal(NwcStreamKind.Toc, StreamClassifier.Classify(toc));
        Assert.Equal(NwcStreamKind.Geometry, StreamClassifier.Classify(geometry));
        Assert.Equal(withGuids ? 1 : 0, container.Streams.Count(s => s.Kind == NwcStreamKind.Guids));
    }

    [Fact]
    public void ContainerWriter_Roundtrip_RestoresExactStreamBuffers()
    {
        var raw1 = System.Text.Encoding.UTF8.GetBytes("Payload 1: Navisworks Partition Header stream test");
        var raw2 = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };
        var raw3 = System.Text.Encoding.ASCII.GetBytes("Payload 3: Table of Contents TOC block");

        byte[] nwcBytes = NwcContainerWriter.Write([raw1, raw2, raw3]);
        Assert.NotEmpty(nwcBytes);

        var container = NwcContainerReader.Read(nwcBytes);
        Assert.Single(container.Sections);
        Assert.Equal(3, container.Streams.Count);
        Assert.Equal(raw1, container.Streams[0].Data);
        Assert.Equal(raw2, container.Streams[1].Data);
        Assert.Equal(raw3, container.Streams[2].Data);
    }

    [Fact]
    public void DocumentBuilder_Roundtrip_SavesAndOpensMatchingDocument()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();

        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Wall_Mesh",
            Vertices =
            [
                new Vector3d(0.0, 0.0, 0.0),
                new Vector3d(5.0, 0.0, 0.0),
                new Vector3d(5.0, 3.0, 0.0),
                new Vector3d(0.0, 3.0, 0.0)
            ],
            Indices = [0, 1, 2, 2, 1, 3],
            BoundingBox = new BoundingBox3d(new Vector3d(0, 0, 0), new Vector3d(5, 3, 0))
        };

        var transform = new NwcTransform
        {
            Id = 100,
            Translation = new Vector3d(12.5, -4.0, 100.0),
            Rotation = new Quaterniond(0, 0, 0, 1),
            BoundingBox = new BoundingBox3d(new Vector3d(-10, -10, 0), new Vector3d(10, 10, 20))
        };

        var viewpoint = new NwcSavedViewpoint
        {
            Name = "Perspective View 1",
            Camera = new NwcCamera
            {
                Position = new Vector3d(15.0, 25.0, 10.0),
                Rotation = new Quaterniond(0.0, 0.0, 0.0, 1.0),
                FocalDistance = 25.0,
                AspectRatio = 1.6,
                FieldOfView = 60.0
            },
            Comments = ["{ClashCheck}", "BIM Coordination Review"]
        };

        var doc = new NwcDocumentBuilder()
            .SetFileInfo("CommercialCenter.rvt", "LcOpRevitExport")
            .AddMesh(mesh)
            .AddTransform(transform)
            .AddSavedViewpoint(viewpoint)
            .AddGuid(guid1)
            .AddGuid(guid2)
            .AddPropertyRecord(new NwcPropertyRecord
            {
                FirstInt = 1,
                Tag = 53,
                Name = "Architecture",
                Class = "Node"
            })
            .AddPropertyRecord(new NwcPropertyRecord
            {
                FirstInt = 2,
                Tag = 52,
                Name = "Walls",
                Class = "LcRevitLayer",
                ValueType = 4,
                Value = "Layer 1"
            })
            .Build();

        byte[] fileBytes = doc.ToByteArray();
        Assert.NotEmpty(fileBytes);

        // Open back via memory stream
        using var ms = new MemoryStream(fileBytes);
        var opened = NwcDocument.Open(ms);

        // Verify FileInfo
        Assert.Equal("CommercialCenter.rvt", opened.FileInfo.SourcePath);
        Assert.Equal("LcOpRevitExport", opened.FileInfo.LoaderPlugin);

        // Verify GUIDs
        Assert.Equal(2, opened.Guids.Count);
        Assert.Equal(guid1, opened.Guids[0]);
        Assert.Equal(guid2, opened.Guids[1]);

        // Verify Transforms
        Assert.Single(opened.Transforms);
        Assert.Equal(12.5, opened.Transforms[0].Translation.X);
        Assert.Equal(-4.0, opened.Transforms[0].Translation.Y);
        Assert.Equal(100.0, opened.Transforms[0].Translation.Z);
        Assert.NotNull(opened.Transforms[0].BoundingBox);
        Assert.Equal(-10.0, opened.Transforms[0].BoundingBox!.Value.Min.X);
        Assert.Equal(20.0, opened.Transforms[0].BoundingBox!.Value.Max.Z);

        // Verify Meshes
        Assert.Single(opened.Meshes);
        Assert.Equal(4, opened.Meshes[0].Vertices.Count);
        Assert.Equal(0.0, opened.Meshes[0].Vertices[0].X);
        Assert.Equal(5.0, opened.Meshes[0].Vertices[1].X);
        Assert.Equal(3.0, opened.Meshes[0].Vertices[2].Y);

        // Mesh is bound to transform
        Assert.NotNull(opened.Meshes[0].Transform);
        Assert.Equal(12.5, opened.Meshes[0].Transform!.Translation.X);

        // Verify Viewpoints
        Assert.Single(opened.SavedViewpoints);
        Assert.Equal(15.0, opened.SavedViewpoints[0].Camera!.Position.X);
        Assert.Equal(25.0, opened.SavedViewpoints[0].Camera!.Position.Y);
        Assert.Equal(10.0, opened.SavedViewpoints[0].Camera!.Position.Z);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DocumentBuilder_SourceTimestamp_RoundtripsAsUtc(DateTimeKind kind)
    {
        var timestamp = new DateTime(2026, 5, 1, 12, 30, 45, kind);
        var expectedUtc = kind == DateTimeKind.Local
            ? timestamp.ToUniversalTime()
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

        var doc = new NwcDocumentBuilder()
            .SetFileInfo("Model.rvt", timestampUtc: timestamp)
            .Build();

        using var ms = new MemoryStream(doc.ToByteArray());
        var opened = NwcDocument.Open(ms);

        Assert.NotNull(opened.FileInfo.SourceTimestampUtc);
        Assert.Equal(expectedUtc, opened.FileInfo.SourceTimestampUtc.Value.ToUniversalTime());
    }

    [Theory]
    [InlineData(true, 0, 0, 1, 0)]
    [InlineData(false, 0, 0, 1, 0)]
    [InlineData(false, 0, 0.99999999, 0, 0.00005)]
    public void DocumentBuilder_SavedViewpointWithZeroW_Roundtrips(bool withIdentity, double qx, double qy, double qz, double qw)
    {
        var builder = new NwcDocumentBuilder();
        if (withIdentity)
        {
            builder.AddSavedViewpoint(new NwcSavedViewpoint
            {
                Name = "Front",
                Camera = new NwcCamera { Position = new Vector3d(1, 2, 3), Rotation = Quaterniond.Identity },
            });
        }

        var rotation = new Quaterniond(qx, qy, qz, qw);
        builder.AddSavedViewpoint(new NwcSavedViewpoint
        {
            Name = "Back",
            Camera = new NwcCamera { Position = new Vector3d(4, 5, 6), Rotation = rotation },
        });

        using var ms = new MemoryStream(builder.Build().ToByteArray());
        var opened = NwcDocument.Open(ms);

        Assert.Equal(withIdentity ? 2 : 1, opened.SavedViewpoints.Count);
        var back = Assert.Single(opened.SavedViewpoints, v => v.Name == "Back");
        var expected = rotation.Normalized();
        Assert.Equal(new Vector3d(4, 5, 6), back.Camera!.Position);
        Assert.Equal(expected.X, back.Camera.Rotation.X, 9);
        Assert.Equal(expected.Y, back.Camera.Rotation.Y, 9);
        Assert.Equal(expected.Z, back.Camera.Rotation.Z, 9);
        Assert.Equal(expected.W, back.Camera.Rotation.W, 9);
    }

    [Fact]
    public void Document_SaveToFileAndReopen_WorksCorrectly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"nwcio_test_{Guid.NewGuid():N}.nwc");
        try
        {
            var mesh1 = new NwcMesh
            {
                Id = 1,
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2]
            };
            var mesh2 = new NwcMesh
            {
                Id = 2,
                Vertices = [new(10, 10, 10), new(20, 10, 10), new(10, 20, 10)],
                Indices = [0, 1, 2]
            };

            var doc = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "TestScene.rvt" },
                meshes: [mesh1, mesh2]);

            doc.Save(tempFile);
            Assert.True(File.Exists(tempFile));

            var reopened = NwcDocument.Open(tempFile);
            Assert.Equal("TestScene.rvt", reopened.FileInfo.SourcePath);
            Assert.Equal(2, reopened.Meshes.Count);

            // Verify downstream exporters run cleanly
            using var glbMs = new MemoryStream();
            reopened.SaveGlb(glbMs);
            Assert.True(glbMs.Length > 20);

            using var sw = new StringWriter();
            reopened.SaveObj(sw);
            string objText = sw.ToString();
            Assert.Contains("v 0.0000 0.0000 0.0000", objText);
            Assert.Contains("v 10.0000 10.0000 10.0000", objText);
            Assert.Contains("f 1 2 3", objText);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Cli_NwcOutput_ConvertsAndRoundtrips()
    {
        string inputPath = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid():N}.nwc");
        string outputPath = Path.Combine(Path.GetTempPath(), $"output_{Guid.NewGuid():N}.nwc");

        try
        {
            var doc = NwcDocument.Create(
                fileInfo: new NwcFileInfo { SourcePath = "TestModel.rvt" },
                meshes:
                [
                    new NwcMesh
                    {
                        Id = 1,
                        Vertices = [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)],
                        Indices = [0, 1, 2]
                    }
                ]);
            doc.Save(inputPath);

            int exitCode = NwcIO.Cli.Program.Main([inputPath, outputPath]);
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputPath));

            var reloaded = NwcDocument.Open(outputPath);
            Assert.Equal("TestModel.rvt", reloaded.FileInfo.SourcePath);
            Assert.Single(reloaded.Meshes);
            Assert.Equal(3, reloaded.Meshes[0].Vertices.Count);
        }
        finally
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
