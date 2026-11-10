using System.IO.Compression;
using System.Text.Json;
using NwcIO.Model;
using NwcIO.Model.Clash;
using Xunit;

namespace NwcIO.Tests;

public class ClashMatrixTests
{
    private static NwcDocument CreateTestDoc(string modelName, int baseId, Vector3d offset, string layerName)
    {
        var node = new NwcNode { Id = baseId, Name = $"{modelName}_Element", Class = layerName };
        var vertices = new List<Vector3d>
        {
            offset + new Vector3d(0, 0, 0),
            offset + new Vector3d(2, 0, 0),
            offset + new Vector3d(2, 2, 0),
            offset + new Vector3d(0, 2, 0),
            offset + new Vector3d(0, 0, 2),
            offset + new Vector3d(2, 0, 2),
            offset + new Vector3d(2, 2, 2),
            offset + new Vector3d(0, 2, 2)
        };
        var indices = new List<int>
        {
            0, 1, 2, 0, 2, 3, // bottom
            4, 5, 6, 4, 6, 7, // top
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7
        };

        var mesh = new NwcMesh { Id = baseId, Vertices = vertices, Indices = indices, Node = node };
        var fileInfo = new NwcFileInfo { SourcePath = $"{modelName}.nwc" };

        return NwcDocument.Create(fileInfo: fileInfo, meshes: new[] { mesh });
    }

    [Fact]
    public void Matrix_DisciplineTests_DetectsCrossModelConflicts()
    {
        // Model 1: Structure at (0, 0, 0)
        var docStr = CreateTestDoc("Structural_Model", 100, new Vector3d(0, 0, 0), "Structural Columns");
        // Model 2: MEP at (1, 1, 1) - intersecting Structure
        var docMep = CreateTestDoc("MEP_Model", 200, new Vector3d(1, 1, 1), "HVAC Ducts");
        // Model 3: Architecture at (10, 10, 10) - far away
        var docArch = CreateTestDoc("Architectural_Model", 300, new Vector3d(10, 10, 10), "Exterior Walls");

        var assembly = NwcAssembly.Create("Coordination Assembly", docStr, docMep, docArch);

        var matrix = new NwcClashMatrix("BIM Coordination Matrix");
        matrix.AddDiscipline("Structure", "Structural");
        matrix.AddDiscipline("MEP", "MEP");
        matrix.AddDiscipline("Architecture", "Architectural");

        matrix.PopulateStandardMatrix(defaultTolerance: 0.001, includeSelfClash: false);

        Assert.Equal(3, matrix.Cells.Count);

        matrix.Execute(assembly);

        // Check Markdown table generation
        string md = matrix.ToMarkdownTable();
        Assert.NotNull(md);
        Assert.Contains("BIM Coordination Matrix", md);
        Assert.Contains("| Structure |", md);
        Assert.Contains("| MEP |", md);

        // Structure vs MEP should have 1 clash
        var strMepCell = matrix.Cells.First(c =>
            (c.DisciplineA == "Structure" && c.DisciplineB == "MEP") ||
            (c.DisciplineA == "MEP" && c.DisciplineB == "Structure"));
        Assert.Equal(1, strMepCell.ClashCount);

        // Structure vs Arch should have 0 clashes
        var strArchCell = matrix.Cells.First(c =>
            (c.DisciplineA == "Structure" && c.DisciplineB == "Architecture") ||
            (c.DisciplineA == "Architecture" && c.DisciplineB == "Structure"));
        Assert.Equal(0, strArchCell.ClashCount);

        // Total clashes in matrix should be 1
        Assert.Equal(1, matrix.TotalClashesCount);
    }

    [Fact]
    public void Matrix_JsonSerialization_ExportsValidFormat()
    {
        var matrix = new NwcClashMatrix("Hospital Coordination Matrix");
        matrix.AddDiscipline("ST", "Structure");
        matrix.AddDiscipline("MEP", "Ductwork");
        matrix.PopulateStandardMatrix(defaultTolerance: 0.005);

        string tempJson = Path.Combine(Path.GetTempPath(), $"matrix_{Guid.NewGuid():N}.json");
        try
        {
            matrix.SaveJson(tempJson, indent: true);
            Assert.True(File.Exists(tempJson));

            string jsonContent = File.ReadAllText(tempJson);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            Assert.Equal("Hospital Coordination Matrix", root.GetProperty("name").GetString());
            Assert.Equal(2, root.GetProperty("disciplines").GetArrayLength());
            Assert.Equal(1, root.GetProperty("cells").GetArrayLength());
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }

    [Fact]
    public void Matrix_BcfExport_ProducesValidBcfZipArchive()
    {
        var docA = CreateTestDoc("ModelA", 1, new Vector3d(0, 0, 0), "Steel");
        var docB = CreateTestDoc("ModelB", 2, new Vector3d(0.5, 0.5, 0.5), "Pipes");
        var assembly = NwcAssembly.Create("Test Assembly", docA, docB);

        var matrix = NwcClashMatrix.CreateFromAssembly(assembly, defaultTolerance: 0.001);
        matrix.Execute(assembly);

        Assert.True(matrix.TotalClashesCount > 0);

        string tempBcf = Path.Combine(Path.GetTempPath(), $"matrix_{Guid.NewGuid():N}.bcfzip");
        try
        {
            matrix.SaveBcf(tempBcf);
            Assert.True(File.Exists(tempBcf));

            using var zip = ZipFile.OpenRead(tempBcf);
            Assert.Contains(zip.Entries, e => e.FullName == "bcf.version");
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("/markup.bcf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(zip.Entries, e => e.FullName.EndsWith("/viewpoint.bcfv", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(tempBcf)) File.Delete(tempBcf);
        }
    }
}
