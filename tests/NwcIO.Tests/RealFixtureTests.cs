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

/// <summary>Marks a theory whose cases come from the discovered fixtures.</summary>
internal sealed class RealFixtureTheoryAttribute : TheoryAttribute
{
    public RealFixtureTheoryAttribute()
    {
        string? skipReason = TestFiles.DescribeSkipReason();
        if (skipReason is not null)
        {
            Skip = skipReason;
        }
    }
}

/// <summary>Marks a federated test that needs at least two fixtures.</summary>
internal sealed class RealMultiFixtureFactAttribute : FactAttribute
{
    public RealMultiFixtureFactAttribute()
    {
        string? skipReason = TestFiles.DescribeSkipReason();
        if (skipReason is null && TestFiles.EnumerateAll().Count < 2)
        {
            skipReason = "At least two .nwc/.nwd/.nwf fixtures are needed for the multi-model test.";
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
        if (!File.Exists(sidecar))
        {
            return null;
        }

        return JsonSerializer.Deserialize<FixtureExpectations>(File.ReadAllText(sidecar));
    }
}

public class RealFixtureTests
{
    public static IEnumerable<object[]> Fixtures() =>
        TestFiles.EnumerateAll().Select(path => new object[] { path });

    private static NwcDocument OpenFixture(string path, FixtureExpectations? expectations = null)
    {
        var doc = NwcDocument.Open(path);
        if (expectations?.SourceContains is { } source)
        {
            Assert.Contains(source, doc.FileInfo.SourcePath, StringComparison.OrdinalIgnoreCase);
        }

        if (expectations?.LoaderPlugin is { } loader)
        {
            Assert.Equal(loader, doc.FileInfo.LoaderPlugin);
        }

        return doc;
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Open_Fixture_ParsesModelInvariants(string path)
    {
        var expectations = FixtureExpectations.Load(path);
        var doc = OpenFixture(path, expectations);

        Assert.NotNull(doc.FileInfo);
        Assert.NotNull(doc.RootNode);

        if (expectations?.MinGuids is { } minGuids)
        {
            Assert.True(doc.Guids.Count >= minGuids, $"Expected at least {minGuids} GUIDs, got {doc.Guids.Count}");
        }

        if (expectations?.MinPropertyRecords is { } minRecords)
        {
            Assert.True(doc.PropertyRecords.Count >= minRecords);
        }

        if (expectations?.MinTransforms is { } minTransforms)
        {
            Assert.True(doc.Transforms.Count >= minTransforms);
        }

        if (expectations?.MinMeshes is { } minMeshes)
        {
            Assert.True(doc.Meshes.Count >= minMeshes);
        }

        foreach (var transform in doc.Transforms)
        {
            Assert.True(transform.Rotation.IsNormalized());
            Assert.Equal(16, transform.ToMatrix4x4().Length);
        }

        foreach (var mesh in doc.Meshes)
        {
            Assert.True(mesh.Vertices.Count >= 2);
            Assert.NotNull(mesh.BoundingBox);
        }

        var firstMesh = doc.Meshes.FirstOrDefault(m => m.TriangleCount > 0);
        if (firstMesh is not null)
        {
            Assert.True(firstMesh.Vertices.Count >= 3);
            Assert.True(firstMesh.TriangleCount >= 1);
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Exports_ObjGlbAndJson_AreStructurallyValid(string path)
    {
        var expectations = FixtureExpectations.Load(path);
        var doc = OpenFixture(path);
        var firstMesh = doc.Meshes.FirstOrDefault(m => m.Name.Length > 0);

        using var objWriter = new StringWriter();
        doc.SaveObj(objWriter, worldCoordinates: true);
        string obj = objWriter.ToString();
        Assert.Contains("v ", obj);
        Assert.Contains("f ", obj);
        if (firstMesh is not null)
        {
            Assert.Contains($"o {firstMesh.Name}", obj);
        }

        using var glbStream = new MemoryStream();
        doc.SaveGlb(glbStream, worldCoordinates: true);
        byte[] glb = glbStream.ToArray();
        Assert.Equal(0x46546C67u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0)));
        Assert.Equal(2u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)));
        if (expectations?.MinGlbBytes is { } minGlb)
        {
            Assert.True(glb.Length >= minGlb);
        }

