using NwcIO.Model;
using NwcIO.Model.Redline;
using NwcIO.Model.Sectioning;
using Xunit;

namespace NwcIO.Tests;

public class XmlViewpointSerializerTests
{
    [Fact]
    public void Serialize_Roundtrip_PreservesViewpointDetails()
    {
        var vp = new NwcSavedViewpoint
        {
            Name = "HVAC Inspection",
            Camera = new NwcCamera
            {
                Position = new Vector3d(12.0, 24.0, 6.0),
                Rotation = new Quaterniond(0, 0, 0, 1),
                FocalDistance = 15.0,
                FieldOfView = 55.0,
                AspectRatio = 1.6,
                Projection = NwcCameraProjection.Perspective,
            },
            SectionPlanes =
            [
                new NwcSectionPlane
                {
                    Alignment = NwcSectionAlignment.Top,
                    Normal = new Vector3d(0, 0, 1),
                    Distance = 4.5,
                    IsEnabled = true,
                }
            ],
            Redlines =
            [
                new NwcRedlineText
                {
                    Text = "Duct clash at L2",
                    Position = new Vector3d(10, 20, 4.5),
                    Color = new RgbaColor(1f, 0f, 0f, 1f),
                    Author = "Coordinator",
                },
                new NwcRedlineArrow
                {
                    StartPoint = new Vector3d(10, 20, 5),
                    EndPoint = new Vector3d(12, 22, 5),
                    Color = new RgbaColor(0f, 1f, 0f, 1f),
                }
            ],
        };

        string xml = NwcXmlViewpointSerializer.Serialize(new[] { vp });

        Assert.Contains("<exchange", xml);
        Assert.Contains("<viewpoint name=\"HVAC Inspection\"", xml);
        Assert.Contains("<clip_planes>", xml);
        Assert.Contains("<redlines>", xml);

        var roundtripped = NwcXmlViewpointSerializer.Deserialize(xml);
        Assert.Single(roundtripped);

        var rVp = roundtripped[0];
        Assert.Equal("HVAC Inspection", rVp.Name);
        Assert.Equal(12.0, rVp.Camera.Position.X, 3);
        Assert.Equal(24.0, rVp.Camera.Position.Y, 3);
        Assert.Equal(6.0, rVp.Camera.Position.Z, 3);
        Assert.Equal(55.0, rVp.Camera.FieldOfView, 3);

        // Section plane
        Assert.Single(rVp.SectionPlanes);
        Assert.Equal(0, rVp.SectionPlanes[0].Normal.X, 3);
        Assert.Equal(0, rVp.SectionPlanes[0].Normal.Y, 3);
        Assert.Equal(1, rVp.SectionPlanes[0].Normal.Z, 3);
        Assert.Equal(4.5, rVp.SectionPlanes[0].Distance, 3);
        Assert.True(rVp.SectionPlanes[0].IsEnabled);

        // Redlines
        Assert.Equal(2, rVp.Redlines.Count);
        var rText = Assert.IsType<NwcRedlineText>(rVp.Redlines[0]);
        Assert.Equal("Duct clash at L2", rText.Text);
        Assert.Equal("Coordinator", rText.Author);

        var rArrow = Assert.IsType<NwcRedlineArrow>(rVp.Redlines[1]);
        Assert.Equal(10.0, rArrow.StartPoint.X, 3);
        Assert.Equal(12.0, rArrow.EndPoint.X, 3);
    }

    [Fact]
    public void Serialize_EmptyList_ReturnsValidRootElement()
    {
        string xml = NwcXmlViewpointSerializer.Serialize(Array.Empty<NwcSavedViewpoint>());
        Assert.Contains("<exchange", xml);
        Assert.Contains("<viewpoints />", xml);

        var list = NwcXmlViewpointSerializer.Deserialize(xml);
        Assert.Empty(list);
    }

    [Fact]
    public void Deserialize_AllRedlineTypes_Succeeds()
    {
        var vp = new NwcSavedViewpoint
        {
            Name = "Multi Redline",
            Camera = new NwcCamera(),
            Redlines =
            [
                new NwcRedlineLine { StartPoint = new Vector3d(0, 0, 0), EndPoint = new Vector3d(1, 1, 1) },
                new NwcRedlineArrow { StartPoint = new Vector3d(1, 1, 1), EndPoint = new Vector3d(2, 2, 2) },
                new NwcRedlineEllipse { Center = new Vector3d(3, 3, 3), RadiusX = 2.0, RadiusY = 1.0 },
                new NwcRedlineCloud { Vertices = new List<Vector3d> { new(0, 0, 0), new(5, 0, 0), new(5, 5, 0) } },
                new NwcRedlineTag { TagNumber = 1, Position = new Vector3d(4, 4, 4), Comment = "Check tag" }
            ],
        };

        string xml = NwcXmlViewpointSerializer.Serialize(new[] { vp });
        var deserialized = NwcXmlViewpointSerializer.Deserialize(xml);

        Assert.Single(deserialized);
        Assert.Equal(5, deserialized[0].Redlines.Count);
        Assert.Contains(deserialized[0].Redlines, r => r is NwcRedlineLine);
        Assert.Contains(deserialized[0].Redlines, r => r is NwcRedlineArrow);
        Assert.Contains(deserialized[0].Redlines, r => r is NwcRedlineEllipse);
        Assert.Contains(deserialized[0].Redlines, r => r is NwcRedlineCloud);
        Assert.Contains(deserialized[0].Redlines, r => r is NwcRedlineTag);
    }
}
