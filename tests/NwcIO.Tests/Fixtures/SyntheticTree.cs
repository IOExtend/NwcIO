using System.Buffers.Binary;

namespace NwcIO.Tests;

/// <summary>A property of a synthetic tab. Value by Type: 0 none; 1,5,6,7,10,11 double; 2,3 int; 4,9 string (null writes -1); 8 (string Display, string Internal).</summary>
internal sealed record Prop(string? Display, string? Internal, int Type, object? Value = null);

/// <summary>A class-86 tab. The same instance used in several hierarchy slots is one object: new on first use, back-referenced after.</summary>
internal sealed record Tab(string? Name, string? Display, string? Internal, params Prop[] Props);

internal abstract record Slot;

/// <summary>Class 86 in the hierarchy.</summary>
internal sealed record HTab(Tab Tab) : Slot;

/// <summary>Class 31 in the hierarchy: [str name][15 x f64 0].</summary>
internal sealed record HMat(string? Name) : Slot;

/// <summary>A null hierarchy slot and a class-86 <c>LcOaPartitionProps</c> root: the <see cref="HTab"/> body, always written new in its stream.</summary>
internal sealed record PTab(Tab Tab) : Slot;

/// <summary>A null hierarchy slot and a class-66 root: [str null][ptr new 52 (Display, "LcRevitId")][i32 0x01000000][i64 Value].</summary>
internal sealed record PId(long Value, string? Display = "Element ID") : Slot;

/// <summary>A null hierarchy slot and a class-79 root in the observed shape: [str null][i32 0][i32 0][i32 1][str "a"][str "b"][ptr new 52 ("h", "lcHyperlink")][i32 0].</summary>
internal sealed record P79 : Slot;

/// <summary>A stored node: class 53 when Children is not null, else class 22; username = (Name, Class). The same instance
/// placed twice is one stored node (new, then back-referenced), which gives shared subtrees.</summary>
internal sealed record TreeNode(string Name, string Class, int X = 0, Slot[]? Slots = null, TreeNode[]? Children = null);

/// <summary>Writes <c>LcOpNwdLogicalHierarchy</c> streams from <see cref="TreeNode"/> trees (docs/format-notes.md §2.2).</summary>
internal static class SyntheticTree
{
    /// <summary>
    /// The hierarchy stream [k = streams][n_i...][100][top count] nodes [1], and the <c>LcOaPartitionProps</c> root streams (empty
    /// when no slot is a P slot). The i-th node in write order that has P slots puts its roots in stream min(i, streams - 1).
    /// </summary>
    public static (byte[] Hierarchy, byte[][] Props) Write(int streams, params TreeNode[] top)
    {
        var w = new LcGraphWriter();
        w.Int(streams);
        for (int i = 0; i < streams; i++)
        {
            w.Int(0);
        }

        w.ImplicitRoot();
        w.Int(top.Length);

        // Identity by reference: two equal-looking records are two stored objects.
        var ids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var props = new PropsStreams(streams);
        foreach (var node in top)
        {
            WriteNode(w, ids, props, node);
        }

        w.Int(1);
        var hierarchy = w.ToArray();
        for (int i = 0; i < streams; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(hierarchy.AsSpan(4 + (4 * i)), props.Counts[i]);
        }

        return (hierarchy, props.Nodes == 0 ? [] : [.. props.Streams.Select(s => s.ToArray())]);
    }

    private static void WriteNode(LcGraphWriter w, Dictionary<object, int> ids, PropsStreams props, TreeNode node)
    {
        if (ids.TryGetValue(node, out int id))
        {
            w.Ref(id);
            return;
        }

        ids.Add(node, w.New(node.Children is null ? 22 : 53));
        w.Str(node.Name);
        w.New(52);
        w.Str(node.Name).Str(node.Class);
        Slot[] slots = node.Slots ?? [];
        w.Int(0).Int(node.X).Int(slots.Length);
        int stream = slots.Any(IsP) ? Math.Min(props.Nodes++, props.Streams.Length - 1) : 0;
        var p = props.Streams[stream];
        foreach (var slot in slots)
        {
            if (IsP(slot))
            {
                w.Null();
                props.Counts[stream]++;
            }

            switch (slot)
            {
                case HTab tab:
                    WriteTab(w, ids, tab.Tab);
                    break;
                case PTab tab:
                    WriteTabBody(p, tab.Tab);
                    break;
                case PId elementId:
                    p.New(66);
                    p.Str(null);
                    p.New(52);
                    p.Str(elementId.Display).Str("LcRevitId");
                    p.Int(0x01000000).Long(elementId.Value);
                    break;
                case P79:
                    p.New(79);
                    p.Str(null).Int(0).Int(0).Int(1).Str("a").Str("b");
                    p.New(52);
                    p.Str("h").Str("lcHyperlink");
                    p.Int(0);
                    break;
                case HMat material:
                    w.New(31);
                    w.Str(material.Name);
                    for (int i = 0; i < 15; i++)
                    {
                        w.Double(0);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(node), $"slot {slot} has no writer");
            }
        }

        if (node.Children is not null)
        {
            w.Int(node.Children.Length);
            foreach (var child in node.Children)
            {
                WriteNode(w, ids, props, child);
            }
        }
    }

    private static bool IsP(Slot slot) => slot is PTab or PId or P79;

    private static void WriteTab(LcGraphWriter w, Dictionary<object, int> ids, Tab tab)
    {
        if (ids.TryGetValue(tab, out int id))
        {
            w.Ref(id);
            return;
        }

        ids.Add(tab, w.Next);
        WriteTabBody(w, tab);
    }

    private static void WriteTabBody(LcGraphWriter w, Tab tab)
    {
        w.New(86);
        w.Str(tab.Name);
        w.New(52);
        w.Str(tab.Display).Str(tab.Internal);
        w.Int(0).Int(tab.Props.Length);
        foreach (var prop in tab.Props)
        {
            w.New(52);
            w.Str(prop.Display).Str(prop.Internal);
            w.Int(prop.Type);
            switch (prop.Type)
            {
                case 0:
                    break;
                case 1 or 5 or 6 or 7 or 10 or 11:
                    w.Double((double)prop.Value!);
                    break;
                case 2 or 3:
                    w.Int((int)prop.Value!);
                    break;
                case 4 or 9:
                    w.Str((string?)prop.Value);
                    break;
                case 8:
                    var (display, internalName) = ((string?, string?))prop.Value!;
                    w.New(52);
                    w.Str(display).Str(internalName);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tab), $"property type {prop.Type} has no writer");
            }
        }
    }

    // The LcOaPartitionProps root streams, their root counts, and how many nodes have put roots in them.
    private sealed class PropsStreams
    {
        public PropsStreams(int count)
        {
            Streams = [.. Enumerable.Range(0, count).Select(_ => new LcGraphWriter())];
            Counts = new int[count];
        }

        public LcGraphWriter[] Streams { get; }

        public int[] Counts { get; }

        public int Nodes { get; set; }
    }
}
