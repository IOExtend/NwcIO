namespace NwcIO.Scene;

/// <summary>Options for <see cref="NwcSceneReader"/>.</summary>
public sealed class NwcSceneOptions
{
    /// <summary>
    /// The key of the scene model (<c>SceneModel.Key</c>), which with each element's <c>SourceId</c> forms the identity that may
    /// be stored outside the scene. Null uses the file name with its extension, for example <c>Tower.nwc</c>. Required when a
    /// document is converted from memory, where there is no file name. An empty key is invalid, and <see cref="NwcSceneReader"/>
    /// throws <see cref="ArgumentException"/> for it.
    /// </summary>
    public string? ModelKey { get; set; }

    /// <summary>The length unit of the document's coordinates and length values; when set, it replaces <see cref="NwcDocument.LengthUnit"/>.</summary>
    public NwcLengthUnit? LengthUnit { get; set; }
}
