using System.Globalization;
using NwcIO.Internal.Export;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Strict reader of the <c>LcOpNwdLogicalHierarchy</c> object graph (docs/format-notes.md §2.2). The stored nodes form a DAG; it is
/// expanded into one <see cref="NwcNode"/> per occurrence in depth-first preorder, the order fragments index (docs/format-notes.md §2.2).
/// </summary>
internal static class HierarchyParser
{
    // shortcut: fixed cap on expanded node occurrences against crafted DAGs (real files stay near 1M); raise it if a real model needs more
    public const int MaxExpandedNodes = 1 << 23;

    // shortcut: fixed cap on property and property-tab references copied into expanded nodes (every occurrence copies its node's lists, about 1 GiB at the cap), against crafted DAGs; raise it if a real model needs more
    public const int MaxExpandedProperties = 1 << 27;

    private const string Segment = "LcOpNwdLogicalHierarchy";
    private const string PropsSegment = "LcOaPartitionProps";

    // Attribute bodies other than classes 86 and 31 are parsed and dropped.
    private static readonly object Dropped = new();

    // Slot families as observed (docs/format-notes.md §2.2).
    private static ReadOnlySpan<int> TopLevelClasses => [53, 32];

    private static ReadOnlySpan<int> ChildClasses => [22, 53];

    private static ReadOnlySpan<int> UsernameClasses => [52];

    private static ReadOnlySpan<int> AttributeClasses => [86, 31, 40, 41, 39, 79, 200];

    private static ReadOnlySpan<int> ExtraClasses => [185];

    private static ReadOnlySpan<int> PartitionRootClasses => [86, 79, 66];

    private static ReadOnlySpan<int> KnownClasses => [53, 22, 32, 52, 86, 31, 40, 41, 39, 79, 200, 185];

    /// <param name="includeProperties">False leaves every node's properties empty; the class-86 bodies are still read and checked.</param>
    /// <param name="guids">The <c>LcOpGuidStore</c> table that element indices point into; null is an empty table.</param>
    /// <param name="partitionProps">
    /// Gives the <c>LcOaPartitionProps</c> root streams, its table of contents excluded; called once, at the first null attribute slot or
    /// after the hierarchy tail. It returns null, or is null, when the file has no such segment; an empty list is a segment without streams.
    /// </param>
    /// <returns>The nodes in preorder; [0] is the synthetic root (Id 0, Name <paramref name="rootName"/>, Class "Model", Tag 0).</returns>
    /// <exception cref="NotSupportedException">One element index sits on nodes under different parents (a linked Revit model).</exception>
    public static IReadOnlyList<NwcNode> Read(
        byte[] data,
        string rootName,
        bool includeProperties = true,
        IReadOnlyList<Guid>? guids = null,
        Func<IReadOnlyList<byte[]>?>? partitionProps = null)
    {
        guids ??= [];
        var (top, total) = new Parser(new LcObjectReader(data, Segment), guids.Count, partitionProps ?? (() => null)).ReadStream();
        return Expand(top, rootName, total, includeProperties, guids);
    }

    /// <summary>Reads a class-185 body (material JSON with embedded files, docs/format-notes.md §2.2), which the fragments stream holds too.</summary>
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

    // IFC_GUID (-1019000) of the element's own tab, when it has the IFC form: 22 characters of the IFC alphabet, the first 0-3 (128 bits).
    private static string? IfcParameter(List<NwcPropertyTab> tabs)
    {
        foreach (var tab in tabs)
        {
            foreach (var record in tab.Properties)
            {
                if (record.TabInternalName == "LcRevitData_Element" && record.InternalName == "lcldrevit_parameter_-1019000" && record.ValueType == 4)
                {
                    return IsIfcGlobalId(record.Value) ? record.Value : null;
                }
            }
        }

        return null;
    }

    private static bool IsIfcGlobalId(string? text) =>
        text is { Length: 22 } && text[0] is >= '0' and <= '3' && text.All(c => c is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or '$');

