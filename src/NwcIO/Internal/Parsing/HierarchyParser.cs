using System.Globalization;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Strict reader of the <c>LcOpNwdLogicalHierarchy</c> object graph (RS §2.3, §3). The stored nodes form a DAG; it is
/// expanded into one <see cref="NwcNode"/> per occurrence in depth-first preorder, the order fragments index (RS §6.1).
/// </summary>
internal static class HierarchyParser
{
    // shortcut: fixed cap on expanded node occurrences against crafted DAGs (real files stay near 1M); raise it if a real model needs more
    public const int MaxExpandedNodes = 1 << 23;

    // shortcut: fixed cap on property references copied into expanded nodes (every occurrence copies its node's list, about 1 GiB at the cap), against crafted DAGs; raise it if a real model needs more
    public const int MaxExpandedProperties = 1 << 27;

    private const string Segment = "LcOpNwdLogicalHierarchy";

    // Attribute bodies other than class 86 are parsed and dropped.
    private static readonly object Dropped = new();

    // Slot families as observed (RS §3, decision D12).
    private static ReadOnlySpan<int> TopLevelClasses => [53, 32];

    private static ReadOnlySpan<int> ChildClasses => [22, 53];

    private static ReadOnlySpan<int> UsernameClasses => [52];

    private static ReadOnlySpan<int> AttributeClasses => [86, 31, 40, 41, 39, 79, 200];

    private static ReadOnlySpan<int> ExtraClasses => [185];

    private static ReadOnlySpan<int> KnownClasses => [53, 22, 32, 52, 86, 31, 40, 41, 39, 79, 200, 185];

    /// <returns>The nodes in preorder; [0] is the synthetic root (Id 0, Name <paramref name="rootName"/>, Class "Model", Tag 0).</returns>
    public static IReadOnlyList<NwcNode> Read(byte[] data, string rootName)
    {
        var (top, total) = new Parser(new LcObjectReader(data, Segment)).ReadStream();
        return Expand(top, rootName, total);
    }

    /// <summary>Reads a class-185 body (material JSON with embedded files, RS §3.4), which the fragments stream holds too.</summary>
    internal static object ReadMaterialJson(LcObjectReader reader)
    {
        reader.ReadString();
        reader.ReadString();
        int count = reader.ReadCount(12);
        for (int i = 0; i < count; i++)
        {
            reader.ReadString();
            int embedded = reader.ReadInt32();
            if (embedded is not (0 or 1))
            {
                throw reader.Fail($"class-185 item flag {embedded} is neither 0 nor 1");
            }

            reader.ReadString();
            if (embedded == 1)
            {
                reader.ReadString();
                reader.SkipPadded(reader.ReadInt64());
            }
        }

        return Dropped;
    }

    // Preorder with an explicit stack: children in stored order, a back-referenced subtree walked again in full.
    private static List<NwcNode> Expand(Node[] top, string rootName, long total)
    {
        var root = new NwcNode { Id = 0, Name = rootName, Class = "Model", Tag = 0 };
        var nodes = new List<NwcNode>((int)total) { root };
        var pending = new Stack<(Node Node, NwcNode Parent)>();
        for (int i = top.Length - 1; i >= 0; i--)
        {
            pending.Push((top[i], root));
        }

        while (pending.TryPop(out var item))
        {
            var (node, parent) = item;
            var occurrence = new NwcNode { Id = nodes.Count, Name = node.Name, Class = node.Class, Tag = node.Tag, Parent = parent };
            occurrence.Properties.EnsureCapacity((int)node.OwnProperties);
            foreach (var tab in node.Tabs)
            {
                occurrence.Properties.AddRange(tab);
            }

            parent.Children.Add(occurrence);
            nodes.Add(occurrence);
            for (int i = node.Children.Length - 1; i >= 0; i--)
            {
                pending.Push((node.Children[i], occurrence));
            }
        }

        return nodes;
    }

    // A stored node object; a back-referenced one is shared, so it can expand to several NwcNodes. Tabs holds one reference per
    // property tab, a back-referenced tab being the same list, so records are copied only by Expand, after the caps passed.
    private sealed record Node(
        int Tag,
        string Name,
        string Class,
        List<List<NwcPropertyRecord>> Tabs,
        long OwnProperties,
        Node[] Children,
        long Size,
        long PropertyReferences,
        int Height);

