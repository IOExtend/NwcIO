using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NwcIO.Model;

namespace NwcIO.Internal.Container;

/// <summary>
/// Serializes high-level <see cref="NwcDocument"/> components into binary byte streams
/// matching the reverse-engineered Navisworks partition stream layouts.
/// </summary>
internal static class NwcStreamSerializer
{
    public static List<byte[]> SerializeDocument(NwcDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var streams = new List<byte[]>();

        // 1. PartitionHeader stream
        streams.Add(BuildPartitionHeaderStream(doc.FileInfo));

        // 2. SceneTree & Properties stream
        var sceneTree = BuildSceneTreeStream(doc.PropertyRecords, doc.RootNode);
        if (sceneTree.Length > 0)
        {
            streams.Add(sceneTree);
        }

        // 3. Transforms stream
        if (doc.Transforms.Count > 0)
        {
            streams.Add(BuildTransformsStream(doc.Transforms));
        }

        // 4. Guids stream
        if (doc.Guids.Count > 0)
        {
            streams.Add(BuildGuidsStream(doc.Guids));
        }

        // 5 & 6. Geometry and TOC streams
        if (doc.Meshes.Count > 0)
        {
            BuildGeometryAndTocStreams(doc.Meshes, out var geomData, out var tocData);
            streams.Add(geomData);
            streams.Add(tocData);
        }

        // 7. Saved viewpoints stream
        if (doc.SavedViewpoints.Count > 0)
        {
            streams.Add(BuildSavedViewsStream(doc.SavedViewpoints));
        }

        // 8. Selection Sets and Search Sets stream
        if (doc.SelectionSets.Count > 0)
        {
            streams.Add(BuildSelectionSetsStream(doc.SelectionSets));
        }

        // 9. 4D TimeLiner schedule stream
        if (doc.TimeLiner is not null && doc.TimeLiner.Tasks.Count > 0)
        {
            streams.Add(BuildTimeLinerStream(doc.TimeLiner));
        }

        return streams;
    }

    public static byte[] BuildSelectionSetsStream(IReadOnlyList<Model.Sets.NwcSetItem> sets)
    {
        using var xmlMs = new MemoryStream();
        Model.Sets.NwcXmlSetSerializer.Write(sets, xmlMs);
        byte[] xmlBytes = xmlMs.ToArray();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(100); // tag category
        w.Write(105); // tag SelectionSets
        w.Write(sets.Count);
        w.Write(xmlBytes.Length);
        w.Write(xmlBytes);

        return ms.ToArray();
    }

    public static byte[] BuildTimeLinerStream(Model.TimeLiner.NwcTimeLinerSchedule schedule)
    {
        using var xmlMs = new MemoryStream();
        Model.TimeLiner.NwcTimeLinerXmlSerializer.Write(schedule, xmlMs);
        var xmlBytes = xmlMs.ToArray();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(100); // tag category
        w.Write(106); // tag TimeLiner
        w.Write(schedule.Tasks.Count);
        w.Write(xmlBytes.Length);
        w.Write(xmlBytes);

        return ms.ToArray();
    }

    public static byte[] BuildPartitionHeaderStream(NwcFileInfo fileInfo)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // Marker [int64 7]
        w.Write(7L);

        // Length-prefixed source path (int32 len, utf8 bytes)
        string path = !string.IsNullOrWhiteSpace(fileInfo.SourcePath) ? fileInfo.SourcePath : "Model.rvt";
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        w.Write(pathBytes.Length);
        w.Write(pathBytes);

        // [int32 -1]
        w.Write(-1);

        // [int64 timestamp in unix seconds]
        // A Local value (DateTime.Now, File.GetLastWriteTime) is converted; an Unspecified one is taken as UTC
        long timestamp = fileInfo.SourceTimestampUtc is { } sourceTimestamp
            ? new DateTimeOffset(sourceTimestamp.Kind == DateTimeKind.Local
                ? sourceTimestamp.ToUniversalTime()
                : DateTime.SpecifyKind(sourceTimestamp, DateTimeKind.Utc)).ToUnixTimeSeconds()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        w.Write(timestamp);

        // [int64 source file size]
        w.Write(0L);

        // Length-prefixed loader plugin
        string plugin = !string.IsNullOrWhiteSpace(fileInfo.LoaderPlugin) ? fileInfo.LoaderPlugin : "LcOpRevitExport";
        byte[] pluginBytes = Encoding.UTF8.GetBytes(plugin);
        w.Write(pluginBytes.Length);
        w.Write(pluginBytes);

