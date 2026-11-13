using NwcIO.Model;

namespace NwcIO.Tests;

internal static class TestMeshes
{
    public static NwcMesh MirroredQuad() => new()
    {
        Id = 1,
        Vertices = [new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(1, 1, 0)],
        Indices = [0, 1, 2, 2, 1, 3],
        Transform = new NwcTransform { Id = 1, Scale = -1 },
    };
}