        using var jsonStream = new MemoryStream();
        doc.SaveJson(jsonStream, new NwcJsonExportOptions
        {
            Indented = false,
            IncludeTransforms = true,
            IncludeSceneTree = true,
            IncludeMeshes = true,
            IncludeMaterials = true,
            IncludeStreamCatalog = true,
        });
        using var json = JsonDocument.Parse(jsonStream.ToArray());
        var root = json.RootElement;
        Assert.Equal(doc.Meshes.Count, root.GetProperty("meshCount").GetInt32());
        Assert.Equal(doc.Materials.Count, root.GetProperty("materialCount").GetInt32());
        Assert.Equal(doc.Guids.Count, root.GetProperty("guids").GetArrayLength());
        Assert.Equal(doc.Transforms.Count, root.GetProperty("transforms").GetArrayLength());
        Assert.True(root.GetProperty("sceneTree").TryGetProperty("children", out _));
        Assert.True(root.GetProperty("container").TryGetProperty("streams", out _));
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Viewpoints_ExportToBcf_WhenPresent(string path)
    {
        var expectations = FixtureExpectations.Load(path);
        var doc = OpenFixture(path);

        if (expectations?.MinViewpoints is { } minViewpoints)
        {
            Assert.True(doc.SavedViewpoints.Count >= minViewpoints);
        }

        foreach (var viewpoint in doc.SavedViewpoints)
        {
            Assert.NotNull(viewpoint.Camera);
            Assert.True(viewpoint.Camera.Rotation.IsNormalized());
        }

        if (doc.SavedViewpoints.Count == 0)
        {
            return;
        }

        using var stream = new MemoryStream();
        doc.SaveViewpointsBcf(stream);
        Assert.True(stream.Length > 0);
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void SpatialIndex_CoversEveryMesh(string path)
    {
        var doc = OpenFixture(path);
        Assert.Equal(doc.Meshes.Count, doc.SpatialIndex.MeshCount);
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Ifc2x3_Export_HasHeaderAndFooter(string path)
    {
        var expectations = FixtureExpectations.Load(path);
        var doc = OpenFixture(path);
        using var writer = new StringWriter();

        doc.SaveIfc(writer, new NwcIfcExportOptions { SchemaVersion = NwcIfcSchemaVersion.Ifc2x3 });

        string ifc = writer.ToString();
        Assert.Contains("ISO-10303-21;", ifc);
        Assert.Contains("FILE_SCHEMA(('IFC2X3'));", ifc);
        Assert.Contains("END-ISO-10303-21;", ifc);
        if (expectations?.MinIfc2x3Bytes is { } minBytes)
        {
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(ifc) >= minBytes);
        }
    }

    [RealFixtureTheory]
    [MemberData(nameof(Fixtures))]
    public void Ifc4_Export_HasHeaderAndFooter(string path)
    {
        var expectations = FixtureExpectations.Load(path);
        var doc = OpenFixture(path);
        using var writer = new StringWriter();

        doc.SaveIfc(writer, new NwcIfcExportOptions
        {
            SchemaVersion = NwcIfcSchemaVersion.Ifc4,
            UseTessellationInIfc4 = true,
        });

        string ifc = writer.ToString();
        Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc);
        Assert.Contains("IFCTRIANGULATEDFACESET(", ifc);
        Assert.Contains("END-ISO-10303-21;", ifc);
        if (expectations?.MinIfc4Bytes is { } minBytes)
        {
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(ifc) >= minBytes);
        }
    }

    [RealMultiFixtureFact]
    public void MultiModelAssembly_FederatesAvailableFixtures()
    {
        var files = TestFiles.EnumerateAll();
        if (files.Count < 2)
        {
            return;
        }

        var assembly = NwcAssembly.Create("Fixture federation", NwcDocument.Open(files[0]), NwcDocument.Open(files[1]));
        Assert.Equal(2, assembly.Models.Count);
        Assert.NotEmpty(assembly.AllMeshes);
        Assert.False(assembly.BoundingBox.IsEmpty);
    }
}