    // A class-52 object: display name and internal name.
    private sealed record Username(int Id, string? Display, string? Internal);

    private sealed class Parser
    {
        private readonly LcObjectReader _reader;
        private readonly Func<int, object> _readBody;

        public Parser(LcObjectReader reader)
        {
            _reader = reader;
            _readBody = ReadBody;
        }

        // [k][k ints][100][n][n ptr][1], then the zero pad to 8 (RS §2.3).
        public (Node[] Top, long Total) ReadStream()
        {
            int k = _reader.ReadCount(4);
            if (k < 1)
            {
                throw _reader.Fail("header count k is below 1");
            }

            for (int i = 0; i < k; i++)
            {
                _reader.ReadInt32();
            }

            _reader.ReadImplicitRoot();
            var top = ReadNodes(TopLevelClasses);
            var (total, _, _) = Measure(top, 0);
            if (_reader.ReadInt32() != 1)
            {
                throw _reader.Fail("hierarchy tail is not 1");
            }

            _reader.ReadEnd();
            return (top, total);
        }

        private object ReadBody(int classId) => classId switch
        {
            53 or 22 => ReadNode(classId),
            32 => throw new NotSupportedException("LcOpNwdLogicalHierarchy holds .nwd partition nodes (class 32), which are not decoded; open with BuildSceneTree = false to read geometry and placement."),
            52 => ReadUsernameBody(),
            86 => ReadPropertyTab(),
            31 => ReadMaterial(),
            40 => ReadTransform(doubles: 3),
            41 => ReadTransform(doubles: 7),
            39 => ReadMatrix(),
            79 => ReadHyperlinks(),
            200 => ReadExtra(),
            185 => ReadMaterialJson(_reader),
            _ => throw new InvalidOperationException($"class {classId} has no body reader"),
        };

        private Node[] ReadNodes(ReadOnlySpan<int> allowedClasses)
        {
            // A node pointer is never null, so it takes at least [1][id].
            int count = _reader.ReadCount(8);

            // Grown as items are read: an array sized from the count could take the rest of the stream at every nesting level.
            var nodes = new List<Node>();
            for (int i = 0; i < count; i++)
            {
                nodes.Add((Node)_reader.ReadPointer(allowedClasses, _readBody, allowNull: false)!);
            }

            return [.. nodes];
        }

        // NODE53 / NODE22 (RS §3.1): name, username, flags, X, Y attributes, and for 53 the children.
        private Node ReadNode(int classId)
        {
            string? name = _reader.ReadString();
            var username = ReadUsername();
            _reader.ReadInt32();
            _reader.ReadInt32();
            int attributeCount = _reader.ReadCount(4);
            var tabs = new List<List<NwcPropertyRecord>>();
            long ownProperties = 0;
            for (int i = 0; i < attributeCount; i++)
            {
                if (_reader.ReadPointer(AttributeClasses, _readBody) is List<NwcPropertyRecord> tab)
                {
                    tabs.Add(tab);
                    ownProperties = checked(ownProperties + tab.Count);
                }
            }

            Node[] children = classId == 53 ? ReadNodes(ChildClasses) : [];
            var (size, propertyReferences, height) = Measure(children, ownProperties);
            return new Node(classId, name ?? username.Display ?? "", username.Internal ?? "", tabs, ownProperties, children, size, propertyReferences, height);
        }

        // Expanded size, property references and height of a node with these children; children finish before their parent, so theirs are known.
        private (long Size, long PropertyReferences, int Height) Measure(Node[] children, long ownProperties)
        {
            long size = 1;
            long propertyReferences = ownProperties;
            int height = 0;
            foreach (var child in children)
            {
                size = checked(size + child.Size);
                propertyReferences = checked(propertyReferences + child.PropertyReferences);
                height = Math.Max(height, child.Height);
            }

            height++;
            if (size > MaxExpandedNodes)
            {
                throw _reader.Fail($"expanded tree exceeds {MaxExpandedNodes} nodes");
            }

            // Each occurrence copies its node's records, so a small DAG over a property-heavy leaf multiplies them.
            if (propertyReferences > MaxExpandedProperties)
            {
                throw _reader.Fail($"expanded tree exceeds {MaxExpandedProperties} property references");
            }

            if (height > LcObjectReader.MaxNestingDepth)
            {
                throw _reader.Fail($"expanded tree exceeds {LcObjectReader.MaxNestingDepth} levels");
            }

            return (size, propertyReferences, height);
        }

