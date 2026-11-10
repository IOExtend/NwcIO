using NwcIO.Model;
using NwcIO.Model.Clash;
using NwcIO.Model.Grids;
using Xunit;

namespace NwcIO.Tests;

public sealed class GridSystemTests
{
    [Fact]
    public void GridLine_CalculatesDistanceAndIntersection()
    {
        // Line A along Y-axis at X = 0
        var lineA = new NwcGridLine("A", new Vector3d(0, -10, 0), new Vector3d(0, 10, 0));
        // Line 1 along X-axis at Y = 0
        var line1 = new NwcGridLine("1", new Vector3d(-10, 0, 0), new Vector3d(10, 0, 0));

        Assert.Equal(20.0, lineA.Length, 3);
        Assert.Equal(20.0, line1.Length, 3);

        double distToA = lineA.DistanceToPoint2D(new Vector3d(2.5, 5, 0));
        Assert.Equal(2.5, distToA, 3);

        bool intersects = lineA.Intersect2D(line1, out var inter);
        Assert.True(intersects);
        Assert.Equal(0.0, inter.X, 3);
        Assert.Equal(0.0, inter.Y, 3);
    }

    [Fact]
    public void GridSystem_FindsNearestAndFormatsLocation()
    {
        var grids = new NwcGridSystem("Building Grids");
        grids.AddLine("A", new Vector3d(0, -20, 0), new Vector3d(0, 20, 0));
        grids.AddLine("B", new Vector3d(6, -20, 0), new Vector3d(6, 20, 0));
        grids.AddLine("1", new Vector3d(-10, 0, 0), new Vector3d(20, 0, 0));
        grids.AddLine("2", new Vector3d(-10, 8, 0), new Vector3d(20, 8, 0));

        var inters = grids.GetIntersections();
        Assert.Equal(4, inters.Count); // A/1, A/2, B/1, B/2

        // Exact snap at (0, 0)
        string locExact = grids.FormatLocation(new Vector3d(0, 0, 0));
        Assert.Equal("A / 1", locExact);

        // Near (0, 0) with small offset
        string locOffset = grids.FormatLocation(new Vector3d(0.5, -0.2, 0));
        Assert.Contains("A (+0.50m)", locOffset);
        Assert.Contains("1 (-0.20m)", locOffset);

        // Nearest intersection
        var nearestInter = grids.FindNearestIntersection(new Vector3d(5.8, 8.1, 0), out double dInter);
        Assert.NotNull(nearestInter);
        Assert.Equal("B / 2", nearestInter.DisplayName);
        Assert.True(dInter < 0.5);
    }

    [Fact]
    public void AssignGridLocations_PopulatesClashTestAndGroups()
    {
        var grids = new NwcGridSystem("Test Grids");
        grids.AddLine("A", new Vector3d(0, -10, 0), new Vector3d(0, 10, 0));
        grids.AddLine("B", new Vector3d(10, -10, 0), new Vector3d(10, 10, 0));
        grids.AddLine("1", new Vector3d(-5, 0, 0), new Vector3d(15, 0, 0));

        var test = new NwcClashTest("HVAC vs Structure");
        var c1 = new NwcClashResult("Clash1", new NwcClashObject("Pipe1"), new NwcClashObject("Beam1"), new Vector3d(0, 0, 3));
        var c2 = new NwcClashResult("Clash2", new NwcClashObject("Pipe2"), new NwcClashObject("Beam2"), new Vector3d(10, 0, 3));
        var c3 = new NwcClashResult("Clash3", new NwcClashObject("Pipe3"), new NwcClashObject("Beam3"), new Vector3d(0, 0, 2.5));

        test.Results.Add(c1);
        test.Results.Add(c2);
        test.Results.Add(c3);

        int assigned = grids.AssignGridLocations(test);
        Assert.Equal(3, assigned);

        Assert.Equal("A / 1", c1.GridLocation);
        Assert.Equal("B / 1", c2.GridLocation);
        Assert.Equal("A / 1", c3.GridLocation);

        // Test grouping by grid location
        int groupsCreated = NwcClashGrouper.GroupByGrid(test, groupPrefix: "GridArea");
        Assert.Equal(1, groupsCreated); // A/1 has 2 clashes, B/1 has 1 (min size 2)
        Assert.Single(test.Groups);
        Assert.Equal("GridArea: A / 1", test.Groups[0].Name);
        Assert.Equal(2, test.Groups[0].Clashes.Count);
        Assert.Single(test.Results); // c2 remains ungrouped
    }

    [Fact]
    public void GridSystem_XmlRoundtrip_PreservesData()
    {
        var original = new NwcGridSystem("Hospital Grid Project");
        original.AddLine("X1", new Vector3d(0, 0, 0), new Vector3d(100, 0, 0), elevation: 3.5, systemName: "Tower A");
        original.AddLine("Y1", new Vector3d(0, 0, 0), new Vector3d(0, 80, 0), elevation: 3.5, systemName: "Tower A");

        using var ms = new MemoryStream();
        original.SaveXml(ms);
        ms.Position = 0;

        var loaded = NwcGridSystem.ReadXml(ms);
        Assert.Equal("Hospital Grid Project", loaded.Name);
        Assert.Equal(2, loaded.Lines.Count);

        var l1 = loaded.Lines.First(l => l.Name == "X1");
        Assert.Equal(0.0, l1.Start.X, 3);
        Assert.Equal(100.0, l1.End.X, 3);
        Assert.Equal(3.5, l1.Elevation, 3);
        Assert.Equal("Tower A", l1.SystemName);
    }
}
