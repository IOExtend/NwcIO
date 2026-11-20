using System.Globalization;
using System.Security.Cryptography;
using NwcIO.Model;

namespace NwcIO.Internal.Container;

internal sealed class NativeCacheModelWriter : IDisposable
{
    private const string RevitLoader = "nwexportrevit";
    private const string WriterSource = "IOExtend.NwcWriter.Native/1";
    private const string LegacyMeshLink = "IOExtend.NwcWriter.Link";
    private const string Cache = @"cache\";
    private const string PartitionProps = Cache + "LcOaPartitionProps";
    private readonly NativeCacheBinaryWriter _hierarchy = new();
    private readonly NativeCacheBinaryWriter _properties = new();
    private readonly NativeCacheBinaryWriter _fragments = new();
    private readonly MemoryStream _geometry = new();
    private readonly List<int> _recordLengths = [];
    private readonly List<Guid> _guids = [];
    private readonly List<int> _hiddenNodes = [];
    private readonly Dictionary<RgbaColor, int> _appearances = [];
    private readonly ILookup<NwcNode?, NwcMesh> _meshes;
    private readonly double _feetPerUnit;
    private int _propertyCount;
    private int _sequence;

    private NativeCacheModelWriter(NwcDocument document, HashSet<NwcNode> nodes)
    {
        if (document.Meshes.Count == 0)
            throw new ArgumentException("Native NWC output requires at least one mesh.", nameof(document));
        if (document.Meshes.Any(mesh => mesh.Node is null || !nodes.Contains(mesh.Node)))
            throw new ArgumentException("Every native NWC mesh must have an owner in the document tree.", nameof(document));

        _feetPerUnit = (document.MetresPerUnit ??
            throw new ArgumentException("Native NWC output requires known document units.", nameof(document))) / 0.3048;
        _meshes = document.Meshes.ToLookup(mesh => mesh.Node);
    }

    public static IReadOnlyList<NativeCacheSegment> Serialize(NwcDocument document)
    {
        var root = document.RootNode ??
            throw new ArgumentException("Native NWC output requires a document tree.", nameof(document));
        var nodes = GetNodes(root);
        using var writer = new NativeCacheModelWriter(document, nodes);
        return writer.Write(document, root);
    }

    private List<NativeCacheSegment> Write(NwcDocument document, NwcNode root)
    {
        string sourceName = document.FileInfo.SourcePath ?? root.Name;
        _hierarchy.Write(1);
        _hierarchy.Write(0); // Patched to the number of roots after visiting the tree.
        _hierarchy.Write(_hierarchy.NextId());
        _hierarchy.Write(1);
        _fragments.Write(document.Meshes.Count);
        WriteNode(root, hiddenAncestor: false);
        _hierarchy.Write(1);
        long end = _hierarchy.BaseStream.Position;
        _hierarchy.BaseStream.Position = 4;
        _hierarchy.Write(_propertyCount);
        _hierarchy.BaseStream.Position = end;

        byte[] hierarchy = _hierarchy.Finish();
        byte[] fragments = _fragments.Finish();
        var geometry = NativeCacheGeometryWriter.BuildSegment(_geometry, _recordLengths);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(hierarchy);
        hash.AppendData(fragments);
        hash.AppendData(geometry.Data);
        hash.AppendData(_properties.Finish());
        byte[] identity = hash.GetHashAndReset();

        List<NativeCacheSegment> segments =
        [
            Single("IOExtendNwcSource", WriteSource(document.FileInfo, sourceName)),
            // Fixed native descriptor for the triangle compression layout.
            new(Cache + "LcOpNwdGeometryCompress",
                Convert.FromHexString("002dc362d0a4cc400a62d4a4456a9111e0cc23dce955195e726cf7a057b89ed524c6a35076afac74"),
                Flags: 3),
            geometry,
            Single(Cache + "LcOpNwdPartition", WritePartition(sourceName)),
            WritePartitionProps(),
            Single(Cache + "LcOpNwdLogicalHierarchy", hierarchy),
            Single(Cache + "LcOpNwdFragments", fragments),
            Single(Cache + "LcOpNwdSpatialHierarchy", WriteSpatialHierarchy(_recordLengths.Count)),
            Single(Cache + "LcOpGuidStore", WriteGuids()),
            Single("LcOpOdyFileInfoChunk", WriteFileInfo(sourceName, identity)),
        ];
        if (_hiddenNodes.Count > 0)
            segments.Add(Single(Cache + "LcOpShadOverridesElement", WriteVisibilityOverrides()));
        return segments;
    }