        private Username ReadUsername() => (Username)_reader.ReadPointer(UsernameClasses, _readBody, allowNull: false)!;

        private Username ReadUsernameBody()
        {
            int id = _reader.Next - 1;
            string? display = _reader.ReadString();
            return new Username(id, display, _reader.ReadString());
        }

        // Class 86 (RS §3.4): [str name][ptr username][i32 flags][i32 n] n x ([ptr key][i32 type][value]); one record per item.
        private List<NwcPropertyRecord> ReadPropertyTab()
        {
            string? name = _reader.ReadString();
            var username = ReadUsername();
            _reader.ReadInt32();
            string tab = name ?? username.Display ?? "";

            // An item is at least a back-referenced key and the type; the list grows as items are read, not from the count.
            int count = _reader.ReadCount(12);
            var records = new List<NwcPropertyRecord>();
            for (int i = 0; i < count; i++)
            {
                var key = ReadUsername();
                int type = _reader.ReadInt32();
                string? value = type switch
                {
                    0 => null,

                    // Type 5 was never seen; RS §3.4 reads it as f64 like the other real types.
                    1 or 5 or 6 or 7 or 10 or 11 => _reader.ReadDouble().ToString(CultureInfo.InvariantCulture),
                    2 or 3 => _reader.ReadInt32().ToString(CultureInfo.InvariantCulture),
                    4 => _reader.ReadString(),
                    8 => ReadUsername().Display,
                    _ => throw _reader.Fail($"property value type {type} is unknown"),
                };
                records.Add(new NwcPropertyRecord
                {
                    FirstInt = key.Id,
                    Tag = 52,
                    Name = key.Display ?? "",
                    Class = tab,
                    ValueType = type,
                    Value = value,
                });
            }

            return records;
        }

        // Class 31: [str name][15 x f64].
        private object ReadMaterial()
        {
            _reader.ReadString();
            for (int i = 0; i < 15; i++)
            {
                _reader.ReadDouble();
            }

            return Dropped;
        }

        // Classes 40 and 41: [str name][i32 X][i32 flags] then the translation (and rotation) doubles.
        private object ReadTransform(int doubles)
        {
            _reader.ReadString();
            _reader.ReadInt32();
            _reader.ReadInt32();
            for (int i = 0; i < doubles; i++)
            {
                _reader.ReadDouble();
            }

            return Dropped;
        }

        // Class 39: the class-40 head, 9 + 3 doubles, then [i32 Y].
        private object ReadMatrix()
        {
            ReadTransform(doubles: 12);
            _reader.ReadInt32();
            return Dropped;
        }

        // Class 79: [str][i32][i32][i32 n] n x ([str][str][ptr][i32 pts][3 pts f64]), no trailing int per item.
        private object ReadHyperlinks()
        {
            _reader.ReadString();
            _reader.ReadInt32();
            _reader.ReadInt32();
            int count = _reader.ReadCount(16);
            for (int i = 0; i < count; i++)
            {
                _reader.ReadString();
                _reader.ReadString();

                // The item pointer's class family was not measured (RS §11), so any class this parser knows, or null, is accepted.
                _reader.ReadPointer(KnownClasses, _readBody);
                int points = _reader.ReadCount(24);
                for (int p = 0; p < 3 * points; p++)
                {
                    _reader.ReadDouble();
                }
            }

            return Dropped;
        }

        // Class 200: [str name][i32 X][i32 Y][ptr extra], the extra a class 185 in every file that has class 200.
        private object ReadExtra()
        {
            _reader.ReadString();
            _reader.ReadInt32();
            _reader.ReadInt32();
            _reader.ReadPointer(ExtraClasses, _readBody, allowNull: false);
            return Dropped;
        }
    }
}