    // Preorder with an explicit stack: children in stored order, a back-referenced subtree walked again in full.
    private static List<NwcNode> Expand(Node[] top, string rootName, long total, bool includeProperties, IReadOnlyList<Guid> guids)
    {
        var root = new NwcNode { Id = 0, Name = rootName, Class = "Model", Tag = 0 };
        var nodes = new List<NwcNode>((int)total) { root };
        var firstByX = new Dictionary<int, NwcNode>();
        var pending = new Stack<(Node Node, NwcNode Parent)>();
        for (int i = top.Length - 1; i >= 0; i--)
        {
            pending.Push((top[i], root));
        }

        while (pending.TryPop(out var item))
        {
            var (node, parent) = item;
            var occurrence = new NwcNode
            {
                Id = nodes.Count,
                Name = node.Name,
                Class = node.Class,
                Tag = node.Tag,
                Parent = parent,
                MaterialName = node.MaterialName ?? parent.MaterialName,
            };
            if (includeProperties)
            {
                occurrence.Properties.EnsureCapacity((int)node.OwnProperties - node.Tabs.Count);
                occurrence.PropertyTabs.AddRange(node.Tabs);
                foreach (var tab in node.Tabs)
                {
                    occurrence.Properties.AddRange(tab.Properties);
                }
            }

            // X > 0 marks an element node (docs/format-notes.md §2.2). The exporter writes some elements as two sibling nodes with one X;
            // the first is the element and the second points to it. One X under different parents is a placed (linked) model.
            if (node.X == 0)
            {
                occurrence.Element = parent.Element;
            }
            else if (!firstByX.TryGetValue(node.X, out var first))
            {
                occurrence.IsElement = true;
                occurrence.Element = occurrence;
                occurrence.ElementGuid = guids[node.X - 1];
                occurrence.IfcGlobalId = IfcParameter(node.Tabs) ?? IfcGuid.ToIfcGuid(occurrence.ElementGuid.Value);
                firstByX.Add(node.X, occurrence);
            }
            else if (ReferenceEquals(first.Parent, parent))
            {
                occurrence.Element = first;
            }
            else
            {
                throw new NotSupportedException("Revit links are not supported yet: one element index sits on nodes under different parents in LcOpNwdLogicalHierarchy.");
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
    // property tab slot, a back-referenced tab being the same object, so tabs and records are copied only by Expand, after the caps passed.
    // OwnProperties counts the records plus one per tab slot, the references each occurrence copies. MaterialName is the name of the
    // node's first class-31 slot ("" for a null name), null without one.
    private sealed record Node(
        int Tag,
        string Name,
        string Class,
        int X,
        string? MaterialName,
        List<NwcPropertyTab> Tabs,
        long OwnProperties,
        Node[] Children,
        long Size,
        long PropertyReferences,
        int Height);

    // A class-52 object: display name and internal name.
    private sealed record Username(int Id, string? Display, string? Internal);

    // A class-31 object: its material name.
    private sealed record Material(string? Name);

    private sealed class Parser
    {
        private readonly LcObjectReader _reader;
        private readonly int _guidCount;

        // Null in a parser of one LcOaPartitionProps stream, which holds roots and no nodes.
        private readonly Func<IReadOnlyList<byte[]>?>? _partitionProps;
        private readonly Func<int, object> _readBody;

        // The roots the null attribute slots take; ReadStream sets it from the header.
        private PartitionRoots? _roots;

        public Parser(LcObjectReader reader, int guidCount, Func<IReadOnlyList<byte[]>?>? partitionProps)
        {
            _reader = reader;
            _guidCount = guidCount;
            _partitionProps = partitionProps;
            _readBody = ReadBody;
        }

        // [k][k root counts][100][n][n ptr][1], then the zero pad to 8 (docs/format-notes.md §2.2).
        public (Node[] Top, long Total) ReadStream()
        {
            int k = _reader.ReadCount(4);
            if (k < 1)
            {
                throw _reader.Fail("header count k is below 1");
            }

            var rootCounts = new int[k];
            for (int i = 0; i < k; i++)
            {
                rootCounts[i] = _reader.ReadCount(0);
            }

            _roots = new PartitionRoots(_reader, rootCounts, _partitionProps!);
            _reader.ReadImplicitRoot();
            var top = ReadNodes(TopLevelClasses);
            var (total, _, _) = Measure(top, 0);
            if (_reader.ReadInt32() != 1)
            {
                throw _reader.Fail("hierarchy tail is not 1");
            }

            _roots.End();
            _reader.ReadEnd();
            return (top, total);
        }

        // A LcOaPartitionProps root is always a new object (docs/format-notes.md §2.2.1).
        public object ReadPartitionRoot() => _reader.ReadPointer(PartitionRootClasses, _readBody, allowNull: false, allowBackReference: false)!;

        public void ReadEnd() => _reader.ReadEnd();

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
            66 => ReadElementId(),
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

        // NODE53 / NODE22 (docs/format-notes.md §2.2): name, username, flags, X, Y attributes, and for 53 the children.
        private Node ReadNode(int classId)
        {
            string? name = _reader.ReadString();
            var username = ReadUsername();
            _reader.ReadInt32();
            int x = _reader.ReadInt32();
            if (x < 0 || x > _guidCount)
            {
                throw _reader.Fail($"element index {x} is outside the GUID table of {_guidCount}");
            }

            int attributeCount = _reader.ReadCount(4);
            var tabs = new List<NwcPropertyTab>();
            long ownProperties = 0;
            string? materialName = null;
            for (int i = 0; i < attributeCount; i++)
            {
                // A null slot takes the next LcOaPartitionProps root (docs/format-notes.md §2.2.1); a stored node is read once, so a shared one takes its roots once.
                switch (_reader.ReadPointer(AttributeClasses, _readBody) ?? _roots!.Take())
                {
                    case NwcPropertyTab tab:
                        tabs.Add(tab);

                        // An empty tab still costs a PropertyTabs entry in every occurrence.
                        ownProperties = checked(ownProperties + tab.Properties.Count + 1);
                        break;
                    case Material material:
                        materialName ??= material.Name ?? "";
                        break;
                }
            }

            Node[] children = classId == 53 ? ReadNodes(ChildClasses) : [];
            var (size, propertyReferences, height) = Measure(children, ownProperties);
            return new Node(classId, name ?? username.Display ?? "", username.Internal ?? "", x, materialName, tabs, ownProperties, children, size, propertyReferences, height);
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

            // Each occurrence copies its node's tab references and records, so a small DAG over a property-heavy leaf multiplies them.
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

        // Class 86 (docs/format-notes.md §2.2): [str name][ptr username][i32 flags][i32 n] n x ([ptr key][i32 type][value]); one record per item.
        private NwcPropertyTab ReadPropertyTab()
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

                    // Type 5 was never seen; it is read as f64 like the other real types (docs/format-notes.md §2.2).
                    1 or 5 or 6 or 7 or 10 or 11 => _reader.ReadDouble().ToString(CultureInfo.InvariantCulture),
                    2 or 3 => _reader.ReadInt32().ToString(CultureInfo.InvariantCulture),

                    // Type 9 was never seen; it is read as a string (docs/format-notes.md §2.2).
                    4 or 9 => _reader.ReadString(),
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
                    TabInternalName = username.Internal,
                    InternalName = key.Internal,
                });
            }

            return new NwcPropertyTab { Name = tab, InternalName = username.Internal, Properties = records };
        }

        // Class 66 (docs/format-notes.md §2.2.1): [str name = null][ptr 52 key][i32 flags = 0x01000000] PAD8 [i64 value in 1..2^31-1],
        // a one-property tab named by its key.
        private NwcPropertyTab ReadElementId()
        {
            if (_reader.ReadString() is not null)
            {
                throw _reader.Fail("class-66 name is not null");
            }

            var key = ReadUsername();
            int flags = _reader.ReadInt32();
            if (flags != 0x01000000)
            {
                throw _reader.Fail($"class-66 flags {flags:X8} are not 01000000");
            }

            long value = _reader.ReadInt64();
            if (value is < 1 or > int.MaxValue)
            {
                throw _reader.Fail($"class-66 value {value} is outside 1..{int.MaxValue}");
            }

            string name = key.Display ?? "";
            var record = new NwcPropertyRecord
            {
                FirstInt = key.Id,
                Tag = 52,
                Name = name,
                Class = name,
                ValueType = 2,
                Value = value.ToString(CultureInfo.InvariantCulture),
                TabInternalName = key.Internal,
                InternalName = key.Internal,
            };
            return new NwcPropertyTab { Name = name, InternalName = key.Internal, Properties = [record] };
        }

        // Class 31: [str name][15 x f64]; the doubles are dropped.
        private Material ReadMaterial()
        {
            var material = new Material(_reader.ReadString());
            for (int i = 0; i < 15; i++)
            {
                _reader.ReadDouble();
            }

            return material;
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

                // In a LcOaPartitionProps root the item is class 52, so no node can start there (docs/format-notes.md §2.2.1). In the
                // hierarchy the item pointer's class family was not measured, so any class this parser knows, or null, is accepted.
                _reader.ReadPointer(_partitionProps is null ? UsernameClasses : KnownClasses, _readBody);
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

    // The LcOaPartitionProps roots (docs/format-notes.md §2.2.1), read on demand so that a .nwd fails at its first class-32 node before
    // any of them: the n-th null attribute slot of the hierarchy takes the n-th root of stream 0, then stream 1, and so on.
    private sealed class PartitionRoots
    {
        private readonly LcObjectReader _hierarchy;
        private readonly int[] _counts;
        private readonly Func<IReadOnlyList<byte[]>?> _source;
        private IReadOnlyList<byte[]> _streams = [];
        private long _unread;
        private bool _checked;
        private int _slots;

        // The current stream, the roots read from it and its parser; the streams before it are read and ended.
        private int _stream;
        private int _taken;
        private Parser? _current;

        public PartitionRoots(LcObjectReader hierarchy, int[] counts, Func<IReadOnlyList<byte[]>?> source)
        {
            _hierarchy = hierarchy;
            _counts = counts;
            _source = source;
            _unread = counts.Sum(c => (long)c);
        }

        /// <summary>The root of the next null attribute slot.</summary>
        public object Take()
        {
            Check();
            _slots++;
            if (_stream == _streams.Count)
            {
                throw _hierarchy.Fail($"null attribute slot {_slots} has no {PropsSegment} root");
            }

            object root = Current().ReadPartitionRoot();
            _taken++;
            _unread--;
            EndReadStreams();
            return root;
        }

        /// <summary>After the hierarchy tail: every root filled a slot. Each stream was ended after its last root, or when passed.</summary>
        public void End()
        {
            Check();
            if (_unread > 0)
            {
                throw _hierarchy.Fail($"{_unread} {PropsSegment} roots fill no null attribute slot");
            }
        }

        private void Check()
        {
            if (_checked)
            {
                return;
            }

            _checked = true;
            var streams = _source();
            if (streams is null)
            {
                if (_unread > 0)
                {
                    throw _hierarchy.Fail($"the header names {_unread} {PropsSegment} roots, but the file has no {PropsSegment} segment");
                }

                return;
            }

            if (streams.Count != _counts.Length)
            {
                throw new InvalidDataException($"{PropsSegment}: {streams.Count} root streams, but the hierarchy header names {_counts.Length}");
            }

            _streams = streams;
            EndReadStreams();
        }

        // Ends the current stream while all its roots are read, so a stream with no roots is ended when passed.
        private void EndReadStreams()
        {
            while (_stream < _streams.Count && _taken == _counts[_stream])
            {
                Current().ReadEnd();
                _stream++;
                _taken = 0;
                _current = null;
            }
        }

        private Parser Current() => _current ??= new Parser(new LcObjectReader(_streams[_stream], $"{PropsSegment} stream {_stream}"), 0, null);
    }
}
