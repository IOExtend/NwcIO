using NwcIO.Model;
using NwcIO.Model.Redline;
using Xunit;

namespace NwcIO.Tests;

public class RedlineTests
{
    [Fact]
    public void RedlineText_PropertiesAndDefaults_AreValid()
    {
        var text = new NwcRedlineText
        {
            Id = Guid.NewGuid(),
            Text = "Verify clash with MEP duct",
            Position = new Vector3d(12.5, 45.0, 3.2),
            FontSize = 14.0,
            Color = new RgbaColor(1.0f, 0.0f, 0.0f, 1.0f),
            Author = "BIM Coordinator",
        };

        Assert.Equal("Verify clash with MEP duct", text.Text);
        Assert.Equal(14.0, text.FontSize);
        Assert.Equal(new RgbaColor(1.0f, 0.0f, 0.0f, 1.0f), text.Color);
        Assert.Equal("BIM Coordinator", text.Author);
    }

    [Fact]
    public void RedlineArrow_StartAndEndPoints_WorkAsExpected()
    {
        var arrow = new NwcRedlineArrow
        {
            StartPoint = new Vector3d(0, 0, 0),
            EndPoint = new Vector3d(10, 20, 30),
            Thickness = 2.5,
            Color = new RgbaColor(0f, 1f, 0f, 1f),
        };

        Assert.Equal(new Vector3d(0, 0, 0), arrow.StartPoint);
        Assert.Equal(new Vector3d(10, 20, 30), arrow.EndPoint);
        Assert.Equal(2.5, arrow.Thickness);
    }

    [Fact]
    public void RedlineCloud_VerticesCollection_MaintainsOrder()
    {
        var cloud = new NwcRedlineCloud
        {
            Vertices = new List<Vector3d>
            {
                new(0, 0, 0),
                new(10, 0, 0),
                new(10, 10, 0),
                new(0, 10, 0),
            },
            Thickness = 1.5,
        };

        Assert.Equal(4, cloud.Vertices.Count);
        Assert.Equal(new Vector3d(10, 10, 0), cloud.Vertices[2]);
    }

    [Fact]
    public void RedlineTag_CreationAndDefaults()
    {
        var tag = new NwcRedlineTag
        {
            TagNumber = 1,
            Position = new Vector3d(5, 5, 2),
            Comment = "RFI #104 pending review",
            Status = NwcRedlineStatus.Approved,
        };

        Assert.Equal(1, tag.TagNumber);
        Assert.Equal("RFI #104 pending review", tag.Comment);
        Assert.Equal(NwcRedlineStatus.Approved, tag.Status);
    }

    [Fact]
    public void SavedViewpoint_ContainsRedlinesCollection()
    {
        var vp = new NwcSavedViewpoint
        {
            Name = "Issue Viewpoint",
            Camera = new NwcCamera(),
            Redlines =
            [
                new NwcRedlineText { Text = "Note 1", Position = new Vector3d(1, 2, 3) },
                new NwcRedlineArrow { StartPoint = new Vector3d(0, 0, 0), EndPoint = new Vector3d(1, 1, 1) },
                new NwcRedlineTag { TagNumber = 1, Comment = "Tag 1" }
            ],
        };

        Assert.Equal(3, vp.Redlines.Count);
        Assert.IsType<NwcRedlineText>(vp.Redlines[0]);
        Assert.IsType<NwcRedlineArrow>(vp.Redlines[1]);
        Assert.IsType<NwcRedlineTag>(vp.Redlines[2]);
    }
}
