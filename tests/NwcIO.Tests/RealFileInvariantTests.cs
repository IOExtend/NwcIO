using System.Diagnostics;
using System.Globalization;
using NwcIO.Model;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace NwcIO.Tests;

/// <summary>
/// Opens every local fixture and checks format invariants. Privacy: nothing from a model reaches an
/// Assert call or the output; failures carry a file index and a fixed rule label only.
/// </summary>
public class RealFileInvariantTests
{
    private readonly ITestOutputHelper _output;

    public RealFileInvariantTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class InvariantViolation : XunitException
    {
        public InvariantViolation(string message) : base(message)
        {
        }
    }

    private sealed class Totals
    {
        public long Fragments;
        public long Triangles;
        public long Segments;
        public long Mirrored;
        public long LeavesWithoutMesh;
        public int MaxDepth;
        public long DistinctRecords;
    }

    [RealFixtureFact]
    public void OpenEveryFixture_HoldsReaderInvariants()
    {
        IReadOnlyList<string> files;
        try
        {
            files = TestFiles.EnumerateAll();
        }
        catch (Exception ex)
        {
            // An IO error message can carry a fixture path.
            throw new XunitException($"fixture enumeration: {ex.GetType().Name}");
        }

        var totals = new Totals();

        for (var i = 0; i < files.Count; i++)
        {
            try
            {
                if (files[i].EndsWith(".nwd", StringComparison.OrdinalIgnoreCase))
                {
                    CheckNwd(i, files[i], totals);
                }
                else
                {
                    CheckNwc(i, files[i], totals);
                }
            }
            catch (Exception ex)
            {
                var safe = ex is InvalidDataException or NotSupportedException or InvariantViolation
                    ? ex.Message
                    : string.Empty;
                throw new XunitException($"file #{i}: {ex.GetType().Name}: {safe}");
            }

            _output.WriteLine($"#{i} ok");
        }

        _output.WriteLine($"files {files.Count}, fragments {totals.Fragments}, distinct records {totals.DistinctRecords}");
        _output.WriteLine($"triangles {totals.Triangles}, segments {totals.Segments}, mirrored poses {totals.Mirrored}");
        _output.WriteLine($"leaves without mesh {totals.LeavesWithoutMesh}, max tree depth {totals.MaxDepth}");
    }

    private static void Check(bool condition, int file, string rule)
    {
        if (!condition)
        {
            throw new InvariantViolation($"file #{file}: {rule}");
        }
    }

    private void CheckNwd(int i, string path, Totals totals)
    {
        // The first document is not referenced after this call, so it can be collected before the second open.
        CheckMeshes(i, OpenWithoutTree(i, path), totals, withNodes: false);

        // This open inflates the whole container again and throws at the hierarchy, before any geometry record is decoded.
        Check(ThrowsNotSupported(path), i, "nwd-default-open-must-throw");
    }

