using NwcIO.Internal.Export;

namespace NwcIO.Model;

/// <summary>
/// Represents a spatial clash (geometric intersection or overlap) detected between elements
/// from different models within a federated BIM assembly.
/// </summary>
/// <param name="ModelA">First contributing model.</param>
/// <param name="MeshA">Clashing mesh from the first model.</param>
/// <param name="ModelB">Second contributing model.</param>
/// <param name="MeshB">Clashing mesh from the second model.</param>
/// <param name="IntersectionBox">Axis-aligned bounding box of the geometric overlap volume.</param>
public sealed record NwcClash(
    NwcDocument ModelA,
    NwcMesh MeshA,
    NwcDocument ModelB,
    NwcMesh MeshB,
    BoundingBox3d IntersectionBox)
{
    /// <summary>Gets the unique identifier of this clash instance.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Gets the volume of the intersection bounding box in cubic units.</summary>
    public double Volume => IntersectionBox.Volume;

    /// <summary>Gets the geometric center of the clash intersection box.</summary>
    public Vector3d Center => IntersectionBox.Center;

    /// <summary>Assembly model key of <see cref="ModelA"/>; null outside an assembly.</summary>
    public string? ModelKeyA { get; init; }

    /// <summary>Assembly model key of <see cref="ModelB"/>; null outside an assembly.</summary>
    public string? ModelKeyB { get; init; }

    /// <summary>Gets the 22-character buildingSMART IFC GlobalId of the first clashing element.</summary>
    public string IfcGuidA => ModelKeyA is { } key
        ? NwcElementGuid.ForMesh(MeshA, key)
        : NwcElementGuid.ForMesh(MeshA, ModelA);

    /// <summary>Gets the 22-character buildingSMART IFC GlobalId of the second clashing element.</summary>
    public string IfcGuidB => ModelKeyB is { } key
        ? NwcElementGuid.ForMesh(MeshB, key)
        : NwcElementGuid.ForMesh(MeshB, ModelB);

    /// <summary>Display name of ModelA.</summary>
    public string ModelAName => Path.GetFileNameWithoutExtension(ModelA.FileInfo.SourcePath ?? "ModelA");

    /// <summary>Display name of ModelB.</summary>
    public string ModelBName => Path.GetFileNameWithoutExtension(ModelB.FileInfo.SourcePath ?? "ModelB");

    /// <summary>Gets a human-readable display title for this clash.</summary>
    public string Title => FormattableString.Invariant($"Clash between {ModelAName} (#{MeshA.Id}) and {ModelBName} (#{MeshB.Id})");
}
