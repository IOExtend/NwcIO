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
    [InlineData("Category", "Family", "Type")]
    [InlineData("Kategorie", "Familie", "Typ")]
    [InlineData("Catégorie", "Famille", "Typ")]
    public void Build_NonRussianMarkers_ConstructsNestedTree(string category, string family, string type)
    {
        var records = BuildHierarchy(category, family, type);

        var root = SceneTreeBuilder.Build("TestModel", records);

        var layer = Assert.Single(root.Children);
        var categoryNode = Assert.Single(layer.Children);
        var familyNode = Assert.Single(categoryNode.Children);
        var typeNode = Assert.Single(familyNode.Children);
        var element = Assert.Single(typeNode.Children);
        Assert.Equal("Wall 1", element.Name);
    }

    [Fact]
    public void Build_UnknownCollectionNames_NestByRecordOrder()
    {
        var records = new List<NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "Level 1", Class = "LcRevitLayer" },
            new() { FirstInt = 2, Tag = 53, Name = "Walls", Class = "Node" },
            new() { FirstInt = 3, Tag = 52, Name = "Zone A", Class = "LcRevitCollection" },
            new() { FirstInt = 4, Tag = 53, Name = "Basic Wall", Class = "Node" },
            new() { FirstInt = 5, Tag = 52, Name = "Assembly B", Class = "LcRevitCollection" },
            new() { FirstInt = 6, Tag = 53, Name = "Generic 200", Class = "Node" },
            new() { FirstInt = 7, Tag = 52, Name = "Definition C", Class = "LcRevitCollection" },
            new() { FirstInt = 8, Tag = 52, Name = "Wall 1", Class = "LcRevitData_Element" },
            new() { FirstInt = 9, Tag = 53, Name = "Floors", Class = "Node" },
            new() { FirstInt = 10, Tag = 52, Name = "Zone D", Class = "LcRevitCollection" },
            new() { FirstInt = 11, Tag = 52, Name = "Floor 1", Class = "LcRevitData_Element" },
        };

        var root = SceneTreeBuilder.Build("TestModel", records);

        var layer = Assert.Single(root.Children);
        Assert.Equal(2, layer.Children.Count);
        var firstCategory = layer.Children[0];
        Assert.Equal("Walls", firstCategory.Name);
        var familyNode = Assert.Single(firstCategory.Children);
        Assert.Equal("Basic Wall", familyNode.Name);
        var typeNode = Assert.Single(familyNode.Children);
        Assert.Equal("Generic 200", typeNode.Name);
        Assert.Equal("Wall 1", Assert.Single(typeNode.Children).Name);

        var secondCategory = layer.Children[1];
        Assert.Equal("Floors", secondCategory.Name);
        Assert.Equal("Floor 1", Assert.Single(secondCategory.Children).Name);
    }

    [Fact]
    public void Build_NonRevitClasses_CreateNodesAndReceiveGuids()
    {
        var guids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var records = new List<NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "0", Class = "LcDwgLayer" },
            new() { FirstInt = 2, Tag = 53, Name = "Model", Class = "Node" },
            new() { FirstInt = 3, Tag = 52, Name = "Category", Class = "LcDwgCollection" },
            new() { FirstInt = 4, Tag = 52, Name = "Block 1", Class = "LcOaNode" },
            new() { FirstInt = 5, Tag = 52, Name = "Block 2", Class = "LcOaNode" },
        };

        var root = SceneTreeBuilder.Build("TestModel", records, guids: guids);

        var layer = Assert.Single(root.Children);
        var category = Assert.Single(layer.Children);
        Assert.Equal("Model", category.Name);
        Assert.Equal(2, category.Children.Count);
        Assert.Equal(guids[0], category.Children[0].ElementGuid);
        Assert.Equal(guids[1], category.Children[1].ElementGuid);
    }

    [Fact]
    public void Build_RepeatedUnknownMarkers_KeepTheirAssignedLevels()
    {
        var records = new List<NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "Level 1", Class = "LcRevitLayer" },
            new() { FirstInt = 2, Tag = 53, Name = "Walls", Class = "Node" },
            new() { FirstInt = 3, Tag = 52, Name = "Kategori", Class = "LcRevitCollection" },
            new() { FirstInt = 4, Tag = 53, Name = "Basic Wall", Class = "Node" },
            new() { FirstInt = 5, Tag = 52, Name = "Aile", Class = "LcRevitCollection" },
            new() { FirstInt = 6, Tag = 53, Name = "Generic 200", Class = "Node" },
            new() { FirstInt = 7, Tag = 52, Name = "Tur", Class = "LcRevitCollection" },
            new() { FirstInt = 8, Tag = 52, Name = "Wall 1", Class = "LcRevitData_Element" },
            new() { FirstInt = 9, Tag = 53, Name = "Basic Wall 2", Class = "Node" },
            new() { FirstInt = 10, Tag = 52, Name = "Aile", Class = "LcRevitCollection" },
            new() { FirstInt = 11, Tag = 53, Name = "Generic 300", Class = "Node" },
            new() { FirstInt = 12, Tag = 52, Name = "Tur", Class = "LcRevitCollection" },
            new() { FirstInt = 13, Tag = 52, Name = "Wall 2", Class = "LcRevitData_Element" },
        };

        var root = SceneTreeBuilder.Build("TestModel", records);

        var category = Assert.Single(Assert.Single(root.Children).Children);
        Assert.Equal(2, category.Children.Count);
        var secondFamily = category.Children[1];
        Assert.Equal("Basic Wall 2", secondFamily.Name);
        Assert.Equal("Wall 2", Assert.Single(Assert.Single(secondFamily.Children).Children).Name);
    }

    [Fact]
    public void Build_MaterialRecordBetweenElements_StaysPropertyAndKeepsGuids()
    {
        var guids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var records = new List<NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "Wall 1", Class = "LcRevitData_Element" },
            new() { FirstInt = 2, Tag = 52, Name = "Material \"Concrete\", #12", Class = "LcRevitElement", ValueType = 1, Value = "155" },
            new() { FirstInt = 3, Tag = 52, Name = "Wall 2", Class = "LcRevitData_Element" },
        };

        var root = SceneTreeBuilder.Build("TestModel", records, guids: guids);

        Assert.Equal(2, root.Children.Count);
        Assert.Equal("Wall 1", root.Children[0].Name);
        Assert.Equal("Wall 2", root.Children[1].Name);
        Assert.Equal(guids[0], root.Children[0].ElementGuid);
        Assert.Equal(guids[1], root.Children[1].ElementGuid);
        Assert.Single(root.Children[0].Properties);
    }

    [Fact]
    public void Build_RepeatedFamilyMarker_StaysUnderTheSameCategory()
    {
        var records = new List<NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "Level 1", Class = "LcRevitLayer" },
            new() { FirstInt = 2, Tag = 53, Name = "Walls", Class = "Node" },
            new() { FirstInt = 3, Tag = 52, Name = "Kategorie", Class = "LcRevitCollection" },
            new() { FirstInt = 4, Tag = 53, Name = "Basic Wall", Class = "Node" },
            new() { FirstInt = 5, Tag = 52, Name = "Familie", Class = "LcRevitCollection" },
            new() { FirstInt = 6, Tag = 53, Name = "Generic 200", Class = "Node" },
            new() { FirstInt = 7, Tag = 52, Name = "Typ", Class = "LcRevitCollection" },
            new() { FirstInt = 8, Tag = 52, Name = "Wall 1", Class = "LcRevitData_Element" },
            new() { FirstInt = 9, Tag = 53, Name = "Basic Wall 2", Class = "Node" },
            new() { FirstInt = 10, Tag = 52, Name = "Familie", Class = "LcRevitCollection" },
            new() { FirstInt = 11, Tag = 53, Name = "Generic 300", Class = "Node" },
            new() { FirstInt = 12, Tag = 52, Name = "Typ", Class = "LcRevitCollection" },
            new() { FirstInt = 13, Tag = 52, Name = "Wall 2", Class = "LcRevitData_Element" },
        };

        var root = SceneTreeBuilder.Build("TestModel", records);

        var category = Assert.Single(Assert.Single(root.Children).Children);
        Assert.Equal(2, category.Children.Count);
        var secondFamily = category.Children[1];
        Assert.Equal("Basic Wall 2", secondFamily.Name);
        var secondType = Assert.Single(secondFamily.Children);
        Assert.Equal("Generic 300", secondType.Name);
        Assert.Equal("Wall 2", Assert.Single(secondType.Children).Name);
    }

    [Fact]
    public void Build_PropertyClassWithoutValue_StaysPropertyOnCurrentNode()
    {
        var records = new List<NwcPropertyRecord>
        {
            new() { FirstInt = 1, Tag = 52, Name = "Wall 1", Class = "LcRevitData_Element" },
            new() { FirstInt = 2, Tag = 52, Name = "Comments", Class = "LcOtherPropertyComments" },
        };

        var root = SceneTreeBuilder.Build("TestModel", records);

        var element = Assert.Single(root.Children);
        var property = Assert.Single(element.Properties);
        Assert.Equal("Comments", property.Name);
        Assert.Empty(element.Children);
    }

    private static List<NwcPropertyRecord> BuildHierarchy(string category, string family, string type) =>
    [
        new() { FirstInt = 1, Tag = 53, Name = "Level 1", Class = "Node" },
        new() { FirstInt = 2, Tag = 52, Name = "Layer", Class = "LcRevitLayer" },
        new() { FirstInt = 3, Tag = 53, Name = "Walls", Class = "Node" },
        new() { FirstInt = 4, Tag = 52, Name = category, Class = "LcRevitCollection" },
        new() { FirstInt = 5, Tag = 53, Name = "Basic Wall", Class = "Node" },
        new() { FirstInt = 6, Tag = 52, Name = family, Class = "LcRevitCollection" },
        new() { FirstInt = 7, Tag = 53, Name = "Generic 200", Class = "Node" },
        new() { FirstInt = 8, Tag = 52, Name = type, Class = "LcRevitCollection" },
        new() { FirstInt = 9, Tag = 52, Name = "Wall 1", Class = "LcRevitData_Element" },
    ];
}
