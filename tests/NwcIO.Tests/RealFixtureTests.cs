using System.IO.Compression;
using System.Text.Json;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

/// <summary>
/// Marks a test that needs a private real-model fixture; xUnit reports it as
/// skipped with a reason when no fixture is available.
/// </summary>
internal sealed class RealFixtureFactAttribute : FactAttribute
{
    public RealFixtureFactAttribute()
    {
        string? skipReason = TestFiles.DescribeSkipReason();
        if (skipReason is not null)
        {
            Skip = skipReason;
        }
    }
}

/// <summary>Marks a theory whose cases come from the discovered fixtures (.nwc, .nwd and .nwf).</summary>
internal sealed class RealFixtureTheoryAttribute : TheoryAttribute
{
    public RealFixtureTheoryAttribute()
    {
        string? skipReason = TestFiles.DescribeModelSkipReason();
        if (skipReason is not null)
        {
            Skip = skipReason;
        }
    }
}

/// <summary>Marks a federated test that needs at least two .nwc/.nwd fixtures.</summary>
internal sealed class RealMultiFixtureFactAttribute : FactAttribute
{
    public RealMultiFixtureFactAttribute()
    {
        string? skipReason = TestFiles.DescribeSkipReason();
        if (skipReason is null && TestFiles.EnumerateAll().Count < 2)
        {
            skipReason = "At least two .nwc/.nwd fixtures are needed for the multi-model test.";
        }

        Skip = skipReason;
    }
}

/// <summary>
/// Optional sidecar expectations for one fixture, stored next to it as <c>&lt;name&gt;.expect.json</c>.
/// Absent values are not asserted, so any valid model passes without a sidecar.
/// </summary>
internal sealed class FixtureExpectations
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        // A misspelled key must fail loudly instead of silently disabling an expectation.
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public string? SourceContains { get; set; }
    public string? LoaderPlugin { get; set; }
    public int? MinGuids { get; set; }
    public int? MinPropertyRecords { get; set; }
    public int? MinTransforms { get; set; }
    public int? MinMeshes { get; set; }
    public int? MinViewpoints { get; set; }
    public long? MinGlbBytes { get; set; }
    public long? MinIfc2x3Bytes { get; set; }
    public long? MinIfc4Bytes { get; set; }

    public static FixtureExpectations? Load(string fixturePath)
    {
        string sidecar = Path.ChangeExtension(fixturePath, ".expect.json");
        return File.Exists(sidecar) ? Parse(File.ReadAllText(sidecar)) : null;
    }

    public static FixtureExpectations Parse(string json) =>
        JsonSerializer.Deserialize<FixtureExpectations>(json, Options)
            ?? throw new JsonException("The expectations file must hold a JSON object.");
}

/// <summary>Fixture-independent checks of the sidecar format documented in docs/test-fixtures.md.</summary>
public class FixtureExpectationsTests
{
    [Fact]
    public void Parse_DocumentedExample_BindsEveryField()
    {
        const string json = """
            {
              "sourceContains": ".rvt",
              "loaderPlugin": "LcNwcLoaderPlugin:lcldrevit",
              "minGuids": 1000,
              "minPropertyRecords": 500,
              "minTransforms": 1000,
              "minMeshes": 500,
              "minViewpoints": 10,
              "minGlbBytes": 100000,
              "minIfc2x3Bytes": 100000,
              "minIfc4Bytes": 50000
            }
            """;

        var expectations = FixtureExpectations.Parse(json);

        Assert.Equal(".rvt", expectations.SourceContains);
        Assert.Equal("LcNwcLoaderPlugin:lcldrevit", expectations.LoaderPlugin);
        Assert.Equal(1000, expectations.MinGuids);
        Assert.Equal(500, expectations.MinPropertyRecords);
        Assert.Equal(1000, expectations.MinTransforms);
        Assert.Equal(500, expectations.MinMeshes);
        Assert.Equal(10, expectations.MinViewpoints);
        Assert.Equal(100000L, expectations.MinGlbBytes);
        Assert.Equal(100000L, expectations.MinIfc2x3Bytes);
        Assert.Equal(50000L, expectations.MinIfc4Bytes);
    }