    private NwcDocument OpenWithoutTree(int i, string path)
    {
        var sw = Stopwatch.StartNew();
        var doc = NwcDocument.Open(path, new NwcReaderOptions { BuildSceneTree = false });
        sw.Stop();
        using (var process = Process.GetCurrentProcess())
        {
            _output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"#{i} open {sw.Elapsed.TotalSeconds:F1} s, peak working set {process.PeakWorkingSet64 / (1024 * 1024)} MB"));
        }

        return doc;
    }

    private static bool ThrowsNotSupported(string path)
    {
        try
        {
            NwcDocument.Open(path);
        }
        catch (NotSupportedException)
        {
            return true;
        }

        return false;
    }

    private static void CheckNwc(int i, string path, Totals totals)
    {
        var doc = NwcDocument.Open(path);
        CheckMeshes(i, doc, totals, withNodes: true);
        Check(doc.DbCache?.Find("LcOpNwdGeometry") is not null, i, "dbcache-geometry-entry");
        Check(doc.RootNode is not null, i, "root-node");

        var stack = new Stack<(NwcNode Node, int Depth)>();
        stack.Push((doc.RootNode!, 1));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            totals.MaxDepth = Math.Max(totals.MaxDepth, depth);
            if (node.Children.Count == 0 && node.Tag == 22 && node.Meshes.Count == 0)
            {
                totals.LeavesWithoutMesh++;
            }

            foreach (var child in node.Children)
            {
                stack.Push((child, depth + 1));
            }
        }
    }

    private static void CheckMeshes(int i, NwcDocument doc, Totals totals, bool withNodes)
    {
        Check(doc.Meshes.Count == doc.Transforms.Count, i, "mesh-transform-count");
        var lastNodeId = int.MinValue;
        // Arrays are shared only within one file; a per-file set keeps nothing alive across files.
        var records = new HashSet<object>(ReferenceEqualityComparer.Instance);

        for (var k = 0; k < doc.Meshes.Count; k++)
        {
            var mesh = doc.Meshes[k];
            var transform = mesh.Transform;
            void Fail(string rule) => throw new InvariantViolation($"file #{i} mesh {k}: {rule}");

            if (mesh.Id != k) Fail("mesh-id");
            if (transform is null || !ReferenceEquals(doc.Transforms[k], transform)) Fail("transform-link");

            if (withNodes)
            {
                var node = mesh.Node;
                if (node is null || node.Tag != 22) Fail("leaf-tag");
                if (node!.Children.Count != 0) Fail("leaf-has-children");
                if (!node.Meshes.Contains(mesh)) Fail("leaf-lacks-mesh");
                if (!ReferenceEquals(node.Mesh, node.Meshes[0])) Fail("leaf-first-mesh");
                if (node.Id < lastNodeId) Fail("leaf-order");
                lastNodeId = node.Id;
            }

            var triangleIndices = mesh.Indices.Count;
            var lineIndices = mesh.LineIndices.Count;
            var vertexCount = mesh.Vertices.Count;
            if ((triangleIndices == 0) == (lineIndices == 0)) Fail("one-kind-of-index");
            if (triangleIndices % 3 != 0) Fail("triangle-index-count");
            if (lineIndices % 2 != 0) Fail("line-index-count");
            if (mesh.Indices.Any(x => x < 0 || x >= vertexCount)) Fail("triangle-index-range");
            if (mesh.LineIndices.Any(x => x < 0 || x >= vertexCount)) Fail("line-index-range");

            if ((mesh.Normals is not null) != (triangleIndices != 0)) Fail("normals-presence");
            if (mesh.Normals is not null)
            {
                if (mesh.Normals.Count != vertexCount) Fail("normals-count");
                foreach (var n in mesh.Normals)
                {
                    if (n != Vector3d.Zero && !(Math.Abs(1 - n.Length) < 1e-6)) Fail("normal-length");
                }
            }

            BoundingBox3d? box = null;
            if (vertexCount > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                foreach (var v in mesh.Vertices)
                {
                    minX = Math.Min(minX, v.X);
                    maxX = Math.Max(maxX, v.X);
                    minY = Math.Min(minY, v.Y);
                    maxY = Math.Max(maxY, v.Y);
                    minZ = Math.Min(minZ, v.Z);
                    maxZ = Math.Max(maxZ, v.Z);
                }

                box = new BoundingBox3d(new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ));
            }

            if (mesh.BoundingBox != box) Fail("bbox-vs-vertices");
            if (mesh.BoundingBox != transform!.BoundingBox) Fail("bbox-vs-transform");
            if (!double.IsFinite(transform.Scale) || transform.Scale == 0) Fail("scale");

            totals.Fragments++;
            if (transform.Scale < 0) totals.Mirrored++;
            if (records.Add(mesh.Vertices))
            {
                totals.Triangles += triangleIndices / 3;
                totals.Segments += lineIndices / 2;
            }
        }
        totals.DistinctRecords += records.Count;
    }
}
