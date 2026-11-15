namespace NwcIO.Model;

/// <summary>
/// A hyperlink / URL reference attached to a scene graph node (<see cref="NwcNode"/>),
/// corresponding to Autodesk Navisworks <c>LcOaURLAttribute</c> (class 79) and <c>LcOaURLDef</c>.
/// </summary>
public sealed class NwcHyperlink
{
    /// <summary>Display name or label of the hyperlink.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Target URL, URI, web link or file path.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional category name (e.g. "Hyperlink" or custom category).</summary>
    public string? Category { get; set; }

    /// <summary>3D attachment points / anchor positions in world or model space, if any.</summary>
    public List<Vector3d> AttachmentPoints { get; } = [];

    public override string ToString() => string.IsNullOrEmpty(Name) ? Url : $"{Name}: {Url}";
}