    private byte[] WriteVisibilityOverrides()
    {
        using var writer = new NativeCacheBinaryWriter();
        writer.Write(_hiddenNodes.Count);
        foreach (int sequence in _hiddenNodes)
        {
            writer.Write(sequence);
            writer.Write(0x101); // Explicit hide override.
        }
        return writer.Finish();
    }

    private void WriteNode(NwcNode node, bool hiddenAncestor)
    {
        var meshes = _meshes[node];
        bool hasMeshes = meshes.Any();
        bool isGroup = !hasMeshes || node.Children.Count != 0;
        Guid? guid = node.ElementGuid;
        if (node.IfcGlobalId is { } ifc)
        {
            if (!IfcGuid.TryFromIfcGuid(ifc, out Guid parsed))
                throw new ArgumentException("A native NWC element has an invalid IFC GlobalId.");
            guid = parsed;
        }
        int element = 0;
        if (guid is Guid value)
        {
            _guids.Add(value);
            element = _guids.Count;
        }

        int seq = ++_sequence;
        if (node.IsHidden) _hiddenNodes.Add(seq);
        var properties = node.Properties.Where(IsModelProperty).GroupBy(property => property.Class).ToArray();
        var id = node.Properties.FirstOrDefault(property => property.Class == "Revit" && property.Name == "Element ID");
        bool hasId = int.TryParse(id?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int elementId) && elementId > 0;
        WriteNodeHeader(isGroup, node.Name, node.Class, element, properties.Length + (hasId ? 1 : 0), node.IsHidden);
        foreach (var group in properties)
        {
            _hierarchy.Write(0);
            WritePropertyTab(group.Key, group);
            _propertyCount++;
        }

        if (hasId)
        {
            _hierarchy.Write(0);
            _properties.NewObject(66);
            _properties.WriteText(null);
            WriteName(_properties, "Element ID", "LcRevitId");
            _properties.Write(0x01000000);
            _properties.Align(8);
            _properties.Write((long)elementId);
            _propertyCount++;
        }

        if (isGroup)
        {
            _hierarchy.Write(node.Children.Count + (hasMeshes ? 1 : 0));
            if (hasMeshes)
            {
                seq = ++_sequence;
                WriteNodeHeader(false, node.Name, node.Class, 0, 0, hidden: false);
            }
        }

        bool hidden = hiddenAncestor || node.IsHidden;
        foreach (var mesh in meshes)
            WriteFragment(mesh, seq, hidden);
        foreach (var child in node.Children)
            WriteNode(child, hidden);
    }

    private void WriteNodeHeader(bool group, string name, string type, int element, int propertyCount, bool hidden)
    {
        _hierarchy.NewObject(group ? 53 : 22);
        _hierarchy.WriteText(name);
        WriteName(_hierarchy, name, type);
        _hierarchy.Write(hidden ? 1 : 0);
        _hierarchy.Write(element);
        _hierarchy.Write(propertyCount);
    }