    [Fact]
    public void Parse_UnknownKey_Throws() =>
        Assert.Throws<JsonException>(() => FixtureExpectations.Parse("""{ "minMeshs": 5 }"""));

    [Fact]
    public void Parse_NullDocument_Throws() =>
        Assert.Throws<JsonException>(() => FixtureExpectations.Parse("null"));

    [Fact]
    public void Load_ReadsSidecarNextToFixture_AndIgnoresMissingOne()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"fixture_expect_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string fixture = Path.Combine(dir, "Sample1.nwc");
            Assert.Null(FixtureExpectations.Load(fixture));

            File.WriteAllText(Path.Combine(dir, "Sample1.expect.json"), """{ "minMeshes": 7 }""");
            Assert.Equal(7, FixtureExpectations.Load(fixture)?.MinMeshes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>
/// Runs every discovered fixture through model-independent invariants. Privacy: a case is identified
/// by its position in the fixture list, and no path or model content reaches an assertion message.
/// </summary>
public class RealFixtureTests
{
    public static IEnumerable<object[]> Fixtures() =>
        TestFiles.EnumerateAll(includeFederations: true).Select((_, index) => new object[] { index });

    public static IEnumerable<object[]> NwcFixtures() =>
        TestFiles.EnumerateAll(includeFederations: true)
            .Select((path, index) => (path, index))
            .Where(f => IsNwc(f.path))
            .Select(f => new object[] { f.index });

    private static bool IsNwc(string path) => path.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase);

    private static string PathOf(int index) => TestFiles.EnumerateAll(includeFederations: true)[index];

    // A .nwd holds partition nodes the scene-tree builder rejects, so only .nwc is opened with a tree.
    private static NwcDocument Open(string path) =>
        NwcDocument.Open(path, new NwcReaderOptions { BuildSceneTree = IsNwc(path) });

    private static NwcDocument OpenFixture(string path, FixtureExpectations? expectations = null)
    {
        var doc = Open(path);
        if (expectations?.SourceContains is { } source)
        {
            Assert.True(
                doc.FileInfo.SourcePath?.Contains(source, StringComparison.OrdinalIgnoreCase) == true,
                "sourceContains expectation not met");
        }

        if (expectations?.LoaderPlugin is { } loader)
        {
            Assert.True(loader == doc.FileInfo.LoaderPlugin, "loaderPlugin expectation not met");
        }

        return doc;
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Open_Fixture_ParsesModelInvariants(int index)
    {
        string path = PathOf(index);
        var expectations = FixtureExpectations.Load(path);
        var doc = OpenFixture(path, expectations);

        Assert.NotNull(doc.FileInfo);
        if (IsNwc(path))
        {
            Assert.NotNull(doc.RootNode);
        }

        if (expectations?.MinGuids is { } minGuids)
        {
            Assert.True(doc.Guids.Count >= minGuids, $"Expected at least {minGuids} GUIDs, got {doc.Guids.Count}");
        }

        if (expectations?.MinPropertyRecords is { } minRecords)
        {
            Assert.True(doc.PropertyRecords.Count >= minRecords, $"Expected at least {minRecords} property records, got {doc.PropertyRecords.Count}");
        }

        if (expectations?.MinTransforms is { } minTransforms)
        {
            Assert.True(doc.Transforms.Count >= minTransforms, $"Expected at least {minTransforms} transforms, got {doc.Transforms.Count}");
        }

        if (expectations?.MinMeshes is { } minMeshes)
        {
            Assert.True(doc.Meshes.Count >= minMeshes, $"Expected at least {minMeshes} meshes, got {doc.Meshes.Count}");
        }

        Assert.All(doc.Transforms, transform => Assert.True(transform.Rotation.IsNormalized(), "transform rotation is not normalized"));

        foreach (var mesh in doc.Meshes)
        {
            Assert.True(mesh.Vertices.Count >= 2, "mesh with fewer than two vertices");
            Assert.NotNull(mesh.BoundingBox);
            if (mesh.TriangleCount > 0)
            {
                Assert.True(mesh.Vertices.Count >= 3, "triangle mesh with fewer than three vertices");
                Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Vertices.Count - 1));
            }
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Export_Obj_WritesVerticesAndFacesOfEveryTriangleMesh(int index)
    {
        var doc = OpenFixture(PathOf(index));
        using var writer = new StringWriter();

        doc.SaveObj(writer, worldCoordinates: true);

        string obj = writer.ToString();
        Assert.Contains("# Wavefront OBJ", obj);
        var named = doc.Meshes.FirstOrDefault(m => m.TriangleCount > 0 && m.Name.Length > 0);
        if (doc.Meshes.Any(m => m.TriangleCount > 0))
        {
            Assert.Contains("\nv ", obj);
            Assert.Contains("\nf ", obj);
        }

        if (named is not null)
        {
            Assert.True(obj.Contains($"\no {named.Name}{Environment.NewLine}", StringComparison.Ordinal), "named mesh missing from OBJ");
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Export_Glb_HasContainerHeader(int index)
    {
        var expectations = FixtureExpectations.Load(PathOf(index));
        var doc = OpenFixture(PathOf(index));
        using var stream = new MemoryStream();

        doc.SaveGlb(stream, worldCoordinates: true);

        byte[] glb = stream.ToArray();
        Assert.Equal(0x46546C67u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0)));
        Assert.Equal(2u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)));
        Assert.Equal((uint)glb.Length, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)));
        if (expectations?.MinGlbBytes is { } minGlb)
        {
            Assert.True(glb.Length >= minGlb, $"Expected GLB of at least {minGlb} bytes, got {glb.Length}");
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Export_Json_MatchesDocumentCounts(int index)
    {
        var doc = OpenFixture(PathOf(index));
        using var stream = new MemoryStream();

        doc.SaveJson(stream, new NwcJsonExportOptions
        {
            Indented = false,
            IncludeTransforms = true,
            IncludeSceneTree = true,
            IncludeMeshes = true,
            IncludeMaterials = true,
            IncludeStreamCatalog = true,
        });

        using var json = JsonDocument.Parse(stream.ToArray());
        var root = json.RootElement;
        Assert.Equal(doc.Meshes.Count, root.GetProperty("meshCount").GetInt32());
        Assert.Equal(doc.Materials.Count, root.GetProperty("materialCount").GetInt32());
        Assert.Equal(doc.Guids.Count, root.GetProperty("guids").GetArrayLength());
        Assert.True(root.GetProperty("container").TryGetProperty("streams", out _));

        // The exporter leaves these sections out when the document has no such data (a .nwf holds no geometry).
        Assert.Equal(doc.Transforms.Count > 0, root.TryGetProperty("transforms", out var transforms));
        if (doc.Transforms.Count > 0)
        {
            Assert.Equal(doc.Transforms.Count, transforms.GetArrayLength());
        }

        Assert.Equal(doc.RootNode is not null, root.TryGetProperty("sceneTree", out var sceneTree));
        if (doc.RootNode is not null)
        {
            Assert.Equal(doc.RootNode.Children.Count > 0, sceneTree.TryGetProperty("children", out _));
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Viewpoints_ExportToBcf_OneTopicPerViewpoint(int index)
    {
        string path = PathOf(index);
        var expectations = FixtureExpectations.Load(path);
        var doc = OpenFixture(path);

        if (expectations?.MinViewpoints is { } minViewpoints)
        {
            Assert.True(doc.SavedViewpoints.Count >= minViewpoints, $"Expected at least {minViewpoints} viewpoints, got {doc.SavedViewpoints.Count}");
        }

        foreach (var viewpoint in doc.SavedViewpoints)
        {
            Assert.NotNull(viewpoint.Camera);
            Assert.True(viewpoint.Camera.Rotation.IsNormalized(), "viewpoint rotation is not normalized");
            Assert.True(viewpoint.Camera.FocalDistance > 0, "viewpoint focal distance is not positive");
            Assert.True(viewpoint.Camera.AspectRatio > 0, "viewpoint aspect ratio is not positive");
        }

        using var stream = new MemoryStream();
        doc.SaveViewpointsBcf(stream);

        using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("bcf.version"));
        Assert.Equal(
            doc.SavedViewpoints.Count,
            archive.Entries.Count(e => e.FullName.EndsWith("/markup.bcf", StringComparison.Ordinal)));
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void SpatialIndex_FindsAndHitsMeshes(int index)
    {
        var doc = OpenFixture(PathOf(index));
        var spatial = doc.SpatialIndex;

        Assert.Equal(doc.Meshes.Count, spatial.MeshCount);
        if (doc.Meshes.Count == 0)
        {
            Assert.Equal(0, spatial.NodeCount);
            Assert.True(spatial.Bounds.IsEmpty);
            return;
        }

        Assert.True(spatial.NodeCount > 0);
        Assert.True(spatial.LeafCount > 0);
        Assert.False(spatial.Bounds.IsEmpty);

        var target = doc.Meshes.FirstOrDefault(m => m.TriangleCount > 0);
        if (target is null)
        {
            return;
        }

        Assert.Contains(spatial.QueryBox(target.GetWorldBoundingBox()), m => m.Id == target.Id);

        // Shoot at the first non-degenerate triangle along its normal, from either side (winding is not assumed).
        var world = target.GetWorldVertices();
        for (int i = 0; i + 2 < target.Indices.Count; i += 3)
        {
            var a = world[target.Indices[i]];
            var b = world[target.Indices[i + 1]];
            var c = world[target.Indices[i + 2]];
            var cross = Vector3d.Cross(b - a, c - a);
            if (cross.Length < 1e-9)
            {
                continue;
            }

            var normal = cross.Normalize();
            var centroid = (a + b + c) / 3.0;
            bool hit = spatial.Raycast(centroid + normal, -normal, out var front)
                || spatial.Raycast(centroid - normal, normal, out front);
            Assert.True(hit, "ray aimed at a triangle missed");
            Assert.True(front.Distance > 0);
            Assert.NotNull(front.Mesh);
            return;
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Ifc2x3_Export_HasHeaderProjectAndFooter(int index)
    {
        var expectations = FixtureExpectations.Load(PathOf(index));
        var doc = OpenFixture(PathOf(index));
        using var writer = new StringWriter();

        doc.SaveIfc(writer, new NwcIfcExportOptions { SchemaVersion = NwcIfcSchemaVersion.Ifc2x3 });

        string ifc = writer.ToString();
        Assert.Contains("ISO-10303-21;", ifc);
        Assert.Contains("FILE_SCHEMA(('IFC2X3'));", ifc);
        Assert.Contains("IFCPROJECT(", ifc);
        Assert.Contains("END-ISO-10303-21;", ifc);
        if (expectations?.MinIfc2x3Bytes is { } minBytes)
        {
            long bytes = System.Text.Encoding.UTF8.GetByteCount(ifc);
            Assert.True(bytes >= minBytes, $"Expected IFC2x3 of at least {minBytes} bytes, got {bytes}");
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Ifc4_Export_HasHeaderTessellationAndFooter(int index)
    {
        var expectations = FixtureExpectations.Load(PathOf(index));
        var doc = OpenFixture(PathOf(index));
        using var writer = new StringWriter();

        doc.SaveIfc(writer, new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc4,
            ExportTypeObjects = true,
            UseTessellationInIfc4 = true,
        });

        string ifc = writer.ToString();
        Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc);
        Assert.Contains("DesignTransferView", ifc);
        Assert.Contains("IFCPROJECT(", ifc);
        if (doc.Meshes.Any(m => m.Vertices.Count >= 3 && m.Indices.Count >= 3))
        {
            Assert.Contains("IFCCARTESIANPOINTLIST3D(", ifc);
            Assert.Contains("IFCTRIANGULATEDFACESET(", ifc);
        }

        Assert.Contains("END-ISO-10303-21;", ifc);
        if (expectations?.MinIfc4Bytes is { } minBytes)
        {
            long bytes = System.Text.Encoding.UTF8.GetByteCount(ifc);
            Assert.True(bytes >= minBytes, $"Expected IFC4 of at least {minBytes} bytes, got {bytes}");
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(NwcFixtures))]
    public void Save_NwcFixture_RoundtripsOrIsRejectedBeforeWriting(int index)
    {
        var original = NwcDocument.Open(PathOf(index));
        string tempNwc = Path.Combine(Path.GetTempPath(), $"roundtrip_{Guid.NewGuid():N}.nwc");

        try
        {
            try
            {
                original.Save(tempNwc);
            }
            catch (ArgumentException)
            {
                // The writer rejects what it cannot store (scaled poses, lines) before writing a byte.
                Assert.False(File.Exists(tempNwc));
                return;
            }

            Assert.True(new FileInfo(tempNwc).Length > 0);

            var reloaded = NwcDocument.Open(tempNwc);
            Assert.True(original.FileInfo.SourcePath == reloaded.FileInfo.SourcePath, "source path changed by the round trip");
            Assert.Equal(original.Meshes.Count, reloaded.Meshes.Count);
            Assert.Equal(original.Transforms.Count, reloaded.Transforms.Count);
            Assert.Equal(original.Guids.Count, reloaded.Guids.Count);
            Assert.Equal(original.SavedViewpoints.Count, reloaded.SavedViewpoints.Count);
        }
        finally
        {
            if (File.Exists(tempNwc))
            {
                File.Delete(tempNwc);
            }
        }
    }

    [RealMultiFixtureFact]
    public void MultiModelAssembly_FederatesTwoFixtures()
    {
        var files = TestFiles.EnumerateAll();
        var first = Open(files[0]);
        var second = Open(files[1]);

        var assembly = NwcAssembly.Create("Fixture federation", first, second);

        Assert.Equal(first.Meshes.Count + second.Meshes.Count, assembly.AllMeshes.Count);
        Assert.Equal(assembly.AllMeshes.Count, assembly.SpatialIndex.MeshCount);

        string tempGlb = Path.Combine(Path.GetTempPath(), $"assembly_{Guid.NewGuid():N}.glb");
        string tempBcf = Path.Combine(Path.GetTempPath(), $"assembly_{Guid.NewGuid():N}.bcfzip");
        string tempHtml = Path.Combine(Path.GetTempPath(), $"assembly_{Guid.NewGuid():N}.html");
        try
        {
            assembly.SaveGlb(tempGlb);
            Assert.True(new FileInfo(tempGlb).Length > 12);

            var clashes = assembly.FindClashes();
            assembly.SaveBcf(tempBcf, clashes);
            using (var archive = ZipFile.OpenRead(tempBcf))
            {
                Assert.NotNull(archive.GetEntry("bcf.version"));
                Assert.Equal(clashes.Count, archive.Entries.Count(e => e.FullName.EndsWith("/markup.bcf", StringComparison.Ordinal)));
            }

            assembly.SaveClashReportHtml(tempHtml, clashes);
            Assert.True(new FileInfo(tempHtml).Length > 0);
        }
        finally
        {
            foreach (string temp in new[] { tempGlb, tempBcf, tempHtml })
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
    }
}
