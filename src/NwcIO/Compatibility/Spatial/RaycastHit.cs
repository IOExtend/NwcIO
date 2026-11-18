namespace NwcIO.Model.Spatial;

/// <summary>
/// Detailed result of a 3D raycast intersection against geometry in the scene.
/// </summary>
/// <param name="Distance">Distance from ray origin to the intersection point along the ray direction.</param>
/// <param name="Point">World coordinates of the 3D intersection point.</param>
/// <param name="Normal">Outward surface normal at the hit location.</param>
/// <param name="Mesh">The <see cref="NwcMesh"/> containing the intersected triangle.</param>
/// <param name="TriangleIndex">Zero-based index of the triangle within the mesh.</param>
public readonly record struct RaycastHit(
    double Distance,
    Vector3d Point,
    Vector3d Normal,
    NwcMesh Mesh,
    int TriangleIndex);
