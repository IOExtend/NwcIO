using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class AssemblyTests
{
    [Fact]
    public void Create_CombinesMultipleDocumentsIntoFederatedAssembly()
    {
        var meshA = new NwcMesh
        {
            Id = 1,
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0)),
        };

        var meshB = new NwcMesh
        {
            Id = 2,
            Vertices = [new(10, 0, 0), new(11, 0, 0), new(10, 1, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(10, 0, 0), new(11, 1, 0)),
        };

        var docA = CreateMockDocument("Model_A.rvt", [meshA]);
        var docB = CreateMockDocument("Model_B.rvt", [meshB]);

        var assembly = NwcAssembly.Create("FederatedCampus", docA, docB);

        Assert.Equal("FederatedCampus", assembly.Name);
        Assert.Equal(2, assembly.Models.Count);
        Assert.Equal(2, assembly.AllMeshes.Count);
        Assert.Equal(2, assembly.RootNode.Children.Count);

        // Overall bounds should encompass both meshes from X=0 to X=11
        Assert.Equal(0.0, assembly.BoundingBox.Min.X);
        Assert.Equal(11.0, assembly.BoundingBox.Max.X);

        // BVH Spatial index on assembly
        var hits = assembly.SpatialIndex.QueryBox(new BoundingBox3d(new(9, -1, -1), new(12, 2, 1)));
        Assert.Single(hits);
        Assert.Equal(2, hits[0].Id);
    }

    [Fact]
    public void FindClashes_DetectsInterferingMeshes()
    {
        // Two meshes in different documents occupying the same volume [0, 1]^3
        var meshA = new NwcMesh
        {
            Id = 10,
            Vertices = [new(0, 0, 0), new(2, 0, 0), new(0, 2, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(0, 0, 0), new(2, 2, 0)),
        };

        var meshB = new NwcMesh
        {
            Id = 20,
            Vertices = [new(1, 1, 0), new(3, 1, 0), new(1, 3, 0)],
            Indices = [0, 1, 2],
            BoundingBox = new BoundingBox3d(new(1, 1, 0), new(3, 3, 0)),
        };

        var docA = CreateMockDocument("Architecture.rvt", [meshA]);
        var docB = CreateMockDocument("Structure.rvt", [meshB]);

        var assembly = NwcAssembly.Create("Coordination", docA, docB);

        var clashes = assembly.FindClashes();

        Assert.Single(clashes);
        var clash = clashes[0];
        Assert.Equal(10, clash.MeshA.Id);
        Assert.Equal(20, clash.MeshB.Id);
        // Overlap box should be [1, 2] in X and Y
        Assert.Equal(1.0, clash.IntersectionBox.Min.X);
        Assert.Equal(2.0, clash.IntersectionBox.Max.X);
    }

    private static NwcDocument CreateMockDocument(string sourceName, IReadOnlyList<NwcMesh> meshes)
    {
        // Synthetic minimal container with PartitionHeader
        using var ms = new MemoryStream();
        ms.Write("#LcUStream-V1.1 binary double release C lichunk-008\n"u8);

        // Zlib stream containing PartitionHeader
        using var zlibMs = new MemoryStream();
        using (var deflater = new System.IO.Compression.ZLibStream(zlibMs, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            using var w = new BinaryWriter(deflater);
            w.Write((long)7);
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(sourceName);
            w.Write(nameBytes.Length);
            w.Write(nameBytes);
            w.Write(-1);
            w.Write((long)1692371995); // timestamp
            w.Write((long)1024);       // length
            byte[] pluginBytes = System.Text.Encoding.UTF8.GetBytes("LcNwcLoaderPlugin:mock");
            w.Write(pluginBytes.Length);
            w.Write(pluginBytes);
        }

        ms.Write(zlibMs.ToArray());
        byte[] containerBytes = ms.ToArray();

        string tempPath = Path.Combine(Path.GetTempPath(), $"mock_{Guid.NewGuid():N}.nwc");
        try
        {
            File.WriteAllBytes(tempPath, containerBytes);
            var doc = NwcDocument.Open(tempPath, new NwcReaderOptions { ReadMeshes = false });

            // Assign the meshes to the mock document via reflection or custom assembly creation
            var prop = typeof(NwcDocument).GetProperty(nameof(NwcDocument.Meshes));
            var backingField = typeof(NwcDocument).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .FirstOrDefault(f => f.Name.Contains("Meshes", StringComparison.OrdinalIgnoreCase));
            if (backingField is not null)
            {
                backingField.SetValue(doc, meshes);
            }

            return doc;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static NwcDocument CreateComponentModel(string sourcePath, int meshId, double offsetX)
    {
        return new NwcDocumentBuilder().SetFileInfo(sourcePath)
            .AddMesh(new NwcMesh
            {
                Id = meshId,
                Vertices = [new(offsetX, 0, 0), new(offsetX + 1, 0, 0), new(offsetX, 1, 0)],
                Indices = [0, 1, 2],
                BoundingBox = new BoundingBox3d(new(offsetX, 0, 0), new(offsetX + 1, 1, 0)),
            })
            .Build();
    }

    private static void WriteNwfWithXRefs(string nwfPath, params string[] effectivePaths)
    {
        string table = string.Join(",", effectivePaths.Select(p =>
            "{\"OriginalPath\":\"" + p.Replace('\\', '/') + "\",\"RemappedPath\":\"\"}"));
        string json = "{\"Type\":\"XRefTable\",\"Version\":1,\"SavedFilename\":\"fed.nwf\",\"Table\":[" + table + "]}";
        byte[] xrefStream = System.Text.Encoding.UTF8.GetBytes(json);
        using var fs = File.Create(nwfPath);
        NwcIO.Internal.Container.NwcContainerWriter.Write(fs, [xrefStream]);
    }

    [Fact]
    public void OpenNwf_ResolvesSubfoldersDeduplicatesAndReportsMissing()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "nwcio-nwf-" + Guid.NewGuid().ToString("N"));
        string archDir = Path.Combine(tempDir, "Arch");
        Directory.CreateDirectory(archDir);
        try
        {
            CreateComponentModel("A.rvt", 1, offsetX: 0).Save(Path.Combine(archDir, "A.nwc"));
            CreateComponentModel("B.rvt", 2, offsetX: 100).Save(Path.Combine(archDir, "B.nwc"));
            CreateComponentModel("C.rvt", 3, offsetX: 200).Save(Path.Combine(tempDir, "C.nwc"));
            Directory.CreateDirectory(Path.Combine(tempDir, "Textures"));
            File.WriteAllText(Path.Combine(tempDir, "Textures", "tex.png"), "not really a png");

            string nowhere = Path.Combine(tempDir, "nowhere").Replace(@"\", "/");
            string nwfPath = Path.Combine(tempDir, "fed.nwf");
            WriteNwfWithXRefs(
                nwfPath,
                nowhere + "/Arch/A.nwc",            // dead absolute root; subfolder must be kept
                "Arch/B.nwc",                       // relative: resolves against the nwf dir, not CWD
                nowhere + "/Arch/B.nwc",            // same file via a different stored path: dedup
                nowhere + "/flat/C.nwc",            // subfolder missing: bare file-name fallback
                nowhere + "/Mech/D.nwc",            // nowhere to be found: unresolved
                "Textures/tex.png");                // resolves but unsupported: skipped

            var assembly = NwcAssembly.OpenNwf(nwfPath);

            // nwf doc + A, B, C — B must appear once despite two references.
            Assert.Equal(4, assembly.Models.Count);
            Assert.Equal(3, assembly.AllMeshes.Count);
            Assert.Equal(1, assembly.AllMeshes.Count(m => Math.Abs(m.BoundingBox!.Value.Min.X - 0.0) < 1e-6));
            Assert.Equal(1, assembly.AllMeshes.Count(m => Math.Abs(m.BoundingBox!.Value.Min.X - 100.0) < 1e-6));
            Assert.Equal(1, assembly.AllMeshes.Count(m => Math.Abs(m.BoundingBox!.Value.Min.X - 200.0) < 1e-6));

            var unresolved = Assert.Single(assembly.UnresolvedXRefs);
            Assert.EndsWith("Mech/D.nwc", unresolved.EffectivePath);
            var skipped = Assert.Single(assembly.SkippedXRefs);
            Assert.EndsWith("tex.png", skipped.EffectivePath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void XRefTryResolve_PrefersLongestSuffixUnderBaseDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "nwcio-xref-" + Guid.NewGuid().ToString("N"));
        string archDir = Path.Combine(tempDir, "Arch");
        Directory.CreateDirectory(archDir);
        try
        {
            File.WriteAllText(Path.Combine(archDir, "A.nwc"), "x");
            File.WriteAllText(Path.Combine(tempDir, "D.nwc"), "x");

            var subfolder = new NwcXRef("Z:/dead/root/Arch/A.nwc", "");
            Assert.True(subfolder.TryResolve(tempDir, out string resolvedSub));
            Assert.Equal(Path.Combine(archDir, "A.nwc"), resolvedSub);

            // The 'flat' subfolder does not exist under the base directory, so the
            // bare file name is the fallback that finds D.nwc at the root.
            var bareName = new NwcXRef("Z:/dead/root/flat/D.nwc", "");
            Assert.True(bareName.TryResolve(tempDir, out string resolvedBare));
            Assert.Equal(Path.Combine(tempDir, "D.nwc"), resolvedBare);

            var relative = new NwcXRef("Arch/A.nwc", "");
            Assert.True(relative.Exists(tempDir));

            var missing = new NwcXRef("Z:/dead/root/Mech/Missing.nwc", "");
            Assert.False(missing.TryResolve(tempDir, out _));
            Assert.False(missing.Exists(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void OpenNwf_RelativeReferencePrefersNwfDirectoryOverCwd()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "nwcio-cwd-" + Guid.NewGuid().ToString("N"));
        string nwfDir = Path.Combine(tempDir, "federation");
        string relativeDir = "nwcio-decoy-" + Guid.NewGuid().ToString("N");
        string nwfArch = Path.Combine(nwfDir, relativeDir);
        string cwdArch = Path.Combine(Directory.GetCurrentDirectory(), relativeDir);
        Directory.CreateDirectory(nwfArch);
        Directory.CreateDirectory(cwdArch);
        try
        {
            // The real component next to the .nwf (offset 0) and a DIFFERENT decoy
            // with the same relative name reachable from the process CWD (offset 500).
            CreateComponentModel("Real.rvt", 1, offsetX: 0).Save(Path.Combine(nwfArch, "B.nwc"));
            CreateComponentModel("Decoy.rvt", 2, offsetX: 500).Save(Path.Combine(cwdArch, "B.nwc"));
            CreateComponentModel("Missing.rvt", 3, offsetX: 1000).Save(Path.Combine(cwdArch, "Missing.nwc"));

            string nwfPath = Path.Combine(nwfDir, "fed.nwf");
            string missingPath = Path.Combine(relativeDir, "Missing.nwc");
            WriteNwfWithXRefs(nwfPath, Path.Combine(relativeDir, "B.nwc"), missingPath);

            var assembly = NwcAssembly.OpenNwf(nwfPath);

            Assert.Equal(2, assembly.Models.Count);
            var mesh = Assert.Single(assembly.AllMeshes);
            Assert.Equal(0.0, mesh.BoundingBox!.Value.Min.X);
            Assert.Equal(missingPath.Replace('\\', '/'), Assert.Single(assembly.UnresolvedXRefs).EffectivePath);

            var missing = new NwcXRef(missingPath, "");
            Assert.False(missing.TryResolve(nwfDir, out _));
            Assert.False(missing.Exists(nwfDir));
            Assert.True(missing.TryResolve(null, out _));
        }
        finally
        {
            Directory.Delete(tempDir, true);
            Directory.Delete(cwdArch, true);
        }
    }
}
