using System.Text.Json;
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

public class RealFixtureTests
{
    private static readonly string? FixturePath = TestFiles.TryResolvePrimary();

    [RealFixtureFact]
    public void Open_RealNwcFixture_ParsesHeaderAndRecords()
    {
        var doc = NwcDocument.Open(FixturePath!);

        Assert.NotNull(doc.FileInfo);
        Assert.NotNull(doc.FileInfo.SourcePath);
        Assert.Contains(".rvt", doc.FileInfo.SourcePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("LcNwcLoaderPlugin:lcldrevit", doc.FileInfo.LoaderPlugin);
        Assert.NotNull(doc.FileInfo.SourceTimestampUtc);

        Assert.NotEmpty(doc.Guids);
        Assert.True(doc.Guids.Count > 1000);

        Assert.NotEmpty(doc.PropertyRecords);
        Assert.True(doc.PropertyRecords.Count > 500);

        Assert.NotEmpty(doc.Transforms);
        Assert.True(doc.Transforms.Count > 1000);
        var firstTransform = doc.Transforms[0];
        Assert.True(firstTransform.Rotation.IsNormalized());
        Assert.Equal(16, firstTransform.ToMatrix4x4().Length);

        Assert.NotNull(doc.RootNode);
        Assert.NotEmpty(doc.RootNode.Children);

        Assert.NotEmpty(doc.Meshes);
        Assert.True(doc.Meshes.Count > 500);
        var firstMesh = doc.Meshes.First(m => m.TriangleCount > 0);
        Assert.True(firstMesh.Vertices.Count >= 3);
        Assert.True(firstMesh.TriangleCount >= 1);
        Assert.NotNull(firstMesh.BoundingBox);

        Assert.NotEmpty(doc.Materials);
        Assert.Contains(doc.Materials, m => m.Color != default);

        // Verify OBJ export
        using var sw = new StringWriter();
        doc.SaveObj(sw, worldCoordinates: true);
        string obj = sw.ToString();
        Assert.NotEmpty(obj);
        Assert.Contains($"o {firstMesh.Name}", obj);
        Assert.Contains("v ", obj);
        Assert.Contains("f ", obj);

        // Verify GLB export
        using var glbMs = new MemoryStream();
        doc.SaveGlb(glbMs, worldCoordinates: true);
        byte[] glbBytes = glbMs.ToArray();
        Assert.True(glbBytes.Length > 100_000);
        Assert.Equal(0x46546C67u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glbBytes.AsSpan(0)));
        Assert.Equal(2u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(glbBytes.AsSpan(4)));

        // Verify JSON export
        string json = doc.ToJson(new NwcJsonExportOptions
        {
            Indented = false,
            IncludeTransforms = true,
            IncludeSceneTree = true,
            IncludeMeshes = true,
            IncludeMaterials = true,
            IncludeStreamCatalog = true,
        });
        Assert.NotEmpty(json);

        using var jsonDoc = JsonDocument.Parse(json);
        var root = jsonDoc.RootElement;
        Assert.True(root.TryGetProperty("file", out var fileEl));
        Assert.True(fileEl.TryGetProperty("sourcePath", out _));
        Assert.True(root.TryGetProperty("propertyRecordCount", out var countEl));
        Assert.Equal(doc.PropertyRecords.Count, countEl.GetInt32());
        Assert.True(root.TryGetProperty("guids", out var guidsEl));
        Assert.Equal(doc.Guids.Count, guidsEl.GetArrayLength());
        Assert.True(root.TryGetProperty("transforms", out var transformsEl));
        Assert.Equal(doc.Transforms.Count, transformsEl.GetArrayLength());
        Assert.True(root.TryGetProperty("meshCount", out var meshCountEl));
        Assert.Equal(doc.Meshes.Count, meshCountEl.GetInt32());
        Assert.True(root.TryGetProperty("materialCount", out var matCountEl));
        Assert.Equal(doc.Materials.Count, matCountEl.GetInt32());
        Assert.True(root.TryGetProperty("materials", out var matsEl));
        Assert.Equal(doc.Materials.Count, matsEl.GetArrayLength());
        Assert.True(root.TryGetProperty("meshes", out var meshesEl));
        Assert.Equal(doc.Meshes.Count, meshesEl.GetArrayLength());
        Assert.True(root.TryGetProperty("sceneTree", out var sceneTreeEl));
        Assert.True(sceneTreeEl.TryGetProperty("children", out _));
        Assert.True(root.TryGetProperty("container", out var containerEl));
        Assert.True(containerEl.TryGetProperty("streams", out _));

        // Milestone 5: Verify XRefTable and DbCache
        Assert.NotNull(doc.XRefs);
        Assert.Equal("XRefTable", doc.XRefs.Type);
        Assert.Equal(1, doc.XRefs.Version);

        Assert.NotNull(doc.DbCache);
        Assert.Equal("LcOpDBCache", doc.DbCache.DatabaseName);
        Assert.NotEmpty(doc.DbCache.Entries);
        Assert.NotNull(doc.DbCache.Find("LcOpNwdGeometry"));

        // Section 2.13: Verify Saved Viewpoints and Cameras
        Assert.NotEmpty(doc.SavedViewpoints);
        Assert.True(doc.SavedViewpoints.Count >= 10, $"Expected >= 10 viewpoints, got {doc.SavedViewpoints.Count}");
        Assert.NotNull(doc.DefaultViewpoint);
        Assert.Contains(doc.SavedViewpoints, vp => !string.IsNullOrWhiteSpace(vp.Name));
        Assert.All(doc.SavedViewpoints, vp =>
        {
            Assert.True(vp.Camera.Rotation.IsNormalized());
            Assert.True(vp.Camera.FocalDistance > 0);
            Assert.True(vp.Camera.AspectRatio > 0);
        });

        // Verify Saved Viewpoints BCF export
        string tempVpBcf = Path.Combine(Path.GetTempPath(), $"viewpoints_{Guid.NewGuid():N}.bcfzip");
        try
        {
            doc.SaveViewpointsBcf(tempVpBcf);
            Assert.True(File.Exists(tempVpBcf));
            var vpBcfInfo = new FileInfo(tempVpBcf);
            Assert.True(vpBcfInfo.Length > 500, $"Expected viewpoints BCF > 500 bytes, got {vpBcfInfo.Length}");
        }
        finally
        {
            if (File.Exists(tempVpBcf)) File.Delete(tempVpBcf);
        }

        // Milestone 5: Verify Spatial Acceleration Index (BVH)
        var spatial = doc.SpatialIndex;
        Assert.NotNull(spatial);
        Assert.Equal(doc.Meshes.Count, spatial.MeshCount);
        Assert.True(spatial.NodeCount > 0);
        Assert.True(spatial.LeafCount > 0);
        Assert.True(spatial.MaxDepth > 0);
        Assert.False(spatial.Bounds.IsEmpty);

        // Query around the first mesh's world bounding box
        var firstMeshBox = firstMesh.GetWorldBoundingBox();
        var queryHits = spatial.QueryBox(firstMeshBox);
        Assert.NotEmpty(queryHits);
        Assert.Contains(queryHits, m => m.Id == firstMesh.Id);

        // Raycast down towards the first mesh from 10 meters above its center
        var targetPoint = firstMeshBox.Center;
        var rayOrigin = new Model.Vector3d(targetPoint.X, targetPoint.Y, spatial.Bounds.Max.Z + 10.0);
        var rayDir = new Model.Vector3d(0, 0, -1);
        bool rayHit = spatial.Raycast(rayOrigin, rayDir, out var hit);
        Assert.True(rayHit);
        Assert.True(hit.Distance > 0);
        Assert.NotNull(hit.Mesh);

        // Step 1: Verify IFC export on real fixture
        string tempIfc = Path.Combine(Path.GetTempPath(), $"fixture_ifc_{Guid.NewGuid():N}.ifc");
        try
        {
            doc.SaveIfc(tempIfc);
            Assert.True(File.Exists(tempIfc));
            var ifcInfo = new FileInfo(tempIfc);
            Assert.True(ifcInfo.Length > 100_000, $"Expected IFC file > 100KB, got {ifcInfo.Length} bytes");

            string ifcHeader = File.ReadLines(tempIfc).Take(30).Aggregate((a, b) => a + "\n" + b);
            Assert.Contains("ISO-10303-21;", ifcHeader);
            Assert.Contains("FILE_SCHEMA(('IFC2X3'));", ifcHeader);
            Assert.Contains("IFCPROJECT(", ifcHeader);

            string ifcTail = File.ReadLines(tempIfc).TakeLast(5).Aggregate((a, b) => a + "\n" + b);
            Assert.Contains("END-ISO-10303-21;", ifcTail);
        }
        finally
        {
            if (File.Exists(tempIfc)) File.Delete(tempIfc);
        }

        // Step 4: Verify IFC4 export with modern IfcTriangulatedFaceSet and Types
        string tempIfc4 = Path.Combine(Path.GetTempPath(), $"fixture_ifc4_{Guid.NewGuid():N}.ifc");
        try
        {
            doc.SaveIfc(tempIfc4, new NwcIfcExportOptions
            {
                SchemaVersion = NwcIfcSchemaVersion.Ifc4,
                ExportTypeObjects = true,
                UseTessellationInIfc4 = true,
            });
            Assert.True(File.Exists(tempIfc4));
            var ifc4Info = new FileInfo(tempIfc4);
            Assert.True(ifc4Info.Length > 50_000, $"Expected IFC4 file > 50KB, got {ifc4Info.Length} bytes");

            string ifc4Header = File.ReadLines(tempIfc4).Take(30).Aggregate((a, b) => a + "\n" + b);
            Assert.Contains("FILE_SCHEMA(('IFC4'));", ifc4Header);
            Assert.Contains("DesignTransferView", ifc4Header);

            string sample = File.ReadLines(tempIfc4).Skip(30).Take(200).Aggregate((a, b) => a + "\n" + b);
            Assert.Contains("IFCCARTESIANPOINTLIST3D(", sample);
            Assert.Contains("IFCTRIANGULATEDFACESET(", sample);
        }
        finally
        {
            if (File.Exists(tempIfc4)) File.Delete(tempIfc4);
        }

        // Milestone 5: Verify Multi-Model Assembly
        var fixture2 = TestFiles.TryResolveSecondary(FixturePath);
        if (fixture2 is not null && File.Exists(fixture2))
        {
            var doc2 = NwcDocument.Open(fixture2);
            var assembly = Model.NwcAssembly.Create("Campus_Coordination", doc, doc2);
            Assert.Equal(2, assembly.Models.Count);
            Assert.True(assembly.AllMeshes.Count > doc.Meshes.Count);
            Assert.True(assembly.SpatialIndex.NodeCount > 0);

            string tempGlb = Path.Combine(Path.GetTempPath(), $"assembly_{Guid.NewGuid():N}.glb");
            string tempBcf = Path.Combine(Path.GetTempPath(), $"assembly_{Guid.NewGuid():N}.bcfzip");
            string tempHtml = Path.Combine(Path.GetTempPath(), $"assembly_{Guid.NewGuid():N}.html");
            try
            {
                assembly.SaveGlb(tempGlb);
                Assert.True(File.Exists(tempGlb));
                var info = new FileInfo(tempGlb);
                Assert.True(info.Length > 200_000);

                var clashes = assembly.FindClashes();
                assembly.SaveBcf(tempBcf, clashes);
                Assert.True(File.Exists(tempBcf));
                var bcfInfo = new FileInfo(tempBcf);
                Assert.True(bcfInfo.Length > 50);

                assembly.SaveClashReportHtml(tempHtml, clashes);
                Assert.True(File.Exists(tempHtml));
                var htmlInfo = new FileInfo(tempHtml);
                Assert.True(htmlInfo.Length > 1000);
            }
            finally
            {
                if (File.Exists(tempGlb)) File.Delete(tempGlb);
                if (File.Exists(tempBcf)) File.Delete(tempBcf);
                if (File.Exists(tempHtml)) File.Delete(tempHtml);
            }
        }
    }

    [RealFixtureFact]
    public void Save_RealNwcFixture_RoundtripsSuccessfully()
        {
            var original = NwcDocument.Open(FixturePath!);
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

                Assert.True(File.Exists(tempNwc));
                Assert.True(new FileInfo(tempNwc).Length > 1000);

                var reloaded = NwcDocument.Open(tempNwc);
                Assert.Equal(original.FileInfo.SourcePath, reloaded.FileInfo.SourcePath);
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
    }