        // Options
        if (fileInfo.Options.Count > 0)
        {
            foreach (var opt in fileInfo.Options)
            {
                byte[] optBytes = Encoding.UTF8.GetBytes(opt);
                w.Write(optBytes.Length);
                w.Write(optBytes);
            }
        }

        return ms.ToArray();
    }

    public static byte[] BuildSceneTreeStream(IReadOnlyList<NwcPropertyRecord> records, NwcNode? rootNode)
    {
        if (records.Count == 0 && rootNode is null)
        {
            return Array.Empty<byte>();
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // SceneTree signature: [100][86]
        w.Write(100);
        w.Write(86);

        if (records.Count > 0)
        {
            foreach (var r in records)
            {
                if (r.Tag == 53)
                {
                    w.Write(r.FirstInt);
                    w.Write(53);
                    WritePaddedString(w, r.Name);
                }
                else if (r.Tag == 52)
                {
                    w.Write(r.FirstInt);
                    w.Write(52);
                    WritePaddedString(w, r.Name);
                    WritePaddedString(w, r.Class);
                    w.Write(r.ValueType);

                    switch (r.ValueType)
                    {
                        case 4:
                            WritePaddedString(w, r.Value ?? string.Empty);
                            break;
                        case 0:
                        case 1:
                        case 2:
                        case 3:
                            w.Write(int.TryParse(r.Value, out var iv) ? iv : 0);
                            break;
                        case 5:
                        case 7:
                            w.Write(double.TryParse(r.Value, CultureInfo.InvariantCulture, out var dv) ? dv : 0.0);
                            break;
                        case 8:
                            w.Write(0);
                            w.Write(0);
                            WritePaddedString(w, r.Value ?? string.Empty);
                            WritePaddedString(w, string.Empty);
                            break;
                        default:
                            w.Write(0);
                            break;
                    }
                }
            }
        }
        else if (rootNode is not null)
        {
            FlattenNode(w, rootNode);
        }

        return ms.ToArray();
    }

    private static void FlattenNode(BinaryWriter w, NwcNode node)
    {
        // Emit Tag 53 (node name)
        w.Write(node.Id);
        w.Write(53);
        WritePaddedString(w, node.Name);

        // Emit Tag 52 (identity record)
        w.Write(node.Id);
        w.Write(52);
        WritePaddedString(w, node.Name);
        WritePaddedString(w, !string.IsNullOrEmpty(node.Class) ? node.Class : "LcRevitElement");
        w.Write(0); // valueType 0
        w.Write(node.Id);

        // Emit properties if any
        foreach (var prop in node.Properties)
        {
            w.Write(node.Id);
            w.Write(52);
            WritePaddedString(w, prop.Name);
            WritePaddedString(w, prop.Class);
            w.Write(4); // string
            WritePaddedString(w, prop.Value ?? string.Empty);
        }

        foreach (var child in node.Children)
        {
            FlattenNode(w, child);
        }
    }

    public static byte[] BuildTransformsStream(IReadOnlyList<NwcTransform> transforms)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(transforms.Count);

        for (int i = 0; i < transforms.Count; i++)
        {
            var t = transforms[i];
            int recId = (i == 0) ? 100 : (t.Id != 0 ? t.Id : 100 + i);
            w.Write(recId);
            w.Write(13); // TagTransform

            w.Write(1);  // inner id
            w.Write(15); // TagPose
            w.Write(t.Translation.X);
            w.Write(t.Translation.Y);
            w.Write(t.Translation.Z);
            w.Write(t.Rotation.X);
            w.Write(t.Rotation.Y);
            w.Write(t.Rotation.Z);
            w.Write(t.Rotation.W);

            if (t.BoundingBox is { IsEmpty: false } bbox)
            {
                w.Write(2);  // inner id
                w.Write(93); // TagBoundingBox
                w.Write((float)bbox.Min.X);
                w.Write((float)bbox.Min.Y);
                w.Write((float)bbox.Min.Z);
                w.Write((float)bbox.Max.X);
                w.Write((float)bbox.Max.Y);
                w.Write((float)bbox.Max.Z);
            }
        }

        return ms.ToArray();
    }

    public static byte[] BuildGuidsStream(IReadOnlyList<Guid> guids)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write(0);
        w.Write(guids.Count);
        foreach (var g in guids)
        {
            w.Write(g.ToByteArray());
        }

        return ms.ToArray();
    }

    public static void BuildGeometryAndTocStreams(
        IReadOnlyList<NwcMesh> meshes,
        out byte[] geomData,
        out byte[] tocData)
    {
        using var geomMs = new MemoryStream();
        using var geomWriter = new BinaryWriter(geomMs);
        var recordLengths = new int[meshes.Count];

        for (int r = 0; r < meshes.Count; r++)
        {
            var mesh = meshes[r];
            var verts = mesh.Vertices;
            int vCount = verts.Count;
            int rLen = 24 + vCount * 12;
            recordLengths[r] = rLen;

            geomWriter.Write(mesh.Id != 0 ? mesh.Id : r); // recId
            geomWriter.Write(94);                         // recTag (94)
            geomWriter.Write(0);                          // flags
            geomWriter.Write(60);                         // subTag (60)
            geomWriter.Write(vCount);                     // p1
            geomWriter.Write(vCount * 3);                 // p2

            for (int v = 0; v < vCount; v++)
            {
                geomWriter.Write((float)verts[v].X);
                geomWriter.Write((float)verts[v].Y);
                geomWriter.Write((float)verts[v].Z);
            }
        }

        geomData = geomMs.ToArray();

        using var tocMs = new MemoryStream();
        using var tocWriter = new BinaryWriter(tocMs);
        tocWriter.Write(meshes.Count);    // recordCount
        tocWriter.Write(1);               // chunkCount = 1
        tocWriter.Write(geomData.Length); // chunkSizes[0]
        for (int r = 0; r < meshes.Count; r++)
        {
            tocWriter.Write(recordLengths[r]);
        }

        tocData = tocMs.ToArray();
    }

    public static byte[] BuildSavedViewsStream(IReadOnlyList<NwcSavedViewpoint> viewpoints)
    {
        using var vpMs = new MemoryStream();
        using var vpWriter = new BinaryWriter(vpMs);

        for (int i = 0; i < viewpoints.Count; i++)
        {
            var vp = viewpoints[i];
            var cam = vp.Camera ?? new NwcCamera();

            // Name preamble before tag 517 so ViewpointParser.ExtractViewName finds it
            string viewName = !string.IsNullOrWhiteSpace(vp.Name) ? vp.Name : $"Viewpoint {i + 1}";
            byte[] nameBytes = Encoding.UTF8.GetBytes(viewName);
            vpWriter.Write(nameBytes.Length);
            vpWriter.Write(nameBytes);
            int pad = (4 - (nameBytes.Length & 3)) & 3;
            for (int p = 0; p < pad; p++)
            {
                vpWriter.Write((byte)0);
            }

            // Tag 517 record
            vpWriter.Write(517); // tag
            vpWriter.Write(1);   // subtag
            vpWriter.Write(0);   // flags
            vpWriter.Write(cam.Position.X);
            vpWriter.Write(cam.Position.Y);
            vpWriter.Write(cam.Position.Z);

            var rot = cam.Rotation.Normalized();

            vpWriter.Write(rot.X);
            vpWriter.Write(rot.Y);
            vpWriter.Write(rot.Z);
            vpWriter.Write(rot.W);
            vpWriter.Write(cam.AspectRatio > 0 ? cam.AspectRatio : 1.7777777777777777);
            vpWriter.Write(cam.FocalDistance > 0 ? cam.FocalDistance : 10.0);
            vpWriter.Write(cam.FieldOfView > 0 ? cam.FieldOfView : 60.0);
            vpWriter.Write(cam.NearDistance > 0 ? cam.NearDistance : 0.1);

            // Comments
            if (vp.Comments.Count > 0)
            {
                foreach (var c in vp.Comments)
                {
                    byte[] cBytes = Encoding.UTF8.GetBytes(c);
                    vpWriter.Write(cBytes.Length);
                    vpWriter.Write(cBytes);
                    int cPad = (4 - (cBytes.Length & 3)) & 3;
                    for (int p = 0; p < cPad; p++)
                    {
                        vpWriter.Write((byte)0);
                    }
                }
            }
        }

        // Pad stream to at least 120 bytes so StreamClassifier.IsSavedViewsStream runs
        while (vpMs.Length < 128)
        {
            vpWriter.Write(0);
        }

        return vpMs.ToArray();
    }

    private static void WritePaddedString(BinaryWriter w, string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        w.Write(bytes.Length);
        w.Write(bytes);
        int pad = (4 - (bytes.Length & 3)) & 3;
        for (int i = 0; i < pad; i++)
        {
            w.Write((byte)0);
        }
    }
}