    private void WritePropertyTab(string name, IEnumerable<NwcPropertyRecord> properties)
    {
        _properties.NewObject(86);
        _properties.WriteText(null);
        WriteName(_properties, name, name);
        _properties.Write(0x01000000);
        _properties.Write(properties.Count());
        foreach (var property in properties)
        {
            WriteName(_properties, property.Name, property.InternalName ?? property.Name);
            _properties.Write(4);
            _properties.WriteText(property.Value ?? property.IntValue?.ToString(CultureInfo.InvariantCulture)
                ?? property.DoubleValue?.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private void WriteFragment(NwcMesh mesh, int sequence, bool hidden)
    {
        if (mesh.Transform is { } transform && (transform.Rotation != Quaterniond.Identity || transform.Scale != 1))
            throw new ArgumentException("Native NWC output requires transforms with rotation and scale baked into the vertices.");
        Vector3d translation = (mesh.Transform?.Translation ?? Vector3d.Zero) * _feetPerUnit;
        if (!double.IsFinite(translation.X) || !double.IsFinite(translation.Y) || !double.IsFinite(translation.Z))
            throw new ArgumentException("A native NWC mesh has a nonfinite translation.");

        var (data, bounds) = NativeCacheGeometryWriter.WriteMesh(mesh, _feetPerUnit);
        double radius = (bounds.Max - bounds.Min).Length / 2;
        if (!float.IsFinite((float)radius))
            throw new ArgumentException("A native NWC mesh is too large for its fragment bounds.");
        _geometry.Write(data);
        _recordLengths.Add(data.Length);
        _fragments.NewObject(13);
        _fragments.WriteDouble(radius);
        _fragments.Write(mesh.LineIndices.Count > 0 ? mesh.LineIndices.Count / 2 : mesh.TriangleCount);
        _fragments.Write(hidden ? 6 : 4);
        _fragments.Write(0x2100);
        _fragments.Write(sequence);
        _fragments.NewObject(14);
        WriteVector(_fragments, translation);

        RgbaColor color = mesh.Material?.Color ?? new RgbaColor(0.7f, 0.7f, 0.7f);
        if (_appearances.TryGetValue(color, out int appearance))
        {
            _fragments.Write(1);
            _fragments.Write(appearance);
        }
        else
        {
            appearance = _fragments.NewObject(55);
            _appearances.Add(color, appearance);
            _fragments.Write(1);
            _fragments.NewObject(54);
            float[] values = [0.2f * color.R, 0.2f * color.G, 0.2f * color.B, color.R, color.G, color.B,
                0, 0, 0, 0, 0, 0, 0, 1 - color.A];
            foreach (float value in values)
            {
                if (!float.IsFinite(value) || value < 0 || value > 1)
                    throw new ArgumentException("Native NWC material channels must be finite and between 0 and 1.");
                _fragments.Write(value);
            }
        }

        _fragments.NewObject(93);
        foreach (Vector3d limit in new[] { bounds.Min, bounds.Max })
        {
            _fragments.Write((float)limit.X);
            _fragments.Write((float)limit.Y);
            _fragments.Write((float)limit.Z);
        }

        WriteVector(_fragments, translation);
        _fragments.Write((float)radius);
        _fragments.Write(_recordLengths.Count);
        _fragments.Write(_recordLengths.Count);
    }

    private NativeCacheSegment WritePartitionProps()
    {
        using var segment = new NativeCacheBinaryWriter();
        using var toc = new NativeCacheBinaryWriter();
        segment.Write(0);
        toc.Write(1);
        byte[] data = NativeCacheContainerWriter.Compress(_properties.Finish());
        segment.Write(data);
        toc.Write(data.Length);
        byte[] table = NativeCacheContainerWriter.Compress(toc.Finish());
        segment.Write(table);
        return new(PartitionProps, ((MemoryStream)segment.BaseStream).ToArray(), PrefixBytes: 4, TocBytes: table.Length);
    }

    private byte[] WriteGuids()
    {
        using var writer = new NativeCacheBinaryWriter();
        writer.Write(1);
        writer.Write(_guids.Count);
        foreach (Guid guid in _guids)
            writer.Write(guid.ToByteArray());
        return writer.Finish();
    }

    private static byte[] WriteSpatialHierarchy(int count)
    {
        using var writer = new NativeCacheBinaryWriter();
        WriteGroup(0, count);
        return writer.Finish();

        void WriteGroup(int start, int size)
        {
            if (size <= 32)
            {
                writer.Write(2);
                writer.Write(size);
                for (int i = 0; i < size; i++)
                {
                    writer.Write(1);
                    writer.Write(start + i);
                }
                return;
            }

            int range = 1 + (size - 1) / 7;
            writer.Write(5);
            writer.Write(1 + (size - 1) / range);
            for (int i = 0; i < size; i += range)
                WriteGroup(start + i, Math.Min(range, size - i));
        }
    }

    private static byte[] WriteSource(NwcFileInfo info, string sourceName)
    {
        using var writer = new NativeCacheBinaryWriter();
        writer.Write(7L);
        writer.WriteText(sourceName);
        writer.Write(-1);
        writer.Align(8);
        writer.Write(info.SourceTimestampUtc is DateTime utc ? new DateTimeOffset(utc.ToUniversalTime()).ToUnixTimeSeconds() : 0L);
        writer.Write(info.SourceByteSize ?? 0L);
        writer.WriteText(RevitLoader);
        writer.WriteText(WriterSource);
        return writer.Finish();
    }

    private static byte[] WritePartition(string sourceName)
    {
        using var writer = new NativeCacheBinaryWriter();
        writer.NewObject(32);
        writer.WriteText("");
        writer.WriteText(sourceName);
        WriteName(writer, "File", "LcOaPartition");
        foreach (int value in new[] { 0, 0, 1 })
            writer.Write(value);
        writer.NewObject(31);
        writer.WriteText(null);
        writer.WriteDouble(0);
        for (int i = 0; i < 14; i++)
            writer.WriteDouble(i < 6 ? 1 : 0);

        // A standalone cache partition omits the children count. The logical hierarchy is a separate stream.
        writer.Write(2);
        writer.Write(0);
        WriteVector(writer, Vector3d.Zero);
        writer.Write(0);
        writer.WriteText(RevitLoader);
        writer.Write(448);
        writer.Write(3); // feet
        writer.Write(1);
        WriteVector(writer, new(0, 0, 1));
        WriteVector(writer, Vector3d.Zero);
        writer.Write(0);
        writer.WriteText(null);
        writer.Write(1);
        writer.WriteText(sourceName);
        foreach (double value in new double[] { 0, 0, 0, 1, 1, 0, 0 })
            writer.WriteDouble(value);
        writer.Write(1);
        writer.Write(0);
        return writer.Finish();
    }

    private static byte[] WriteFileInfo(string name, byte[] identity)
    {
        using var writer = new NativeCacheBinaryWriter();
        ReadOnlySpan<byte> document = identity.AsSpan(0, 16), partition = identity.AsSpan(16, 16);
        writer.Write(-1);
        writer.Write(0);
        writer.Write(document);
        foreach (int value in new[] { 0, 1, 71 })
            writer.Write(value);
        writer.WriteText(Path.ChangeExtension(name, ".nwc"));
        writer.Write(0);
        writer.Write(partition);
        writer.Write(0);
        writer.WriteText("cache");
        foreach (int value in new[] { 0, 0, -1, -1, -1 })
            writer.Write(value);
        writer.Write(partition);
        writer.WriteText("cache");
        writer.Write(0);
        writer.Write(document);
        writer.Write(new byte[16]);
        return writer.Finish();
    }

    private static void WriteName(NativeCacheBinaryWriter writer, string display, string name)
    {
        writer.NewObject(52);
        writer.WriteText(display);
        writer.WriteText(name);
    }

    private static void WriteVector(NativeCacheBinaryWriter writer, Vector3d value)
    {
        writer.WriteDouble(value.X);
        writer.WriteDouble(value.Y);
        writer.WriteDouble(value.Z);
    }

    private static HashSet<NwcNode> GetNodes(NwcNode root)
    {
        var nodes = new HashSet<NwcNode>();
        var pending = new Stack<(NwcNode Node, int Depth)>();
        pending.Push((root, 1));
        while (pending.TryPop(out var item))
        {
            if (item.Depth >= 256 || !nodes.Add(item.Node))
                throw new ArgumentException("Native NWC output requires a tree with no repeated nodes and fewer than 256 levels.");
            foreach (var child in item.Node.Children)
                pending.Push((child, item.Depth + 1));
        }
        return nodes;
    }

    private static bool IsModelProperty(NwcPropertyRecord property) => property.Class != LegacyMeshLink;

    private static NativeCacheSegment Single(string name, byte[] data) => new(name, NativeCacheContainerWriter.Compress(data));

    public void Dispose()
    {
        _hierarchy.Dispose();
        _properties.Dispose();
        _fragments.Dispose();
        _geometry.Dispose();
    }
}
