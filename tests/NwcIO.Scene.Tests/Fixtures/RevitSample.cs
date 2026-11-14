using NwcIO.Model;
using NwcIO.Tests;

namespace NwcIO.Scene.Tests;

/// <summary>The golden synthetic Revit model; preorder ids of its nodes in comments.</summary>
internal static class RevitSample
{
    public static readonly Guid G1 = Guid.Parse("7b0c5a4e-3f2d-4b1a-9c8e-6d5f4e3ef9b1");   // export GUID of UniqueId 7b0c5a4e-3f2d-4b1a-9c8e-6d5f4e3a2b10-0004d2a1
    public static readonly Guid G2 = Guid.Parse("11111111-2222-4333-8444-555555555555");
    public static readonly Guid G3 = Guid.Parse("0f1e2d3c-4b5a-4968-8778-695a4b3c2d1e");
    public static readonly Guid G4 = Guid.Parse("a0b1c2d3-e4f5-4a6b-9c7d-8e9fa0b1c2d3");

    public static byte[] Build(string loader = "LcNwcLoaderPlugin:lcldrevit", int doorCategoryId = -2000023, int lineCategoryId = -2000051,
        IReadOnlyList<(string Name, byte[] Data)>? extraStreams = null)
    {
        var level = new Tab(null, "Level", "LcRevitData_Parameter",
            new Prop("Name", "LcRevitPropertyElementName", 4, "Level 1"), new Prop("Category Id", "LcRevitPropertyElementCategoryId", 2, -2000240),
            new Prop("Id", "LcRevitPropertyElementId", 2, 311), new Prop("Elevation", "lcldrevit_parameter_-1007102", 6, 3000 / 304.8));
        var type = new Tab(null, "Type", "LcRevitData_Type",
            new Prop("Name", "LcRevitPropertyElementName", 4, "Generic 200"), new Prop("Width", "lcldrevit_parameter_-1000010", 6, 200 / 304.8));
        var wallA = new Tab(null, "Element", "LcRevitData_Element",
            new Prop("Category", "LcRevitPropertyElementCategory", 4, "Walls"), new Prop("Category Id", "LcRevitPropertyElementCategoryId", 2, -2000011),
            new Prop("Id", "LcRevitPropertyElementId", 2, 1001), new Prop("Area", "lcldrevit_parameter_-1000001", 10, 10 / 0.09290304),
            new Prop("Volume", "lcldrevit_parameter_-1000002", 11, 2 / 0.0283168466), new Prop("Angle", "lcldrevit_parameter_-1000003", 7, Math.PI / 2),
            new Prop("Comment", "lcldrevit_parameter_Comment_PG_DATA", 4, "note"), new Prop("Factor", "lcldrevit_parameter_-1000004", 1, 0.25),
            new Prop("Structural", "lcldrevit_parameter_-1000005", 3, 1), new Prop("Function", "lcldrevit_parameter_-1000006", 8, ("Exterior", "lcldrevit_enum_exterior")),
            new Prop("Ident", "lcldrevit_parameter_-1000007", 9, "ID-9"), new Prop("Empty", "lcldrevit_parameter_-1000008", 0),
            new Prop("Date", "lcldrevit_parameter_-1000009", 5, 1.0));
        var concrete = new Tab("Concrete", "Material", "LcRevitMaterialProperties",
            new Prop("Color", "revit_Color", 4, "128, 128, 128"), new Prop("Note", "revit_mat_cust_prop_Note", 4, "x"));
        var wallB = new Tab(null, "Element", "LcRevitData_Element",
            new Prop("Id", "LcRevitPropertyElementId", 2, 1002), new Prop("Category Id", "LcRevitPropertyElementCategoryId", 2, -2000011),
            new Prop("IfcGUID", "lcldrevit_parameter_-1019000", 4, "3Ax9$kQwz0Bf8cYp2LmN4r"));
        var door = new Tab(null, "Element", "LcRevitData_Element",
            new Prop("Id", "LcRevitPropertyElementId", 2, 1003), new Prop("Category Id", "LcRevitPropertyElementCategoryId", 2, doorCategoryId));
        var line = new Tab(null, "Element", "LcRevitData_Element",
            new Prop("Id", "LcRevitPropertyElementId", 2, 1004), new Prop("Category Id", "LcRevitPropertyElementCategoryId", 2, lineCategoryId));

        var (hierarchy, props) = SyntheticTree.Write(2,
            new TreeNode("Level 1", "LcRevitLayer", Slots: [new PId(311)], Children: [                                              // 1
                new("Walls", "LcRevitCollection", Children: [                                                                          // 2
                    new("Basic Wall", "LcRevitCollection", Children: [                                                                 // 3
                        new("Generic 200", "LcRevitCollection", Children: [                                                            // 4
                            new("Wall A", "LcRevitComposite", X: 1, Slots: [new PTab(wallA), new HTab(level), new HTab(type), new PId(1001)], Children: [   // 5
                                new("Solid", "LcRevitSolid", Slots: [new HMat("Concrete"), new PTab(concrete)])]),                                    // 6
                            new("Wall B", "LcRevitInstance", X: 2, Slots: [new PTab(wallB), new HTab(level), new HTab(type), new PId(1002)]),          // 7
                            new("Door", "LcRevitComposite", X: 3, Slots: [new HTab(door), new PId(1003)], Children: [                                 // 8
                                new("Panel", "LcRevitSolid", Slots: [new HMat("Glass")])]),                                                            // 9
                            new("Door", "LcRevitInstance", X: 3, Slots: [new HTab(door), new PId(1003)])])])])]),                                     // 10
            new TreeNode("No level", "LcRevitLayer", Slots: [new PId(999)], Children: [                                               // 11: id names no level
                new("Lines", "LcRevitCollection", Children: [                                                                          // 12
                    new("Line 1", "LcRevitInstance", X: 5, Slots: [new PTab(line), new PId(1004)])])]));                               // 13: the pair 8/10 takes table entries 3 and 4

        var white = new Look(1, 1, 1, 1e-5f, 0);
        var fragments = SyntheticFragments.Build(
            new Frag(6, new(1, 2, 3), 1, 2, 4, Rec: 1, Look: new Look(128 / 255f, 128 / 255f, 128 / 255f, 0.5f, 0)),
            new Frag(7, new(10, 0, 0), -1, 2, 4, SharesGeometryOf: 0),
            new Frag(9, Vector3d.Zero, 1, 2, 4, Rec: 1, Look: new Look(0, 0, 1, 1, 0.5f)),
            new Frag(10, new(0, 0, 3), 1, 1, 4, Rec: 2, MaxX: 10, MaxY: 0, Look: white),
            new Frag(13, Vector3d.Zero, 1, 2, 4, Rec: 3, SharesLookOf: 3));
        return SyntheticNwcFile.Build(hierarchy, fragments, [[.. FormatVectors.R1, .. FormatVectors.R6, .. FormatVectors.R7]], [120, 104, 136],
            extraStreams: [("LcOpGuidStore", SyntheticNwcFile.GuidStore(G1, G2, G3, G3, G4)), ("LcOpDBCache", SyntheticNwcFile.PartitionHeader("Tower.rvt", 1767225600, loader)), .. extraStreams ?? []],
            partitionProps: props);
    }
}
