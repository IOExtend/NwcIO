using IOScene;

namespace NwcIO.Scene;

/// <summary>The scene made from one Navisworks document by <see cref="NwcSceneReader"/>, what was lost on the way, and counts.</summary>
public sealed class NwcSceneResult
{
    /// <summary>The scene: one model with the document's selection tree, geometry, materials and property sets, in metres.</summary>
    public required SceneDocument Document { get; init; }

    /// <summary>What the conversion could not carry over; empty when nothing was lost.</summary>
    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>The number of elements in the scene: Revit element nodes plus leaves outside elements.</summary>
    public int ElementCount { get; init; }

    /// <summary>The number of those elements with an instance on themselves, on a part or on a twin.</summary>
    public int ElementsWithGeometry { get; init; }

    /// <summary>The number of geometries in the scene.</summary>
    public int GeometryCount { get; init; }

    /// <summary>The number of instances in the scene.</summary>
    public int InstanceCount { get; init; }

    /// <summary>The length unit used: <see cref="NwcSceneOptions.LengthUnit"/> when set, else <see cref="NwcDocument.LengthUnit"/>.</summary>
    public NwcLengthUnit LengthUnit { get; init; }

    /// <summary>Metres in one <see cref="LengthUnit"/> (0.3048 for feet); null when the unit is unknown.</summary>
    public double? MetresPerUnit { get; init; }
}
