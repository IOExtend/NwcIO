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

    public static string ForMesh(NwcMesh mesh, NwcDocument document) => ForMesh(mesh, ModelKey(document));

    public static string ForMesh(NwcMesh mesh, string modelKey)
    {
        NwcNode? node = mesh.Node;
        Guid? authoringGuid = node?.ElementGuid ?? node?.Element?.ElementGuid;
        if (authoringGuid is { } guid)
        {
            return IfcGuid.ToIfcGuid(guid);
        }

        return IfcGuid.FromKey($"{modelKey}:mesh:{mesh.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
