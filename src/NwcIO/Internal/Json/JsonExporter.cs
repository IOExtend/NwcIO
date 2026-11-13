using System.Text.Json;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Json;

/// <summary>Writes a parsed <see cref="NwcDocument"/> to JSON.</summary>
public static class JsonExporter
{
    public static void Write(NwcDocument document, Stream output, NwcJsonExportOptions options)
    {
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Indented = options.Indented,
        });

        writer.WriteStartObject();

        writer.WriteStartObject("file");
        writer.WriteString("sourcePath", document.FileInfo.SourcePath);
        writer.WriteString("sourceTimestampUtc", document.FileInfo.SourceTimestampUtc?.ToString("O"));
        writer.WriteString("loaderPlugin", document.FileInfo.LoaderPlugin);
        writer.WriteStartArray("options");
        foreach (var option in document.FileInfo.Options)
        {
            writer.WriteStringValue(option);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteNumber("propertyRecordCount", document.PropertyRecords.Count);
        writer.WriteStartArray("records");
        foreach (var record in document.PropertyRecords)
        {
            writer.WriteStartObject();
            writer.WriteNumber("firstInt", record.FirstInt);
            writer.WriteNumber("tag", record.Tag);
            writer.WriteString("name", record.Name);
            writer.WriteString("class", record.Class);
            if (record.Value is not null)
            {
                writer.WriteNumber("valueType", record.ValueType);
                writer.WriteString("value", record.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteNumber("guidCount", document.Guids.Count);
        writer.WriteStartArray("guids");
        foreach (var guid in document.Guids)
        {
            writer.WriteStringValue(guid.ToString("D"));
        }

        writer.WriteEndArray();

        if (options.IncludeTransforms && document.Transforms.Count > 0)
        {
            writer.WriteNumber("transformCount", document.Transforms.Count);
            writer.WriteStartArray("transforms");
            foreach (var transform in document.Transforms)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", transform.Id);

                writer.WriteStartObject("translation");
                WriteSafeNumber(writer, "x", transform.Translation.X);
                WriteSafeNumber(writer, "y", transform.Translation.Y);
                WriteSafeNumber(writer, "z", transform.Translation.Z);
                writer.WriteEndObject();

                writer.WriteStartObject("rotation");
                WriteSafeNumber(writer, "x", transform.Rotation.X);
                WriteSafeNumber(writer, "y", transform.Rotation.Y);
                WriteSafeNumber(writer, "z", transform.Rotation.Z);
                WriteSafeNumber(writer, "w", transform.Rotation.W);
                writer.WriteEndObject();

                // Translation and rotation alone cannot express a scaled or mirrored pose; omitted at 1 so other output is unchanged.
                if (transform.Scale != 1.0)
                {
                    WriteSafeNumber(writer, "scale", transform.Scale);
                }

                if (transform.BoundingBox is { } bbox)
                {
                    writer.WriteStartObject("boundingBox");
                    writer.WriteStartArray("min");
                    WriteSafeNumberValue(writer, bbox.Min.X);
                    WriteSafeNumberValue(writer, bbox.Min.Y);
                    WriteSafeNumberValue(writer, bbox.Min.Z);
                    writer.WriteEndArray();
                    writer.WriteStartArray("max");
                    WriteSafeNumberValue(writer, bbox.Max.X);
                    WriteSafeNumberValue(writer, bbox.Max.Y);
                    WriteSafeNumberValue(writer, bbox.Max.Z);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteStartArray("matrix");
                foreach (double m in transform.ToMatrix4x4())
                {
                    WriteSafeNumberValue(writer, m);
                }

                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (options.IncludeSceneTree && document.RootNode is not null)
        {
            writer.WriteStartObject("sceneTree");
            WriteNodeRecursive(writer, document.RootNode);
            writer.WriteEndObject();
        }

        writer.WriteNumber("meshCount", document.Meshes.Count);
        if (options.IncludeMeshes && document.Meshes.Count > 0)
        {
            writer.WriteStartArray("meshes");
            foreach (var mesh in document.Meshes)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", mesh.Id);
                writer.WriteString("name", mesh.Name);
                writer.WriteNumber("vertexCount", mesh.Vertices.Count);
                writer.WriteNumber("triangleCount", mesh.TriangleCount);
                if (mesh.Material is not null)
                {
                    writer.WriteString("material", mesh.Material.Name);
                }

                if (mesh.BoundingBox is { } bbox)
                {
                    writer.WriteStartObject("boundingBox");
                    writer.WriteStartArray("min");
                    WriteSafeNumberValue(writer, bbox.Min.X);
                    WriteSafeNumberValue(writer, bbox.Min.Y);
                    WriteSafeNumberValue(writer, bbox.Min.Z);
                    writer.WriteEndArray();
                    writer.WriteStartArray("max");
                    WriteSafeNumberValue(writer, bbox.Max.X);
                    WriteSafeNumberValue(writer, bbox.Max.Y);
                    WriteSafeNumberValue(writer, bbox.Max.Z);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteNumber("materialCount", document.Materials.Count);
        if (options.IncludeMaterials && document.Materials.Count > 0)
        {
            writer.WriteStartArray("materials");
            foreach (var mat in document.Materials)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", mat.Id);
                writer.WriteString("name", mat.Name);
                writer.WriteString("color", mat.Color.ToHex(includeAlpha: true));
                WriteSafeNumber(writer, "metallic", mat.Metallic);
                WriteSafeNumber(writer, "roughness", mat.Roughness);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (document.XRefs is not null)
        {
            writer.WriteStartObject("xrefs");
            writer.WriteString("type", document.XRefs.Type);
            writer.WriteNumber("version", document.XRefs.Version);
            writer.WriteString("savedFilename", document.XRefs.SavedFilename);
            writer.WriteStartArray("references");
            foreach (var xref in document.XRefs.References)
            {
                writer.WriteStartObject();
                writer.WriteString("originalPath", xref.OriginalPath);
                writer.WriteString("remappedPath", xref.RemappedPath);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (document.DbCache is not null)
        {
            writer.WriteStartObject("dbCache");
            writer.WriteString("databaseName", document.DbCache.DatabaseName);
            writer.WriteNumber("entryCount", document.DbCache.Entries.Count);
            writer.WriteStartArray("entries");
            foreach (var entry in document.DbCache.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", entry.Name);
                writer.WriteNumber("flags", entry.Flags);
                writer.WriteNumber("offset", entry.Offset);
                writer.WriteNumber("length", entry.Length);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (options.IncludeSavedViewpoints && document.SavedViewpoints.Count > 0)
        {
            writer.WriteStartArray("savedViewpoints");
            foreach (var vp in document.SavedViewpoints)
            {
                writer.WriteStartObject();
                writer.WriteString("id", vp.Id.ToString("D"));
                writer.WriteString("name", vp.Name);

                writer.WriteStartObject("camera");
                writer.WriteString("projection", vp.Camera.Projection.ToString());
                WriteSafeNumber(writer, "fieldOfView", vp.Camera.FieldOfView);
                WriteSafeNumber(writer, "focalDistance", vp.Camera.FocalDistance);
                WriteSafeNumber(writer, "aspectRatio", vp.Camera.AspectRatio);
                WriteSafeNumber(writer, "nearDistance", vp.Camera.NearDistance);
                WriteSafeNumber(writer, "farDistance", vp.Camera.FarDistance);

                writer.WriteStartObject("position");
                WriteSafeNumber(writer, "x", vp.Camera.Position.X);
                WriteSafeNumber(writer, "y", vp.Camera.Position.Y);
                WriteSafeNumber(writer, "z", vp.Camera.Position.Z);
                writer.WriteEndObject();

                writer.WriteStartObject("rotation");
                WriteSafeNumber(writer, "x", vp.Camera.Rotation.X);
                WriteSafeNumber(writer, "y", vp.Camera.Rotation.Y);
                WriteSafeNumber(writer, "z", vp.Camera.Rotation.Z);
                WriteSafeNumber(writer, "w", vp.Camera.Rotation.W);
                writer.WriteEndObject();

                writer.WriteStartObject("direction");
                WriteSafeNumber(writer, "x", vp.Camera.Direction.X);
                WriteSafeNumber(writer, "y", vp.Camera.Direction.Y);
                WriteSafeNumber(writer, "z", vp.Camera.Direction.Z);
                writer.WriteEndObject();

                writer.WriteStartObject("target");
                WriteSafeNumber(writer, "x", vp.Camera.Target.X);
                WriteSafeNumber(writer, "y", vp.Camera.Target.Y);
                WriteSafeNumber(writer, "z", vp.Camera.Target.Z);
                writer.WriteEndObject();

                writer.WriteEndObject(); // camera

                if (vp.Comments.Count > 0)
                {
                    writer.WriteStartArray("comments");
                    foreach (var c in vp.Comments)
                    {
                        writer.WriteStringValue(c);
                    }
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        if (options.IncludeStreamCatalog)
        {
            WriteStreamCatalog(writer, document);
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    public static void WriteFile(NwcDocument document, string path, NwcJsonExportOptions options)
    {
        using var stream = File.Create(path);
        Write(document, stream, options);
    }

    private static void WriteNodeRecursive(Utf8JsonWriter writer, NwcNode node)
    {
        writer.WriteNumber("id", node.Id);
        writer.WriteString("name", node.Name);
        writer.WriteString("class", node.Class);
        if (node.Value is not null)
        {
            writer.WriteString("value", node.Value);
        }

        if (node.ElementGuid is { } guid)
        {
            writer.WriteString("guid", guid.ToString("D"));
        }

        if (node.Transform is { } transform)
        {
            writer.WriteStartObject("transform");
            writer.WriteStartArray("translation");
            WriteSafeNumberValue(writer, transform.Translation.X);
            WriteSafeNumberValue(writer, transform.Translation.Y);
            WriteSafeNumberValue(writer, transform.Translation.Z);
            writer.WriteEndArray();
            writer.WriteStartArray("rotation");
            WriteSafeNumberValue(writer, transform.Rotation.X);
            WriteSafeNumberValue(writer, transform.Rotation.Y);
            WriteSafeNumberValue(writer, transform.Rotation.Z);
            WriteSafeNumberValue(writer, transform.Rotation.W);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (node.Properties.Count > 0)
        {
            writer.WriteStartArray("properties");
            foreach (var prop in node.Properties)
            {
                writer.WriteStartObject();
                writer.WriteString("name", prop.Name);
                writer.WriteString("class", prop.Class);
                if (prop.Value is not null)
                {
                    writer.WriteString("value", prop.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        if (node.Children.Count > 0)
        {
            writer.WriteStartArray("children");
            foreach (var child in node.Children)
            {
                writer.WriteStartObject();
                WriteNodeRecursive(writer, child);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }
    }

    private static void WriteStreamCatalog(Utf8JsonWriter writer, NwcDocument document)
    {
        var container = document.Container;
        writer.WriteStartObject("container");

        writer.WriteStartArray("sections");
        foreach (var section in container.Sections)
        {
            writer.WriteStartObject();
            writer.WriteNumber("fileOffset", section.FileOffset);
            writer.WriteString("format", section.Format);
            writer.WriteStartArray("tokens");
            foreach (var token in section.Tokens)
            {
                writer.WriteStringValue(token);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteNumber("streamCount", container.Streams.Count);
        writer.WriteStartArray("streams");
        foreach (var stream in container.Streams)
        {
            writer.WriteStartObject();
            writer.WriteNumber("index", stream.Index);
            writer.WriteNumber("fileOffset", stream.FileOffset);
            writer.WriteNumber("compressedLength", stream.CompressedLength);
            writer.WriteNumber("decompressedLength", stream.Data.Length);
            if (stream.GapBefore > 0)
            {
                writer.WriteNumber("gapBefore", stream.GapBefore);
            }

            writer.WriteString("kind", stream.Kind.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        if (container.TrailingGap > 0)
        {
            writer.WriteNumber("trailingGap", container.TrailingGap);
        }

        writer.WriteEndObject();
    }

    private static void WriteSafeNumber(Utf8JsonWriter writer, string propertyName, double value)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumber(propertyName, value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteSafeNumberValue(Utf8JsonWriter writer, double value)
    {
        if (double.IsFinite(value))
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
