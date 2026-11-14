namespace NwcIO.Model;

/// <summary>
/// A hierarchical item in the Navisworks scene graph (model item, layer, collection,
/// category, family, type or element instance).
/// </summary>
public sealed class NwcNode
{
    public required int Id { get; init; }

    public required string Name { get; set; }

    public required string Class { get; set; }

    public int Tag { get; set; }

    public string? Value { get; set; }

    /// <summary>Parent node in the scene tree, or null for root nodes.</summary>
    public NwcNode? Parent { get; set; }

    /// <summary>Child nodes nested under this item.</summary>
    public List<NwcNode> Children { get; } = [];

    /// <summary>Properties attached directly to this node.</summary>
    public List<NwcPropertyRecord> Properties { get; } = [];

    /// <summary>Element unique identifier (GUID) if assigned.</summary>
    public Guid? ElementGuid { get; set; }

    /// <summary>True on a Revit element node: the first node of a model file read through its DbCache directory that carries an element index; its <see cref="ElementGuid"/> is the element's export GUID.</summary>
    public bool IsElement { get; set; }

    /// <summary>The element occurrence this node belongs to: itself when <see cref="IsElement"/>, else its parent's <see cref="Element"/>, or for the second node of a sibling pair that repeats one element the first node of the pair; null outside elements.</summary>
    public NwcNode? Element { get; set; }

    /// <summary>3D spatial transformation (pose and bounding box) if present.</summary>
    public NwcTransform? Transform { get; set; }

    /// <summary>Material assigned to this node.</summary>
    public NwcMaterial? Material { get; set; }

    /// <summary>The first of <see cref="Meshes"/> for Navisworks model files read through their DbCache directory (files written by <c>NwcWriter</c> and other containers leave <see cref="Meshes"/> empty); null when the node has no geometry.</summary>
    public NwcMesh? Mesh { get; set; }

    /// <summary>Every mesh instance placed at this node; <see cref="Mesh"/> is the first of them. Only Navisworks model files read through their DbCache directory fill it (files written by <c>NwcWriter</c> and other containers leave it empty).</summary>
    public List<NwcMesh> Meshes { get; } = [];

    public override string ToString() => $"{Name} ({Class}) [Children: {Children.Count}, Props: {Properties.Count}]";
}
