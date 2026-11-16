using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class SceneTreeBuilderTests
{
    [Fact]
    public void Build_HierarchyRecords_ConstructsNestedTree()
    {
        var records = new List<NwcPropertyRecord>
        {
            // Layer node
            new() { FirstInt = 1, Tag = 53, Name = "Level 1", Class = "Node" },
            new() { FirstInt = 2, Tag = 52, Name = "Layer", Class = "LcRevitLayer" },

            // Category collection
            new() { FirstInt = 3, Tag = 53, Name = "Walls", Class = "Node" },
            new() { FirstInt = 4, Tag = 52, Name = "Категория", Class = "LcRevitCollection" },

            // Family collection
            new() { FirstInt = 5, Tag = 53, Name = "Basic Wall", Class = "Node" },
            new() { FirstInt = 6, Tag = 52, Name = "Семейство", Class = "LcRevitCollection" },

            // Type collection
            new() { FirstInt = 7, Tag = 53, Name = "Concrete 200mm", Class = "Node" },
            new() { FirstInt = 8, Tag = 52, Name = "Тип", Class = "LcRevitCollection" },

            // Element composite
            new() { FirstInt = 9, Tag = 52, Name = "Walls: Basic Wall: Concrete 200mm", Class = "LcRevitComposite" },

            // Element properties
            new() { FirstInt = 10, Tag = 52, Name = "Length", Class = "LcRevitPropertyLength", ValueType = 4, Value = "5000 mm" },
        };

        var root = SceneTreeBuilder.Build("TestModel", records);

        Assert.Equal("TestModel", root.Name);
        Assert.Single(root.Children);

        var layer = root.Children[0];
        Assert.Equal("Level 1", layer.Name);
        Assert.Equal("LcRevitLayer", layer.Class);
        Assert.Single(layer.Children);

        var category = layer.Children[0];
        Assert.Equal("Walls", category.Name);
        Assert.Equal("LcRevitCollection", category.Class);
        Assert.Single(category.Children);

        var family = category.Children[0];
        Assert.Equal("Basic Wall", family.Name);
        Assert.Single(family.Children);

        var type = family.Children[0];
        Assert.Equal("Concrete 200mm", type.Name);
        Assert.Single(type.Children);

        var composite = type.Children[0];
        Assert.Equal("Walls: Basic Wall: Concrete 200mm", composite.Name);
        Assert.Single(composite.Properties);
        Assert.Equal("Length", composite.Properties[0].Name);
        Assert.Equal("5000 mm", composite.Properties[0].Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ExplicitParentChain_EnforcesTheNestingLimit(bool overLimit)
    {
        int depth = LcObjectReader.MaxNestingDepth + (overLimit ? 1 : 0);
        var records = new List<NwcPropertyRecord>();
        for (int id = 1; id <= depth; id++)
        {
            records.Add(new NwcPropertyRecord { FirstInt = id, Tag = 53, Name = $"N{id}", Class = "Node", ParentId = id - 1 });
            records.Add(new NwcPropertyRecord { FirstInt = id, Tag = 52, Name = "n", Class = "LcTestGroup" });
        }

        if (overLimit)
        {
            Assert.Throws<InvalidDataException>(() => SceneTreeBuilder.Build("root", records));
        }
        else
        {
            Assert.NotNull(SceneTreeBuilder.Build("root", records));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_DeepChainWithTransformOrGuid_ThrowsBeforeRecursiveAttachment(bool useGuid)
    {
        // The depth check must run before the recursive transform/GUID passes, otherwise a crafted
        // chain overflows the stack (uncatchable) instead of throwing.
        const int depth = 200_000;
        var records = new List<NwcPropertyRecord>(depth * 2);
        for (int id = 1; id <= depth; id++)
        {
            records.Add(new NwcPropertyRecord { FirstInt = id, Tag = 53, Name = $"N{id}", Class = "Node", ParentId = id - 1 });
            records.Add(new NwcPropertyRecord { FirstInt = id, Tag = 52, Name = "n", Class = "LcTestGroup" });
        }

        Assert.Throws<InvalidDataException>(() => useGuid
            ? SceneTreeBuilder.Build("root", records, guids: [Guid.NewGuid()])
            : SceneTreeBuilder.Build("root", records, transforms: [new NwcTransform { Id = 1 }]));
    }

    [Fact]
    public void Open_Save_Open_PreservesExplicitParentHierarchy()
    {
        var root = new NwcNode { Id = 1, Name = "Site", Class = "LcTestGroup" };
        var building = new NwcNode { Id = 2, Name = "Building", Class = "LcTestGroup", Parent = root };
        var wall = new NwcNode { Id = 3, Name = "Wall-1", Class = "LcRevitData_Element", Parent = building };
        root.Children.Add(building);
        building.Children.Add(wall);

        byte[] firstGeneration = NwcIO.NwcDocument.Create(rootNode: root).ToByteArray();
        var firstOpen = NwcIO.NwcDocument.Open(new MemoryStream(firstGeneration));
        var secondOpen = NwcIO.NwcDocument.Open(new MemoryStream(firstOpen.ToByteArray()));

        var model = secondOpen.RootNode!;
        Assert.Equal("Site", model.Name);
        var buildingNode = Assert.Single(model.Children);
        Assert.Equal("Building", buildingNode.Name);
        var wallNode = Assert.Single(buildingNode.Children);
        Assert.Equal("Wall-1", wallNode.Name);
    }
}
