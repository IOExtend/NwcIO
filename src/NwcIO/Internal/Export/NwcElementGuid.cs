using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Stable IFC GlobalIds for meshes, derived from the model's source path, the authoring element id when the
/// mesh has one and the mesh id, so equal mesh ids in different models stay distinct, every fragment of an
/// element keeps its own id, and repeated exports of the same input produce the same ids.
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

    /// <summary>
    /// GlobalId of one mesh product. The authoring element id (the Revit IFC GlobalId, else the element GUID)
    /// is only a base: several fragments of one element, and copies of one element across assembly models,
    /// must still get distinct ids, so the model key and the mesh id are part of the key.
    /// </summary>
    public static string ForMesh(NwcMesh mesh, string modelKey)
    {
        string meshId = mesh.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        NwcNode? owner = mesh.Node?.Element ?? mesh.Node;
        if (!string.IsNullOrEmpty(owner?.IfcGlobalId))
        {
            return IfcGuid.FromKey($"{modelKey}:element:{owner.IfcGlobalId}:mesh:{meshId}");
        }

        if (owner?.ElementGuid is { } guid && guid != Guid.Empty)
        {
            return IfcGuid.FromKey($"{modelKey}:element:{guid:D}:mesh:{meshId}");
        }

        return IfcGuid.FromKey($"{modelKey}:mesh:{meshId}");
    }
}
