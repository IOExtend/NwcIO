using System.Buffers.Binary;
using System.Text.Json;
using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Exports 3D meshes, scene hierarchy, materials and PBR surface parameters
/// to standardized glTF 2.0 Binary container format (.glb).
/// </summary>
public static class GlbExporter
{
    private const uint GlbHeaderMagic = 0x46546C67; // "glTF"
    private const uint GlbVersion = 2;
    private const uint ChunkTypeJson = 0x4E4F534A; // "JSON"
    private const uint ChunkTypeBin = 0x004E4942;  // "BIN\0"

    private const int TargetArrayBuffer = 34962;        // Vertex attributes
    private const int TargetElementArrayBuffer = 34963; // Indices

    private const int ComponentTypeFloat = 5126;
    private const int ComponentTypeUnsignedShort = 5123;
    private const int ComponentTypeUnsignedInt = 5125;

    public static void WriteFile(
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcMaterial> materials,
        string path,
        bool worldCoordinates = true,
        string? generatorName = null,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(path);

        using var fs = File.Create(path);
        Write(meshes, materials, fs, worldCoordinates, generatorName, viewpoints);
    }

    public static void Write(
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcMaterial> materials,
        Stream output,
        bool worldCoordinates = true,
        string? generatorName = null,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(output);

        // 1. Pack binary buffer (positions, normals, indices)
        using var binMs = new MemoryStream();
        var bufferViews = new List<BufferViewDef>();
        var accessors = new List<AccessorDef>();
        var gltfMeshes = new List<MeshDef>();

        // Material indexing. Meshes may reference materials the caller did not list
        // (for example an assembly material table deduped by name), so the table is
        // grown on demand instead of falling back to an arbitrary index 0.
        var materialTable = new List<NwcMaterial>(materials);
        var matIndexMap = new Dictionary<NwcMaterial, int>();
        for (int i = 0; i < materialTable.Count; i++)
        {
            matIndexMap[materialTable[i]] = i;
        }

        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = meshes[m];
            var vertices = worldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
            if (vertices.Count < 3 || mesh.Indices.Count < 3)
            {
                continue;
            }

            int vCount = vertices.Count;

            if (vertices.Any(position =>
                !float.IsFinite((float)position.X) ||
                !float.IsFinite((float)position.Y) ||
                !float.IsFinite((float)position.Z)))
            {
                continue;
            }

            // Keep only complete triangles that reference existing vertices.
            var faces = worldCoordinates ? mesh.GetWorldIndices() : mesh.Indices;
            var indices = new List<int>();
            for (int i = 0; i <= faces.Count - 3; i += 3)
            {
                int i0 = faces[i];
                int i1 = faces[i + 1];
                int i2 = faces[i + 2];
                if (i0 < 0 || i0 >= vCount || i1 < 0 || i1 >= vCount || i2 < 0 || i2 >= vCount)
                {
                    continue;
                }

                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);
            }

            if (indices.Count == 0)
            {
                continue;
            }

            // Compute vertex normals
            var normals = ComputeNormals(vertices, indices);

