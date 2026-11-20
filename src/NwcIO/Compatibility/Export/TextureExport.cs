using System.Numerics;
using NwcIO.Model;

namespace NwcIO.Internal.Export;

internal static class TextureExport
{
    public static IReadOnlyList<Vector2>? GetCoordinates(NwcMesh mesh, int vertexCount, CancellationToken cancellationToken)
    {
        if (mesh.TexCoords is not { } coordinates || coordinates.Count != vertexCount) return null;

        for (int i = 0; i < coordinates.Count; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var uv = coordinates[i];
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y)) return null;
        }
        return coordinates;
    }

    // Export a reference only. A model-supplied path must not cause the exporter to open a file or fetch a URL.
    public static string? GetLocalPath(NwcXRef? texture)
    {
        string? path = texture?.EffectivePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim() || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029')) return null;

        int colon = path.IndexOf(':', IsDrivePath(path) ? 2 : 0);
        return colon < 0 ? path : null;
    }

    public static string? GetImageUri(NwcXRef? texture)
    {
        if (GetLocalPath(texture) is not { } path) return null;
        string extension = Path.GetExtension(path);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)) return null;

        if (IsDrivePath(path)) return $"file:///{path[..2]}{EscapePath(path[2..])}";
        if (path[0] == '/') return "file://" + EscapePath(path);
        return EscapePath(path);
    }

    private static bool IsDrivePath(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/';

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
