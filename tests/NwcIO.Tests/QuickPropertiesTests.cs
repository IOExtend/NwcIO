using NwcIO.Model;
using NwcIO.Model.Sets;
using Xunit;

namespace NwcIO.Tests;

public sealed class QuickPropertiesTests
{
    [Fact]
    public void CreateDefault_PopulatesStandardBimProperties()
    {
        var qp = NwcQuickProperties.CreateDefault();

        Assert.NotEmpty(qp.Definitions);
        Assert.Contains(qp.Definitions, d => d.Property == "Name");
        Assert.Contains(qp.Definitions, d => d.Property == "Type");
        Assert.Contains(qp.Definitions, d => d.Property == "Layer");
        Assert.Contains(qp.Definitions, d => d.Title == "Material");
    }

    [Fact]
    public void Extract_RetrievesPropertiesFromNode()
    {
        var qp = new NwcQuickProperties();
        qp.Add("Item", "Name", "Element Name");
        qp.Add("Item", "Class", "Element Class");
        qp.Add("Dimensions", "Length", "Length");
        qp.Add("Structural", "Profile", "Profile Type");

        var node = new NwcNode
        {
            Id = 42,
            Name = "HEB 300 Column",
            Class = "StructuralColumns",
        };

        node.Properties.Add(new NwcPropertyRecord
        {
            FirstInt = 1,
            Tag = 10,
            Name = "Length",
            Class = "Dimensions",
            ValueType = 4,
            Value = "4500 mm",
        });

        node.Properties.Add(new NwcPropertyRecord
        {
            FirstInt = 1,
            Tag = 11,
            Name = "Profile",
            Class = "Structural",
            ValueType = 4,
            Value = "HEB 300",
        });

        var extracted = qp.Extract(node);

        Assert.Equal("HEB 300 Column", extracted["Element Name"]);
        Assert.Equal("StructuralColumns", extracted["Element Class"]);
        Assert.Equal("4500 mm", extracted["Length"]);
        Assert.Equal("HEB 300", extracted["Profile Type"]);
    }

    [Fact]
    public void XmlSerialization_RoundtripsDefinitions()
    {
        var qp = new NwcQuickProperties();
        qp.Add("Revit", "Comments", "User Comments");
        qp.Add("IFC", "ExportAs", "Export Type");
        qp.Add("Dimensions", "Volume", "Total Volume");

        string xml = qp.ToXml();
        Assert.Contains("category=\"Revit\"", xml, StringComparison.Ordinal);
        Assert.Contains("name=\"Comments\"", xml, StringComparison.Ordinal);
        Assert.Contains("displayname=\"User Comments\"", xml, StringComparison.Ordinal);

        var restored = NwcQuickProperties.FromXml(xml);
        Assert.Equal(3, restored.Definitions.Count);
        Assert.Equal("Revit", restored.Definitions[0].Category);
        Assert.Equal("Comments", restored.Definitions[0].Property);
        Assert.Equal("User Comments", restored.Definitions[0].DisplayName);
        Assert.Equal("IFC", restored.Definitions[1].Category);
        Assert.Equal("Export Type", restored.Definitions[1].DisplayName);
    }
}
