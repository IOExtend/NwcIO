namespace NwcIO.Model.Redline;

/// <summary>Review or approval status of a redline markup tag in Navisworks.</summary>
public enum NwcRedlineStatus
{
    New = 0,
    Active = 1,
    Approved = 2,
    Resolved = 3,
}

/// <summary>Base record for all 2D and 3D review markup annotations drawn on Navisworks viewpoints.</summary>
public abstract record NwcRedline
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RgbaColor Color { get; init; } = new(1f, 0f, 0f, 1f); // Navisworks default red
    public double Thickness { get; init; } = 2.0;
    public string? Author { get; init; }
    public DateTime? CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>A text note markup drawn at a specific coordinate on a viewpoint.</summary>
public sealed record NwcRedlineText : NwcRedline
{
    public required string Text { get; init; }
    public Vector3d Position { get; init; } = Vector3d.Zero;
    public double FontSize { get; init; } = 12.0;

    public NwcRedlineText() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NwcRedlineText(string text, Vector3d position, double fontSize = 12.0, RgbaColor? color = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Position = position;
        FontSize = fontSize;
        if (color.HasValue) Color = color.Value;
    }
}

/// <summary>A straight line markup segment, optionally with an arrow pointer.</summary>
public sealed record NwcRedlineLine : NwcRedline
{
    public Vector3d StartPoint { get; init; }
    public Vector3d EndPoint { get; init; }
    public bool HasArrow { get; init; }

    public NwcRedlineLine() { }

    public NwcRedlineLine(Vector3d start, Vector3d end, bool hasArrow = false, RgbaColor? color = null)
    {
        StartPoint = start;
        EndPoint = end;
        HasArrow = hasArrow;
        if (color.HasValue) Color = color.Value;
    }
}

/// <summary>A directional arrow markup pointing from start to end.</summary>
public sealed record NwcRedlineArrow : NwcRedline
{
    public Vector3d StartPoint { get; init; }
    public Vector3d EndPoint { get; init; }

    public NwcRedlineArrow() { }

    public NwcRedlineArrow(Vector3d start, Vector3d end, RgbaColor? color = null)
    {
        StartPoint = start;
        EndPoint = end;
        if (color.HasValue) Color = color.Value;
    }
}

/// <summary>A revision cloud markup composed of a sequence of vertices.</summary>
public sealed record NwcRedlineCloud : NwcRedline
{
    public IReadOnlyList<Vector3d> Vertices { get; init; } = Array.Empty<Vector3d>();
    public bool IsClosed { get; init; } = true;

    public NwcRedlineCloud() { }

    public NwcRedlineCloud(IEnumerable<Vector3d> vertices, bool isClosed = true, RgbaColor? color = null)
    {
        Vertices = vertices?.ToList() ?? (IReadOnlyList<Vector3d>)Array.Empty<Vector3d>();
        IsClosed = isClosed;
        if (color.HasValue) Color = color.Value;
    }
}

/// <summary>An elliptical / circular markup boundary.</summary>
public sealed record NwcRedlineEllipse : NwcRedline
{
    public Vector3d Center { get; init; }
    public double RadiusX { get; init; }
    public double RadiusY { get; init; }

    public NwcRedlineEllipse() { }

    public NwcRedlineEllipse(Vector3d center, double radiusX, double radiusY, RgbaColor? color = null)
    {
        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
        if (color.HasValue) Color = color.Value;
    }
}

/// <summary>A numbered review issue tag / pin placed at a 3D coordinate with optional comments and workflow status.</summary>
public sealed record NwcRedlineTag : NwcRedline
{
    public int TagNumber { get; init; } = 1;
    public Vector3d Position { get; init; } = Vector3d.Zero;
    public string? Comment { get; init; }
    public NwcRedlineStatus Status { get; init; } = NwcRedlineStatus.New;

    public NwcRedlineTag() { }

    public NwcRedlineTag(int tagNumber, Vector3d position, string? comment = null, NwcRedlineStatus status = NwcRedlineStatus.New)
    {
        TagNumber = tagNumber;
        Position = position;
        Comment = comment;
        Status = status;
    }
}
