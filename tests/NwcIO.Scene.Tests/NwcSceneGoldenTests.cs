using IOScene;
using NwcIO;
using NwcIO.Model;
using NwcIO.Scene;
using NwcIO.Tests;
using Xunit;
using Vector3d = IOScene.Vector3d;
using RgbaColor = IOScene.RgbaColor;
using BoundingBox3d = IOScene.BoundingBox3d;

namespace NwcIO.Scene.Tests;

public class NwcSceneGoldenTests
{
    // The golden identity of NwcIO.Scene for RevitSample: the model key, SourceId and IfcGuid of every element that has a SourceId
    // (document order), and every keyed set with its keyed values (Key, Kind, Unit or -, Text). Changing what NwcIO.Scene writes
    // here is a breaking release of NwcIO.Scene.
    private static readonly string[] Golden =
    [
        "key Tower.nwc",
        "element 7b0c5a4e-3f2d-4b1a-9c8e-6d5f4e3ef9b1 1x35fEForB6foERLzEFlcn",
        "element 11111111-2222-4333-8444-555555555555 3Ax9$kQwz0Bf8cYp2LmN4r",
        "element 0f1e2d3c-4b5a-4968-8778-695a4b3c2d1e 0F7YqyIrf9Q8TuQLfBF2qU",
        "element a0b1c2d3-e4f5-4a6b-9c7d-8e9fa0b1c2d3 2WiSBJvFLAQvnzZf_WiSBJ",
        "set nwc:LcRevitId",
        "value nwc:LcRevitId.LcRevitId Text - 311",
        "set nwc:LcRevitData_Element",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementCategory Text - Walls",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementCategoryId Text - -2000011",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementId Text - 1001",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1000001 Text - 10 m²",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1000002 Text - 2 m³",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1000003 Text - 90 °",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1000004 Text - 0.25",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1000005 Text - 1",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1000006 Text - Exterior",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1000007 Text - ID-9",
        "set nwc:LcRevitData_Type",
        "value nwc:LcRevitData_Type.LcRevitPropertyElementName Text - Generic 200",
        "value nwc:LcRevitData_Type.lcldrevit_parameter_-1000010 Text - 200 mm",
        "set nwc:LcRevitId",
        "value nwc:LcRevitId.LcRevitId Text - 1001",
        "set nwc:LcRevitMaterialProperties",
        "value nwc:LcRevitMaterialProperties.revit_Color Text - 128, 128, 128",
        "set nwc:LcRevitData_Element",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementId Text - 1002",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementCategoryId Text - -2000011",
        "value nwc:LcRevitData_Element.lcldrevit_parameter_-1019000 Text - 3Ax9$kQwz0Bf8cYp2LmN4r",
        "set nwc:LcRevitId",
        "value nwc:LcRevitId.LcRevitId Text - 1002",
        "set nwc:LcRevitData_Element",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementId Text - 1003",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementCategoryId Text - -2000023",
        "set nwc:LcRevitId",
        "value nwc:LcRevitId.LcRevitId Text - 1003",
        "set nwc:LcRevitId",
        "value nwc:LcRevitId.LcRevitId Text - 1003",
        "set nwc:LcRevitId",
        "value nwc:LcRevitId.LcRevitId Text - 999",
        "set nwc:LcRevitData_Element",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementId Text - 1004",
        "value nwc:LcRevitData_Element.LcRevitPropertyElementCategoryId Text - -2000051",
        "set nwc:LcRevitId",
        "value nwc:LcRevitId.LcRevitId Text - 1004",
    ];

    [Fact]
    public void Read_RevitSample_MatchesTheGoldenIdentity()
    {
        var doc = Valid(NwcSceneReader.Read(NwcDocument.Open(new MemoryStream(RevitSample.Build())), new() { ModelKey = "Tower.nwc" })).Document;
        Assert.Equal(Golden,
            new[] { "key " + Assert.Single(doc.Models).Key }
            .Concat(doc.Elements.Where(e => e.SourceId != null).Select(e => $"element {e.SourceId} {e.IfcGuid ?? "-"}"))
            .Concat(doc.PropertySets.Where(s => s.Key != null).SelectMany(s => s.Properties.Where(p => p.Key != null)
                .Select(p => $"value {p.Key} {p.Value.Kind} {p.Value.Unit ?? "-"} {p.Value.Text}").Prepend("set " + s.Key))));
    }

    [Fact]
    public void Read_RevitSample_SurvivesSceneJson()
    {
        var doc = Valid(NwcSceneReader.Read(NwcDocument.Open(new MemoryStream(RevitSample.Build())), new() { ModelKey = "Tower.nwc" })).Document;
        var again = SceneJson.Read(SceneJson.Write(doc));
        Assert.Empty(again.Validate());
        Assert.Equal(SceneJson.Write(doc), SceneJson.Write(again));
    }

    [Fact]
    public void Read_HostileStrings_SurviveSceneJson()
    {
        const string hostile = "q\"uote\\ \u0001\u001f \U0001F600 Стена </script>";
        var tab = new NwcPropertyTab { Name = hostile, InternalName = "LcRevitData_Element",
            Properties = [new NwcPropertyRecord { FirstInt = 1, Tag = 52, Name = hostile, Class = hostile, ValueType = 4, Value = hostile, InternalName = "LcRevitPropertyElementName" }] };
        var root = new NwcNode { Id = 0, Name = hostile, Class = "Model" };
        var leaf = new NwcNode { Id = 1, Name = hostile, Class = "Leaf", Tag = 22, Parent = root };
        leaf.PropertyTabs.Add(tab); root.Children.Add(leaf);
        var doc = Valid(NwcSceneReader.Read(NwcDocument.Create(rootNode: root), new() { ModelKey = hostile })).Document;
        var again = SceneJson.Read(SceneJson.Write(doc));
        Assert.Empty(again.Validate());
        Assert.Equal(hostile, again.Elements[1].Name);
        Assert.Equal(hostile, again.PropertySets[0].Properties[0].Value.Text);
    }

    // Every converted fixture must pass Validate() without errors or warnings.
    private static NwcSceneResult Valid(NwcSceneResult result)
    {
        Assert.Empty(result.Document.Validate());
        return result;
    }
}
