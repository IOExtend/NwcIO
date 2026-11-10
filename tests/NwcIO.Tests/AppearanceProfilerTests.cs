using NwcIO.Model;
using NwcIO.Model.Appearance;
using NwcIO.Model.Sets;
using Xunit;

namespace NwcIO.Tests;

public sealed class AppearanceProfilerTests
{
    [Fact]
    public void RuleMatchesNodeByProperty()
    {
        var node = new NwcNode { Id = 1, Name = "Supply Duct 100", Class = "LcRevitData_Element" };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Class = "LcOaNode", Name = "LcOaNodeName", Value = "Supply Duct 100" });
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 2, Class = "Mechanical", Name = "System", Value = "Supply Air" });

        var ruleMatch = new NwcAppearanceRule(
            "Supply Air Blue",
            new NwcSearchCondition("Mechanical", "System", NwcSearchOperator.Equals, "Supply Air"),
            new RgbaColor(0.0f, 0.5f, 1.0f, 1.0f));

        var ruleMismatch = new NwcAppearanceRule(
            "Return Air Red",
            new NwcSearchCondition("Mechanical", "System", NwcSearchOperator.Equals, "Return Air"),
            new RgbaColor(1.0f, 0.0f, 0.0f, 1.0f));

        Assert.True(ruleMatch.Matches(node));
        Assert.False(ruleMismatch.Matches(node));
    }

    [Fact]
    public void ApplyAppearanceProfilerOverridesNodeAndMeshColor()
    {
        var mesh1 = new NwcMesh
        {
            Id = 1,
            Name = "Duct_1",
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
        };
        var mesh2 = new NwcMesh
        {
            Id = 2,
            Name = "Wall_1",
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        var nodeDuct = new NwcNode { Id = 1, Name = "Duct_1", Class = "LcRevitData_Element", Mesh = mesh1 };
        nodeDuct.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Class = "Item", Name = "Type", Value = "HVAC Duct" });

        var nodeWall = new NwcNode { Id = 2, Name = "Wall_1", Class = "LcRevitData_Element", Mesh = mesh2 };
        nodeWall.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 2, Class = "Item", Name = "Type", Value = "Basic Wall" });

        var root = new NwcNode { Id = 0, Name = "Root", Class = "LcOpModelFile" };
        root.Children.Add(nodeDuct);
        root.Children.Add(nodeWall);

        var doc = NwcDocument.Create(
            meshes: [mesh1, mesh2],
            rootNode: root);

        var profiler = new NwcAppearanceProfiler();
        profiler.AddRule(
            name: "HVAC Blue",
            category: "Item",
            property: "Type",
            op: NwcSearchOperator.Contains,
            value: "HVAC",
            color: new RgbaColor(0.1f, 0.2f, 0.9f, 1.0f),
            transparency: 0.3f);

        int overridden = doc.ApplyAppearanceProfiler(profiler);

        Assert.Equal(1, overridden);
        Assert.NotNull(nodeDuct.Material);
        Assert.Equal("Profiler_HVAC Blue", nodeDuct.Material.Name);
        Assert.Equal(0.1f, nodeDuct.Material.Color.R, 3);
        Assert.Equal(0.2f, nodeDuct.Material.Color.G, 3);
        Assert.Equal(0.9f, nodeDuct.Material.Color.B, 3);
        Assert.Equal(0.7f, nodeDuct.Material.Color.A, 3); // 1.0 - 0.3 = 0.7

        Assert.NotNull(mesh1.Material);
        Assert.Equal(nodeDuct.Material, mesh1.Material);

        Assert.Null(nodeWall.Material);
    }

    [Fact]
    public void DocumentBuilderAppliesAppearanceProfiler()
    {
        var mesh = new NwcMesh
        {
            Id = 1,
            Name = "Pipe_1",
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            Indices = [0, 1, 2],
        };

        var node = new NwcNode { Id = 1, Name = "Pipe_1", Class = "LcRevitData_Element", Mesh = mesh };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Class = "Piping", Name = "Service", Value = "Hydronic Return" });

        var root = new NwcNode { Id = 0, Name = "Building", Class = "LcOpModelFile" };
        root.Children.Add(node);

        var rule = new NwcAppearanceRule(
            "Hydronic Green",
            new NwcSearchCondition("Piping", "Service", NwcSearchOperator.Contains, "Hydronic"),
            new RgbaColor(0.0f, 0.8f, 0.2f, 1.0f),
            transparency: 0.1f);

        var doc = new NwcDocumentBuilder()
            .AddMesh(mesh)
            .SetRootNode(root)
            .AddAppearanceRule(rule)
            .Build();

        Assert.NotNull(doc.AppearanceProfiler);
        Assert.Single(doc.AppearanceProfiler.Rules);
        Assert.NotNull(node.Material);
        Assert.Equal("Profiler_Hydronic Green", node.Material.Name);
        Assert.Equal(0.0f, node.Material.Color.R, 3);
        Assert.Equal(0.8f, node.Material.Color.G, 3);
        Assert.Equal(0.2f, node.Material.Color.B, 3);
        Assert.Equal(0.9f, node.Material.Color.A, 3);
    }

    [Fact]
    public void XmlAppearanceProfilerRoundtrip()
    {
        var profiler = new NwcAppearanceProfiler();
        profiler.AddRule("Walls Red", "Element", "Category", NwcSearchOperator.Equals, "Walls", new RgbaColor(1.0f, 0.0f, 0.0f, 1.0f), 0.25f);
        profiler.AddRule("Ducts Cyan", "System", "Name", NwcSearchOperator.StartsWith, "Supply", new RgbaColor(0.0f, 1.0f, 1.0f, 1.0f), 0.0f);

        using var ms = new MemoryStream();
        NwcXmlAppearanceProfilerSerializer.Write(profiler, ms);
        ms.Position = 0;

        var loaded = NwcXmlAppearanceProfilerSerializer.Read(ms);

        Assert.Equal(2, loaded.Rules.Count);

        var rule1 = loaded.Rules[0];
        Assert.Equal("Walls Red", rule1.Name);
        Assert.True(rule1.Enabled);
        Assert.Equal("Element", rule1.Condition.Category);
        Assert.Equal("Category", rule1.Condition.Property);
        Assert.Equal(NwcSearchOperator.Equals, rule1.Condition.Operator);
        Assert.Equal("Walls", rule1.Condition.Value);
        Assert.Equal(1.0f, rule1.Color.R, 3);
        Assert.Equal(0.0f, rule1.Color.G, 3);
        Assert.Equal(0.0f, rule1.Color.B, 3);
        Assert.Equal(0.25f, rule1.Transparency, 3);

        var rule2 = loaded.Rules[1];
        Assert.Equal("Ducts Cyan", rule2.Name);
        Assert.Equal("System", rule2.Condition.Category);
        Assert.Equal("Name", rule2.Condition.Property);
        Assert.Equal(NwcSearchOperator.StartsWith, rule2.Condition.Operator);
        Assert.Equal("Supply", rule2.Condition.Value);
        Assert.Equal(0.0f, rule2.Color.R, 3);
        Assert.Equal(1.0f, rule2.Color.G, 3);
        Assert.Equal(1.0f, rule2.Color.B, 3);
        Assert.Equal(0.0f, rule2.Transparency, 3);
    }

    [Fact]
    public void ExportAppearanceProfilerXmlThrowsWhenNull()
    {
        var doc = NwcDocument.Create();
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => doc.ExportAppearanceProfilerXml(ms));
    }

    [Fact]
    public void CliAppliesAppearanceProfilerAndExports()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "NwcIO_CliProfilerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string nwcPath = Path.Combine(tempDir, "model.nwc");
            string profilerXmlPath = Path.Combine(tempDir, "rules.xml");
            string outputGlbPath = Path.Combine(tempDir, "output.glb");
            string exportedRulesPath = Path.Combine(tempDir, "exported_rules.xml");

            var mesh = new NwcMesh
            {
                Id = 1,
                Name = "Vent_1",
                Vertices = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
                Indices = [0, 1, 2],
            };
            var node = new NwcNode { Id = 1, Name = "Vent_1", Class = "LcRevitData_Element", Mesh = mesh };
            node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 1, Class = "Item", Name = "System", Value = "Exhaust" });
            var root = new NwcNode { Id = 0, Name = "Building", Class = "LcOpModelFile" };
            root.Children.Add(node);

            var doc = NwcDocument.Create(meshes: [mesh], rootNode: root);
            doc.Save(nwcPath);

            var profiler = new NwcAppearanceProfiler();
            profiler.AddRule("Exhaust Magenta", "Item", "System", NwcSearchOperator.Equals, "Exhaust", new RgbaColor(1.0f, 0.0f, 1.0f, 1.0f), 0.2f);
            NwcXmlAppearanceProfilerSerializer.Save(profiler, profilerXmlPath);

            int exitCode = NwcIO.Cli.Program.Main([
                nwcPath,
                outputGlbPath,
                "--profiler", profilerXmlPath,
                "--export-profiler", exportedRulesPath
            ]);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputGlbPath));
            Assert.True(File.Exists(exportedRulesPath));

            var reloaded = NwcXmlAppearanceProfilerSerializer.Open(exportedRulesPath);
            Assert.Single(reloaded.Rules);
            Assert.Equal("Exhaust Magenta", reloaded.Rules[0].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
