using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;
using NwcIO.Model.Sets;
using Xunit;

namespace NwcIO.Tests;

public class SelectionSetTests
{
    private static NwcDocument CreateSampleDocumentWithTree()
    {
        var root = new NwcNode { Id = 1, Name = "RootModel", Class = "LcOaModel" };
        var arch = new NwcNode { Id = 2, Name = "Architecture", Class = "LcOaLayer", Parent = root };
        var wall1 = new NwcNode { Id = 10, Name = "Basic Wall: Generic - 200mm", Class = "LcRevitData_Element", Parent = arch };
        var wall2 = new NwcNode { Id = 11, Name = "Curtain Wall: Storefront", Class = "LcRevitData_Element", Parent = arch };
        var col1 = new NwcNode { Id = 20, Name = "M_Concrete-Round-Column: 450mm", Class = "LcRevitData_Element", Parent = arch };

        arch.Children.Add(wall1);
        arch.Children.Add(wall2);
        arch.Children.Add(col1);
        root.Children.Add(arch);

        return NwcDocument.Create(rootNode: root);
    }

    [Fact]
    public void SelectionSet_ResolvesNodesByIdAndPath()
    {
        var doc = CreateSampleDocumentWithTree();
        var set = new NwcSelectionSet("SelectedWalls", [10, 11]);

        var resolved = set.ResolveNodes(doc);

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, n => n.Id == 10);
        Assert.Contains(resolved, n => n.Id == 11);
    }

    [Fact]
    public void SearchSet_EvaluatesDynamicConditions()
    {
        var doc = CreateSampleDocumentWithTree();
        var searchSet = new NwcSearchSet("All Columns",
        [
            new NwcSearchCondition("Item", "Name", NwcSearchOperator.Contains, "Column")
        ]);

        var matching = searchSet.Evaluate(doc);

        var col = Assert.Single(matching);
        Assert.Equal(20, col.Id);
        Assert.Contains("Column", col.Name);
    }

    [Fact]
    public void SearchSet_WildcardOperatorMatches()
    {
        var doc = CreateSampleDocumentWithTree();
        var searchSet = new NwcSearchSet("Walls Wildcard",
        [
            new NwcSearchCondition("Item", "Name", NwcSearchOperator.Wildcard, "*Wall*")
        ]);

        var matching = searchSet.Evaluate(doc);

        Assert.Equal(2, matching.Count);
        Assert.Contains(matching, n => n.Id == 10);
        Assert.Contains(matching, n => n.Id == 11);
    }

    [Theory]
    [InlineData("Name", "Wall")]
    [InlineData("Class", "LcRevitData_Element")]
    [InlineData("Type", "LcRevitData_Element")]
    [InlineData("Id", "7")]
    [InlineData("Guid", "11111111-1111-1111-1111-111111111111")]
    public void SearchCondition_ItemBuiltInsAndItemRecordsAreCandidates(string property, string value)
    {
        var node = new NwcNode
        {
            Id = 7,
            Name = "Wall",
            Class = "LcRevitData_Element",
            ElementGuid = Guid.Parse("11111111-1111-1111-1111-111111111111")
        };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 52, Class = "Element", Name = property, Value = "456" });
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 52, Class = "Item", Name = property, Value = "123" });

        Assert.True(new NwcSearchCondition("item", property, NwcSearchOperator.Equals, "123").Evaluate(node));
        Assert.False(new NwcSearchCondition("item", property, NwcSearchOperator.NotEquals, value).Evaluate(node));
        Assert.True(new NwcSearchCondition("item", property, NwcSearchOperator.Equals, value).Evaluate(node));
        Assert.False(new NwcSearchCondition("item", property, NwcSearchOperator.NotEquals, "123").Evaluate(node));
        Assert.False(new NwcSearchCondition("item", property, NwcSearchOperator.Equals, "456").Evaluate(node));
        Assert.True(new NwcSearchCondition("item", property, NwcSearchOperator.NotEquals, "456").Evaluate(node));
    }

    [Theory]
    [InlineData("Type", NwcSearchOperator.Contains, "HVAC")]
    [InlineData("Custom", NwcSearchOperator.Equals, "HVAC Duct")]
    [InlineData("Guid", NwcSearchOperator.Equals, "HVAC Duct")]
    public void SearchCondition_ItemRecordCanSupplyMatchingValue(string property, NwcSearchOperator op, string value)
    {
        var node = new NwcNode { Id = 7, Name = "Wall", Class = "LcRevitData_Element" };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 52, Class = "iTeM", Name = property, Value = "HVAC Duct" });

        Assert.True(new NwcSearchCondition("Item", property, op, value).Evaluate(node));
        Assert.False(new NwcSearchCondition("Item", property, NwcSearchOperator.NotEquals, "HVAC Duct").Evaluate(node));
        Assert.True(new NwcSearchCondition("Item", property, NwcSearchOperator.NotEquals, "Other").Evaluate(node));
    }

    [Theory]
    [InlineData("Item", "item")]
    [InlineData("Element", "element")]
    public void SearchCondition_ExplicitCategoryMatchesExactlyIgnoringCase(string category, string recordClass)
    {
        var node = new NwcNode { Id = 7, Name = "Wall", Class = "LcRevitData_Element" };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 52, Class = category + " ID", Name = "Value", Value = "123" });
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 52, Class = recordClass, Name = "Value", Value = "999" });

        Assert.False(new NwcSearchCondition(category, "Value", NwcSearchOperator.Equals, "123").Evaluate(node));
        Assert.True(new NwcSearchCondition(category, "value", NwcSearchOperator.Equals, "999").Evaluate(node));
        Assert.True(new NwcSearchCondition(category, "Value", NwcSearchOperator.NotEquals, "123").Evaluate(node));
        Assert.False(new NwcSearchCondition(category, "Value", NwcSearchOperator.NotEquals, "999").Evaluate(node));
    }

    [Theory]
    [InlineData("Element", false)]
    [InlineData("Element", true)]
    [InlineData("Item", false)]
    [InlineData("Item", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    public void SearchCondition_NotEqualsRejectsAnyEqualCandidate(string category, bool reverse)
    {
        var node = new NwcNode { Id = 7, Name = "Wall", Class = "LcRevitData_Element" };
        var recordClass = string.IsNullOrEmpty(category) ? "Element" : category;
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 52, Class = recordClass, Name = "Value", Value = "999" });
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 52, Class = recordClass, Name = "Value", Value = "123" });
        if (reverse)
        {
            node.Properties.Reverse();
        }

        Assert.False(new NwcSearchCondition(category, "Value", NwcSearchOperator.NotEquals, "123").Evaluate(node));
        Assert.True(new NwcSearchCondition(category, "Value", NwcSearchOperator.NotEquals, "456").Evaluate(node));
        Assert.True(new NwcSearchCondition(category, "Value", NwcSearchOperator.Equals, "123").Evaluate(node));
    }

    [Theory]
    [InlineData("nonnumeric")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e999")]
    public void SearchCondition_IdNotEqualsRejectsInvalidNumericTarget(string value)
    {
        var node = new NwcNode { Id = 7, Name = "Wall", Class = "LcRevitData_Element" };

        Assert.False(new NwcSearchCondition("Item", "Id", NwcSearchOperator.NotEquals, value).Evaluate(node));
    }

    [Theory]
    [InlineData("7", false)]
    [InlineData("8", true)]
    public void SearchCondition_IdNotEqualsComparesValidNumericTarget(string value, bool expected)
    {
        var node = new NwcNode { Id = 7, Name = "Wall", Class = "LcRevitData_Element" };

        Assert.Equal(expected, new NwcSearchCondition("Item", "Id", NwcSearchOperator.NotEquals, value).Evaluate(node));
    }

    [Theory]
    [InlineData("Element", "Missing")]
    [InlineData("Element", "NullValue")]
    [InlineData("Item", "Guid")]
    [InlineData("Item", "Missing")]
    public void SearchCondition_NotEqualsRequiresAnExistingValue(string category, string property)
    {
        var node = new NwcNode { Id = 7, Name = "Wall", Class = "LcRevitData_Element" };
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 1, Tag = 52, Class = "Element", Name = "NullValue" });
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 2, Tag = 52, Class = "Other", Name = "Missing", Value = "123" });
        node.Properties.Add(new NwcPropertyRecord { FirstInt = 3, Tag = 52, Class = "Item", Name = "Guid" });

        Assert.False(new NwcSearchCondition(category, property, NwcSearchOperator.NotEquals, "456").Evaluate(node));
    }

    [Fact]
    public void SelectionSets_RoundtripThroughNavisworksXml()
    {
        var folder = new NwcSelectionSetFolder("Structural");
        var explicitSet = new NwcSelectionSet("Piles", [101, 102, 103]);
        var searchSet = new NwcSearchSet("Concrete Slabs",
        [
            new NwcSearchCondition("Item", "Name", NwcSearchOperator.StartsWith, "Floor"),
            new NwcSearchCondition("Item", "Class", NwcSearchOperator.Equals, "LcRevitData_Element")
        ]);

        folder.Children.Add(explicitSet);
        folder.Children.Add(searchSet);

        using var ms = new MemoryStream();
        NwcXmlSetSerializer.Write([folder], ms);
        ms.Position = 0;

        var loaded = NwcXmlSetSerializer.Read(ms);
        var loadedFolder = Assert.IsType<NwcSelectionSetFolder>(Assert.Single(loaded));
        Assert.Equal("Structural", loadedFolder.Name);
        Assert.Equal(2, loadedFolder.Children.Count);

        var loadedExplicit = Assert.IsType<NwcSelectionSet>(loadedFolder.Children[0]);
        Assert.Equal("Piles", loadedExplicit.Name);
        Assert.Equal(3, loadedExplicit.NodeIds.Count);

        var loadedSearch = Assert.IsType<NwcSearchSet>(loadedFolder.Children[1]);
        Assert.Equal("Concrete Slabs", loadedSearch.Name);
        Assert.Equal(2, loadedSearch.Conditions.Count);
        Assert.Equal(NwcSearchOperator.StartsWith, loadedSearch.Conditions[0].Operator);
    }

    [Fact]
    public void SelectionSets_RoundtripThroughNwcFile()
    {
        var explicitSet = new NwcSelectionSet("MySelection", [50, 51]);
        var searchSet = new NwcSearchSet("MySearch", [new NwcSearchCondition("Item", "Name", NwcSearchOperator.Contains, "Beam")]);

        var doc = new NwcDocumentBuilder()
            .SetFileInfo("SetsModel.rvt")
            .AddSelectionSet(explicitSet)
            .AddSelectionSet(searchSet)
            .Build();

        using var ms = new MemoryStream();
        doc.Save(ms);
        ms.Position = 0;

        var reopened = NwcDocument.Open(ms);
        Assert.Equal(2, reopened.SelectionSets.Count);

        var s1 = Assert.IsType<NwcSelectionSet>(reopened.SelectionSets[0]);
        Assert.Equal("MySelection", s1.Name);
        Assert.Equal([50, 51], s1.NodeIds);

        var s2 = Assert.IsType<NwcSearchSet>(reopened.SelectionSets[1]);
        Assert.Equal("MySearch", s2.Name);
        Assert.Single(s2.Conditions);
        Assert.Equal("Beam", s2.Conditions[0].Value);
    }

    [Fact]
    public void SearchSet_EveryOperatorRoundtripsThroughNavisworksXml()
    {
        foreach (var op in Enum.GetValues<NwcSearchOperator>())
        {
            var searchSet = new NwcSearchSet("S", [new NwcSearchCondition("Item", "Name", op, "Duct*")]);

            using var ms = new MemoryStream();
            NwcXmlSetSerializer.Write([searchSet], ms);
            ms.Position = 0;

            var loaded = Assert.IsType<NwcSearchSet>(Assert.Single(NwcXmlSetSerializer.Read(ms)));
            var back = Assert.Single(loaded.Conditions).Operator;
            Assert.Equal(op, back);
        }
    }

    [Fact]
    public void SearchSet_ReadsProfilerStyleInclusiveOperatorSpelling()
    {
        var searchSet = new NwcSearchSet("S", [new NwcSearchCondition("Item", "Name", NwcSearchOperator.LessThanOrEqual, "5")]);

        using var ms = new MemoryStream();
        NwcXmlSetSerializer.Write([searchSet], ms);
        ms.Position = 0;
        string xml;
        using (var reader = new StreamReader(ms))
        {
            xml = reader.ReadToEnd();
        }

        // The appearance profiler serializer writes "less_equal"; both spellings must be accepted.
        Assert.Contains("less_or_equal", xml);
        using var alternate = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml.Replace("less_or_equal", "less_equal")));
        var loaded = Assert.IsType<NwcSearchSet>(Assert.Single(NwcXmlSetSerializer.Read(alternate)));
        Assert.Equal(NwcSearchOperator.LessThanOrEqual, Assert.Single(loaded.Conditions).Operator);
    }

    [Fact]
    public void SearchSet_UnknownOperator_ThrowsInsteadOfFallingBackToEquals()
    {
        var searchSet = new NwcSearchSet("S", [new NwcSearchCondition("Item", "Name", NwcSearchOperator.Equals, "x")]);

        using var ms = new MemoryStream();
        NwcXmlSetSerializer.Write([searchSet], ms);
        ms.Position = 0;
        string xml;
        using (var reader = new StreamReader(ms))
        {
            xml = reader.ReadToEnd();
        }

        using var invalid = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml.Replace("equals", "regex")));
        Assert.Throws<FormatException>(() => NwcXmlSetSerializer.Read(invalid));
    }

    [Fact]
    public void Open_UnknownOperatorInRawSetsStream_ThrowsInsteadOfReturningZeroSets()
    {
        // A raw XML sets stream (no [100][105] binary header) holding one valid set
        // plus a condition with an operator this library does not know.
        using var xmlMs = new MemoryStream();
        NwcXmlSetSerializer.Write(
        [
            new NwcSelectionSet("Piles", [1, 2]),
            new NwcSearchSet("S", [new NwcSearchCondition("Item", "Name", NwcSearchOperator.Equals, "x")]),
        ], xmlMs);
        string xml = Encoding.UTF8.GetString(xmlMs.ToArray());
        byte[] raw = Encoding.UTF8.GetBytes(xml.Replace("equals", "bogus_operator"));

        using var file = new MemoryStream();
        NwcContainerWriter.Write(file, [raw]);
        file.Position = 0;

        Assert.Throws<FormatException>(() => NwcDocument.Open(file));
    }
}
