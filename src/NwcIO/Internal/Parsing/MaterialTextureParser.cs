using System.Text.Json;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

internal static class MaterialTextureParser
{
    public static string? ReadDiffusePath(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? rootId = SingleString(Property(root, "userassets"));
            var materials = Property(root, "materials");
            if (rootId is null) return null;

            var material = Property(materials, rootId);
            var diffuse = Property(Property(Property(material, "properties"), "colors"), "generic_diffuse");
            if (Property(diffuse, "IsConnectionEnabled").ValueKind != JsonValueKind.True) return null;
            string? bitmapId = SingleString(Property(diffuse, "connections"));
            if (bitmapId is null) return null;

            var bitmap = Property(materials, bitmapId);
            var definition = Property(bitmap, "definition");
            if (definition.ValueKind != JsonValueKind.String || !definition.ValueEquals("UnifiedBitmap")) return null;
            var uris = Property(Property(bitmap, "properties"), "uris");
            string? path = SingleString(Property(Property(uris, "unifiedbitmap_Bitmap"), "values"));
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(path, UriKind.Absolute, out var file) && file.IsFile && file.Host.Length == 0)
            {
                return file.LocalPath;
            }
            return path;
        }
        catch (JsonException)
        {
            // Appearance JSON is optional and the base diffuse colour is stored separately.
            return null;
        }
    }

    public static Dictionary<string, NwcXRef> Index(NwcXRefTable? table)
    {
        var index = new Dictionary<string, NwcXRef>(StringComparer.Ordinal);
        if (table is not null)
        {
            foreach (var reference in table.References)
            {
                if (reference.OriginalPath.Length > 0) index.TryAdd(Key(reference.OriginalPath), reference);
            }
            foreach (var reference in table.References)
            {
                if (reference.RemappedPath.Length > 0) index.TryAdd(Key(reference.RemappedPath), reference);
            }
        }
        return index;
    }

    public static NwcXRef? Resolve(string? path, Dictionary<string, NwcXRef> references) =>
        path is null ? null : references.TryGetValue(Key(path), out var reference) ? reference : new NwcXRef(path, "");

    private static string Key(string path)
    {
        string key = path.Replace('\\', '/');
        return key.StartsWith("//", StringComparison.Ordinal) || key.Length >= 2 && key[1] == ':' ? key.ToUpperInvariant() : key;
    }

    private static JsonElement Property(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var val) ? val : default;

    private static string? SingleString(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array && element.GetArrayLength() == 1 && element[0].ValueKind == JsonValueKind.String
            ? element[0].GetString() : null;
}
