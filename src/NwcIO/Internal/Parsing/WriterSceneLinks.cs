using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>Restores ownership and property tabs from an explicitly marked writer cache.</summary>
internal static class WriterSceneLinks
{
    internal const string MetreCacheOption = "IOExtend.NwcWriter.Scene/1";
    private const string LinkClass = "IOExtend.NwcWriter.Link";
    private const string LinkName = "Mesh ID";

    internal static void Attach(NwcNode root, List<NwcMesh> meshes, bool readMeshes, List<NwcDiagnostic> diagnostics)
    {
        var byId = new Dictionary<int, NwcMesh>(meshes.Count);
        foreach (var mesh in meshes)
        {
            if (!byId.TryAdd(mesh.Id, mesh))
            {
                diagnostics.Add(new(NwcDiagnosticSeverity.Error, "duplicate-writer-mesh-id",
                    $"Writer cache mesh ID {mesh.Id} is repeated. Mesh ownership was not restored."));
                return;
            }
        }

        var pending = new Stack<NwcNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            foreach (var group in node.Properties.Where(p => p.Class != LinkClass).GroupBy(p => p.Class))
                node.PropertyTabs.Add(new NwcPropertyTab { Name = group.Key, Properties = group.ToArray() });

            if (readMeshes)
            {
                foreach (var link in node.Properties.Where(p => p.Class == LinkClass && p.Name == LinkName))
                {
                    if (link.ValueType != 2 || link.IntValue is not (>= int.MinValue and <= int.MaxValue) ||
                        !byId.TryGetValue((int)link.IntValue.Value, out var mesh) || mesh.Node is not null)
                    {
                        diagnostics.Add(new(NwcDiagnosticSeverity.Error, "invalid-writer-mesh-link",
                            $"Node {node.Id} has an invalid or repeated writer mesh link."));
                        continue;
                    }

                    mesh.Node = node;
                    node.Meshes.Add(mesh);
                    node.Mesh ??= mesh;
                }
            }

            foreach (var child in node.Children)
                pending.Push(child);
        }
    }
}
