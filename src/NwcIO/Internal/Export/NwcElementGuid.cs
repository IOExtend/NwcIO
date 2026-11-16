using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Stable IFC GlobalIds for meshes. The authoring element GUID wins; otherwise the id is derived from the
/// model's source path and the mesh id, so equal mesh ids in different models stay distinct and repeated
/// exports of the same input produce the same ids.
/// </summary>
internal static class NwcElementGuid
{
    /// <summary>Model identity used in deterministic keys: the source path, or a fixed fallback.</summary>
    public static string ModelKey(NwcDocument document) =>
        document.FileInfo.SourcePath is { Length: > 0 } path ? path : "Model";

    /// <summary>
    /// One model key per assembly model. A source path is unique on its own; when several models share one
    /// (or all use the fallback), the model position disambiguates them.
    /// </summary>
    public static string[] ModelKeys(IReadOnlyList<NwcDocument> models)
    {
        var baseKeys = new string[models.Count];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < models.Count; i++)
        {
            baseKeys[i] = ModelKey(models[i]);
            counts[baseKeys[i]] = counts.GetValueOrDefault(baseKeys[i]) + 1;
        }

        var result = new string[models.Count];
        for (int i = 0; i < models.Count; i++)
        {
            result[i] = counts[baseKeys[i]] > 1
                ? $"{baseKeys[i]}#{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : baseKeys[i];
        }

        return result;
    }

    public static string ForMesh(NwcMesh mesh, NwcDocument document) => ForMesh(mesh, ModelKey(document));

    public static string ForMesh(NwcMesh mesh, string modelKey)
    {
        // One element usually owns several fragments (meshes); the element GUID alone would give every
        // mesh the same GlobalId. Keep the authoring GUID as a stable prefix and add the mesh identity.
        NwcNode? node = mesh.Node;
        Guid? authoringGuid = node?.ElementGuid ?? node?.Element?.ElementGuid;
        string meshId = mesh.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (authoringGuid is { } guid && guid != Guid.Empty)
        {
            return IfcGuid.FromKey($"{modelKey}:element:{guid:D}:mesh:{meshId}");
        }

        return IfcGuid.FromKey($"{modelKey}:mesh:{meshId}");
    }
}