            // A. Vertex Positions
            AlignStream(binMs, 4);
            int posOffset = (int)binMs.Position;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            var posSpan = new byte[vCount * 12];
            for (int v = 0; v < vCount; v++)
            {
                var pt = vertices[v];
                BitConverter.TryWriteBytes(posSpan.AsSpan(v * 12), (float)pt.X);
                BitConverter.TryWriteBytes(posSpan.AsSpan(v * 12 + 4), (float)pt.Y);
                BitConverter.TryWriteBytes(posSpan.AsSpan(v * 12 + 8), (float)pt.Z);

                if (double.IsFinite(pt.X) && double.IsFinite(pt.Y) && double.IsFinite(pt.Z))
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Z < minZ) minZ = pt.Z;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y > maxY) maxY = pt.Y;
                    if (pt.Z > maxZ) maxZ = pt.Z;
                }
            }

            if (!double.IsFinite(minX))
            {
                minX = 0; minY = 0; minZ = 0;
                maxX = 0; maxY = 0; maxZ = 0;
            }

            binMs.Write(posSpan);
            int posViewIdx = bufferViews.Count;
            bufferViews.Add(new BufferViewDef(posOffset, posSpan.Length, TargetArrayBuffer));

            int posAccessorIdx = accessors.Count;
            accessors.Add(new AccessorDef(
                posViewIdx,
                0,
                ComponentTypeFloat,
                vCount,
                "VEC3",
                [(float)minX, (float)minY, (float)minZ],
                [(float)maxX, (float)maxY, (float)maxZ]));

            // B. Normals
            AlignStream(binMs, 4);
            int normOffset = (int)binMs.Position;
            var normSpan = new byte[vCount * 12];
            for (int v = 0; v < vCount; v++)
            {
                var n = normals[v];
                BitConverter.TryWriteBytes(normSpan.AsSpan(v * 12), n.X);
                BitConverter.TryWriteBytes(normSpan.AsSpan(v * 12 + 4), n.Y);
                BitConverter.TryWriteBytes(normSpan.AsSpan(v * 12 + 8), n.Z);
            }

            binMs.Write(normSpan);
            int normViewIdx = bufferViews.Count;
            bufferViews.Add(new BufferViewDef(normOffset, normSpan.Length, TargetArrayBuffer));

            int normAccessorIdx = accessors.Count;
            accessors.Add(new AccessorDef(
                normViewIdx,
                0,
                ComponentTypeFloat,
                vCount,
                "VEC3",
                null,
                null));

            // C. Indices
            AlignStream(binMs, 4);
            int idxOffset = (int)binMs.Position;
            int idxCount = indices.Count;
            bool useShort = vCount <= 65535;

            byte[] idxSpan;
            if (useShort)
            {
                idxSpan = new byte[idxCount * 2];
                for (int i = 0; i < idxCount; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(idxSpan.AsSpan(i * 2), (ushort)indices[i]);
                }
            }
            else
            {
                idxSpan = new byte[idxCount * 4];
                for (int i = 0; i < idxCount; i++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(idxSpan.AsSpan(i * 4), (uint)indices[i]);
                }
            }

            binMs.Write(idxSpan);
            int idxViewIdx = bufferViews.Count;
            bufferViews.Add(new BufferViewDef(idxOffset, idxSpan.Length, TargetElementArrayBuffer));

            int idxAccessorIdx = accessors.Count;
            accessors.Add(new AccessorDef(
                idxViewIdx,
                0,
                useShort ? ComponentTypeUnsignedShort : ComponentTypeUnsignedInt,
                idxCount,
                "SCALAR",
                null,
                null));

            // Material assignment
            int? matIdx = null;
            if (mesh.Material is not null)
            {
                if (!matIndexMap.TryGetValue(mesh.Material, out int foundIdx))
                {
                    foundIdx = materialTable.Count;
                    materialTable.Add(mesh.Material);
                    matIndexMap[mesh.Material] = foundIdx;
                }

                matIdx = foundIdx;
            }

            gltfMeshes.Add(new MeshDef(
                mesh.Name,
                posAccessorIdx,
                normAccessorIdx,
                idxAccessorIdx,
                matIdx));
        }

        // Align BIN chunk to 4 bytes
        AlignStream(binMs, 4);
        byte[] binData = binMs.ToArray();

        // 2. Build JSON chunk
        byte[] jsonData = BuildJsonChunk(
            materialTable,
            gltfMeshes,
            accessors,
            bufferViews,
            binData.Length,
            generatorName ?? "NwcIO glTF 2.0 Exporter",
            viewpoints);

        // Align JSON chunk to 4 bytes with 0x20 spaces as per glTF spec
        int jsonPadding = (4 - (jsonData.Length % 4)) % 4;
        int jsonChunkLength = jsonData.Length + jsonPadding;

        // 3. Write binary GLB container
        int totalLength = 12 + (8 + jsonChunkLength) + (binData.Length > 0 ? 8 + binData.Length : 0);

        using var bw = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

        // Header (12 bytes)
        bw.Write(GlbHeaderMagic);
        bw.Write(GlbVersion);
        bw.Write((uint)totalLength);

        // Chunk 0: JSON
        bw.Write((uint)jsonChunkLength);
        bw.Write(ChunkTypeJson);
        bw.Write(jsonData);
        for (int p = 0; p < jsonPadding; p++)
        {
            bw.Write((byte)0x20); // space padding
        }

        // Chunk 1: BIN
        if (binData.Length > 0)
        {
            bw.Write((uint)binData.Length);
            bw.Write(ChunkTypeBin);
            bw.Write(binData);
        }

        bw.Flush();
    }

    private static byte[] BuildJsonChunk(
        List<NwcMaterial> materials,
        List<MeshDef> meshes,
        List<AccessorDef> accessors,
        List<BufferViewDef> bufferViews,
        int binByteLength,
        string generator,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            // asset
            writer.WriteStartObject("asset");
            writer.WriteString("version", "2.0");
            writer.WriteString("generator", generator);
            writer.WriteEndObject();

            // scene
            writer.WriteNumber("scene", 0);

            // glTF has no value for a non-finite number, so a camera that can't be placed is left out
            var cameras = viewpoints?.Where(vp => IsFinite(vp.Camera.Position) && IsFinite(vp.Camera.Rotation)).ToList();
            int camCount = cameras?.Count ?? 0;
            int totalNodes = meshes.Count + camCount;

            // scenes
            writer.WriteStartArray("scenes");
            writer.WriteStartObject();
            if (totalNodes > 0) writer.WriteStartArray("nodes");
            for (int i = 0; i < totalNodes; i++)
            {
                writer.WriteNumberValue(i);
            }

            if (totalNodes > 0) writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            // nodes
            if (totalNodes > 0) writer.WriteStartArray("nodes");
            for (int i = 0; i < meshes.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("name", meshes[i].Name);
                writer.WriteNumber("mesh", i);
                writer.WriteEndObject();
            }

            if (cameras is not null)
            {
                for (int i = 0; i < cameras.Count; i++)
                {
                    var vp = cameras[i];
                    var cam = vp.Camera;
                    writer.WriteStartObject();
                    writer.WriteString("name", vp.Name);
                    writer.WriteNumber("camera", i);

                    writer.WriteStartArray("translation");
                    writer.WriteNumberValue(cam.Position.X);
                    writer.WriteNumberValue(cam.Position.Y);
                    writer.WriteNumberValue(cam.Position.Z);
                    writer.WriteEndArray();

                    writer.WriteStartArray("rotation");
                    writer.WriteNumberValue(cam.Rotation.X);
                    writer.WriteNumberValue(cam.Rotation.Y);
                    writer.WriteNumberValue(cam.Rotation.Z);
                    writer.WriteNumberValue(cam.Rotation.W);
                    writer.WriteEndArray();

                    writer.WriteEndObject();
                }
            }

            if (totalNodes > 0) writer.WriteEndArray();

            // cameras
            if (cameras is not null && cameras.Count > 0)
            {
                writer.WriteStartArray("cameras");
                for (int i = 0; i < cameras.Count; i++)
                {
                    var vp = cameras[i];
                    var cam = vp.Camera;
                    writer.WriteStartObject();
                    writer.WriteString("name", vp.Name);

                    if (cam.Projection == NwcCameraProjection.Orthographic)
                    {
                        writer.WriteString("type", "orthographic");
                        writer.WriteStartObject("orthographic");
                        double xmag = Math.Max(1.0, PositiveOr(cam.FocalDistance * 0.5, 1.0));
                        double ymag = xmag / PositiveOr(cam.AspectRatio, 1.0);
                        writer.WriteNumber("xmag", xmag);
                        writer.WriteNumber("ymag", PositiveOr(ymag, xmag));
                        writer.WriteNumber("zfar", PositiveOr(cam.FarDistance, 10000.0));
                        writer.WriteNumber("znear", PositiveOr(cam.NearDistance, 0.1));
                        writer.WriteEndObject();
                    }
                    else
                    {
                        writer.WriteString("type", "perspective");
                        writer.WriteStartObject("perspective");
                        writer.WriteNumber("aspectRatio", PositiveOr(cam.AspectRatio, 1.0));
                        writer.WriteNumber("yfov", PositiveOr(cam.FieldOfView * (Math.PI / 180.0), 1.04719755));
                        writer.WriteNumber("zfar", PositiveOr(cam.FarDistance, 10000.0));
                        writer.WriteNumber("znear", PositiveOr(cam.NearDistance, 0.1));
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            // meshes
            if (meshes.Count > 0) writer.WriteStartArray("meshes");
            for (int i = 0; i < meshes.Count; i++)
            {
                var m = meshes[i];
                writer.WriteStartObject();
                writer.WriteString("name", m.Name);
                writer.WriteStartArray("primitives");
                writer.WriteStartObject();

                writer.WriteStartObject("attributes");
                writer.WriteNumber("POSITION", m.PositionAccessor);
                writer.WriteNumber("NORMAL", m.NormalAccessor);
                writer.WriteEndObject();

                writer.WriteNumber("indices", m.IndicesAccessor);
                if (m.MaterialIndex is { } matIdx)
                {
                    writer.WriteNumber("material", matIdx);
                }

                writer.WriteNumber("mode", 4); // TRIANGLES
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            if (meshes.Count > 0) writer.WriteEndArray();

            // materials
            if (materials.Count > 0) writer.WriteStartArray("materials");
            for (int i = 0; i < materials.Count; i++)
            {
                var mat = materials[i];
                writer.WriteStartObject();
                writer.WriteString("name", mat.Name);
                writer.WriteBoolean("doubleSided", mat.DoubleSided);

                writer.WriteStartObject("pbrMetallicRoughness");
                writer.WriteStartArray("baseColorFactor");
                WriteSafeFloatValue(writer, mat.Color.R);
                WriteSafeFloatValue(writer, mat.Color.G);
                WriteSafeFloatValue(writer, mat.Color.B);
                WriteSafeFloatValue(writer, mat.Color.A);
                writer.WriteEndArray();
                WriteSafeFloat(writer, "metallicFactor", mat.Metallic);
                WriteSafeFloat(writer, "roughnessFactor", mat.Roughness);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            if (materials.Count > 0) writer.WriteEndArray();

            // accessors
            if (accessors.Count > 0) writer.WriteStartArray("accessors");
            for (int i = 0; i < accessors.Count; i++)
            {
                var acc = accessors[i];
                writer.WriteStartObject();
                writer.WriteNumber("bufferView", acc.BufferView);
                writer.WriteNumber("byteOffset", acc.ByteOffset);
                writer.WriteNumber("componentType", acc.ComponentType);
                writer.WriteNumber("count", acc.Count);
                writer.WriteString("type", acc.Type);

                if (acc.Min is not null && acc.Max is not null)
                {
                    writer.WriteStartArray("min");
                    foreach (var val in acc.Min) WriteSafeFloatValue(writer, val);
                    writer.WriteEndArray();

                    writer.WriteStartArray("max");
                    foreach (var val in acc.Max) WriteSafeFloatValue(writer, val);
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            if (accessors.Count > 0) writer.WriteEndArray();

            // bufferViews
            if (bufferViews.Count > 0) writer.WriteStartArray("bufferViews");
            for (int i = 0; i < bufferViews.Count; i++)
            {
                var bv = bufferViews[i];
                writer.WriteStartObject();
                writer.WriteNumber("buffer", 0);
                writer.WriteNumber("byteOffset", bv.ByteOffset);
                writer.WriteNumber("byteLength", bv.ByteLength);
                writer.WriteNumber("target", bv.Target);
                writer.WriteEndObject();
            }

            if (bufferViews.Count > 0) writer.WriteEndArray();

            // buffers
            if (binByteLength > 0)
            {
                writer.WriteStartArray("buffers");
                writer.WriteStartObject();
                writer.WriteNumber("byteLength", binByteLength);
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return ms.ToArray();
    }

    private static (float X, float Y, float Z)[] ComputeNormals(IReadOnlyList<Vector3d> vertices, List<int> indices)
    {
        var normals = new (double X, double Y, double Z)[vertices.Count];

        for (int i = 0; i <= indices.Count - 3; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count)
            {
                continue;
            }

            var p0 = vertices[i0];
            var p1 = vertices[i1];
            var p2 = vertices[i2];

            double ux = p1.X - p0.X;
            double uy = p1.Y - p0.Y;
            double uz = p1.Z - p0.Z;

            double vx = p2.X - p0.X;
            double vy = p2.Y - p0.Y;
            double vz = p2.Z - p0.Z;

            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;

            normals[i0].X += nx; normals[i0].Y += ny; normals[i0].Z += nz;
            normals[i1].X += nx; normals[i1].Y += ny; normals[i1].Z += nz;
            normals[i2].X += nx; normals[i2].Y += ny; normals[i2].Z += nz;
        }

        var result = new (float X, float Y, float Z)[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            var n = normals[i];
            double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
            if (len > 1e-6)
            {
                result[i] = ((float)(n.X / len), (float)(n.Y / len), (float)(n.Z / len));
            }
            else
            {
                result[i] = (0.0f, 0.0f, 1.0f);
            }
        }

        return result;
    }

    private static bool IsFinite(Vector3d v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private static bool IsFinite(Quaterniond q) =>
        double.IsFinite(q.X) && double.IsFinite(q.Y) && double.IsFinite(q.Z) && double.IsFinite(q.W);

    private static double PositiveOr(double value, double fallback) =>
        double.IsFinite(value) && value > 0 ? value : fallback;

    private static void WriteSafeFloatValue(Utf8JsonWriter writer, float val)
    {
        writer.WriteNumberValue(float.IsFinite(val) ? val : 0.0f);
    }

    private static void WriteSafeFloat(Utf8JsonWriter writer, string propertyName, float val)
    {
        writer.WriteNumber(propertyName, float.IsFinite(val) ? val : 0.0f);
    }

    private static void AlignStream(Stream stream, int alignment)
    {
        long rem = stream.Position % alignment;
        if (rem != 0)
        {
            int pad = alignment - (int)rem;
            for (int i = 0; i < pad; i++)
            {
                stream.WriteByte(0);
            }
        }
    }

    private readonly record struct BufferViewDef(int ByteOffset, int ByteLength, int Target);

    private readonly record struct AccessorDef(
        int BufferView,
        int ByteOffset,
        int ComponentType,
        int Count,
        string Type,
        float[]? Min,
        float[]? Max);

    private readonly record struct MeshDef(
        string Name,
        int PositionAccessor,
        int NormalAccessor,
        int IndicesAccessor,
        int? MaterialIndex);
}
