using System.Buffers.Binary;
using System.Text.Json;
using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Exports 3D meshes, scene hierarchy, materials and PBR surface parameters
/// to standardized glTF 2.0 Binary container format (.glb).
/// </summary>
internal static class GlbExporter
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

        try
        {
            using var fs = File.Create(path);
            Write(meshes, materials, fs, worldCoordinates, generatorName, viewpoints);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
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

        // 1. Pack binary buffer (positions, normals, indices) into a temporary file, so the BIN payload
        // is not held in a single managed array.
        using Stream binMs = CreateBinBuffer();
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

        // Copies of a source material made for line primitives that export COLOR_0, one per source material and blend flag.
        var materialsWithBlend = new HashSet<int>();
        var lineMaterialMap = new Dictionary<(int? Source, bool Blend), int>();

        for (int m = 0; m < meshes.Count; m++)
        {
            var mesh = meshes[m];
            var vertices = worldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
            int vCount = vertices.Count;
            if (vCount < 2 || (mesh.Indices.Count < 3 && mesh.LineIndices.Count < 2))
            {
                continue;
            }

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

            // Keep only valid line segments that reference existing vertices.
            var lineIndices = new List<int>();
            for (int i = 0; i <= mesh.LineIndices.Count - 2; i += 2)
            {
                int i0 = mesh.LineIndices[i];
                int i1 = mesh.LineIndices[i + 1];
                if (i0 < 0 || i0 >= vCount || i1 < 0 || i1 >= vCount)
                {
                    continue;
                }

                lineIndices.Add(i0);
                lineIndices.Add(i1);
            }

            if (indices.Count == 0 && lineIndices.Count == 0)
            {
                continue;
            }

            // A. Vertex Positions
            AlignStream(binMs, 4);
            long posOffset = binMs.Position;
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

            // B. Triangle Normals (if triangles exist)
            int? normAccessorIdx = null;
            if (indices.Count > 0)
            {
                var normals = ComputeNormals(vertices, indices);
                AlignStream(binMs, 4);
                long normOffset = binMs.Position;
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

                normAccessorIdx = accessors.Count;
                accessors.Add(new AccessorDef(
                    normViewIdx,
                    0,
                    ComponentTypeFloat,
                    vCount,
                    "VEC3",
                    null,
                    null));
            }

            // C. Line Colors (if line colors exist and match vertex count)
            var lineColors = lineIndices.Count > 0 && mesh.LineColors is { Count: > 0 } storedColors && storedColors.Count == vCount
                ? storedColors
                : null;
            int? colAccessorIdx = null;
            if (lineColors is not null)
            {
                AlignStream(binMs, 4);
                long colOffset = binMs.Position;
                var colSpan = new byte[vCount * 16];
                for (int v = 0; v < vCount; v++)
                {
                    var c = lineColors[v];
                    BitConverter.TryWriteBytes(colSpan.AsSpan(v * 16), ColorComponent(c.R));
                    BitConverter.TryWriteBytes(colSpan.AsSpan(v * 16 + 4), ColorComponent(c.G));
                    BitConverter.TryWriteBytes(colSpan.AsSpan(v * 16 + 8), ColorComponent(c.B));
                    BitConverter.TryWriteBytes(colSpan.AsSpan(v * 16 + 12), ColorComponent(c.A));
                }

                binMs.Write(colSpan);
                int colViewIdx = bufferViews.Count;
                bufferViews.Add(new BufferViewDef(colOffset, colSpan.Length, TargetArrayBuffer));

                colAccessorIdx = accessors.Count;
                accessors.Add(new AccessorDef(
                    colViewIdx,
                    0,
                    ComponentTypeFloat,
                    vCount,
                    "VEC4",
                    null,
                    null));
            }

            // D. Triangle Indices
            int? triIdxAccessor = null;
            if (indices.Count > 0)
            {
                AlignStream(binMs, 4);
                long idxOffset = binMs.Position;
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

                triIdxAccessor = accessors.Count;
                accessors.Add(new AccessorDef(
                    idxViewIdx,
                    0,
                    useShort ? ComponentTypeUnsignedShort : ComponentTypeUnsignedInt,
                    idxCount,
                    "SCALAR",
                    null,
                    null));
            }

            // E. Line Indices
            int? lineIdxAccessor = null;
            if (lineIndices.Count > 0)
            {
                AlignStream(binMs, 4);
                long idxOffset = binMs.Position;
                int idxCount = lineIndices.Count;
                bool useShort = vCount <= 65535;

                byte[] idxSpan;
                if (useShort)
                {
                    idxSpan = new byte[idxCount * 2];
                    for (int i = 0; i < idxCount; i++)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(idxSpan.AsSpan(i * 2), (ushort)lineIndices[i]);
                    }
                }
                else
                {
                    idxSpan = new byte[idxCount * 4];
                    for (int i = 0; i < idxCount; i++)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(idxSpan.AsSpan(i * 4), (uint)lineIndices[i]);
                    }
                }

                binMs.Write(idxSpan);
                int idxViewIdx = bufferViews.Count;
                bufferViews.Add(new BufferViewDef(idxOffset, idxSpan.Length, TargetElementArrayBuffer));

                lineIdxAccessor = accessors.Count;
                accessors.Add(new AccessorDef(
                    idxViewIdx,
                    0,
                    useShort ? ComponentTypeUnsignedShort : ComponentTypeUnsignedInt,
                    idxCount,
                    "SCALAR",
                    null,
                    null));
            }

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

            // glTF multiplies COLOR_0 by baseColorFactor, but a stored line colour replaces the material colour, so a line
            // that exports COLOR_0 gets a white copy of its source material and shows only its own colours. The copy is BLEND
            // only when a segment colour is translucent. The source material, shared with the triangles and the lines
            // without COLOR_0, keeps its own colour and alphaMode.
            int? lineMatIdx = matIdx;
            if (lineColors is not null)
            {
                bool blend = lineIndices.Any(i => ColorComponent(lineColors[i].A) < 1.0f);
                if (matIdx.HasValue || blend)
                {
                    if (!lineMaterialMap.TryGetValue((matIdx, blend), out int lineMaterialIdx))
                    {
                        var source = mesh.Material;
                        lineMaterialIdx = materialTable.Count;
                        materialTable.Add(new NwcMaterial
                        {
                            Id = -materialTable.Count - 1,
                            Name = source is null ? "LineColors_Blend" : source.Name + (blend ? "_LineBlend" : "_Line"),
                            Color = RgbaColor.White,
                            DoubleSided = source?.DoubleSided ?? true,
                            Metallic = source?.Metallic ?? 0f,
                            Roughness = source?.Roughness ?? 1f
                        });
                        lineMaterialMap[(matIdx, blend)] = lineMaterialIdx;
                        if (blend)
                        {
                            materialsWithBlend.Add(lineMaterialIdx);
                        }
                    }

                    lineMatIdx = lineMaterialIdx;
                }
            }

            var primitives = new List<PrimitiveDef>();
            if (triIdxAccessor.HasValue && normAccessorIdx.HasValue)
            {
                primitives.Add(new PrimitiveDef(
                    posAccessorIdx,
                    normAccessorIdx.Value,
                    null,
                    triIdxAccessor.Value,
                    matIdx,
                    4)); // TRIANGLES
            }

            if (lineIdxAccessor.HasValue)
            {
                primitives.Add(new PrimitiveDef(
                    posAccessorIdx,
                    null,
                    colAccessorIdx,
                    lineIdxAccessor.Value,
                    lineMatIdx,
                    1)); // LINES
            }

            gltfMeshes.Add(new MeshDef(mesh.Name, primitives));
        }

        // Align BIN chunk to 4 bytes
        AlignStream(binMs, 4);
        long binLength = binMs.Position;
        if (binLength > uint.MaxValue)
        {
            throw new InvalidOperationException($"The glTF binary payload is {binLength} bytes; the GLB format supports at most {uint.MaxValue} bytes.");
        }

        // 2. Build JSON chunk
        byte[] jsonData = BuildJsonChunk(
            materialTable,
            gltfMeshes,
            accessors,
            bufferViews,
            binLength,
            generatorName ?? "NwcIO glTF 2.0 Exporter",
            viewpoints,
            materialsWithBlend);

        // Align JSON chunk to 4 bytes with 0x20 spaces as per glTF spec
        int jsonPadding = (4 - (jsonData.Length % 4)) % 4;
        int jsonChunkLength = jsonData.Length + jsonPadding;

        // 3. Write binary GLB container
        long totalLength = 12 + (8 + jsonChunkLength) + (binLength > 0 ? 8 + binLength : 0);
        if (totalLength > uint.MaxValue)
        {
            throw new InvalidOperationException($"The GLB file would be {totalLength} bytes; the format supports at most {uint.MaxValue} bytes.");
        }

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

        // Chunk 1: BIN, copied from the temporary file without a managed array copy
        if (binLength > 0)
        {
            bw.Write((uint)binLength);
            bw.Write(ChunkTypeBin);
            bw.Flush();
            binMs.Position = 0;
            binMs.CopyTo(output);
        }

        bw.Flush();
    }

    private static byte[] BuildJsonChunk(
        List<NwcMaterial> materials,
        List<MeshDef> meshes,
        List<AccessorDef> accessors,
        List<BufferViewDef> bufferViews,
        long binByteLength,
        string generator,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null,
        HashSet<int>? materialsWithBlend = null)
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
                foreach (var p in m.Primitives)
                {
                    writer.WriteStartObject();
                    writer.WriteStartObject("attributes");
                    writer.WriteNumber("POSITION", p.PositionAccessor);
                    if (p.NormalAccessor is { } normAcc)
                    {
                        writer.WriteNumber("NORMAL", normAcc);
                    }
                    if (p.ColorAccessor is { } colAcc)
                    {
                        writer.WriteNumber("COLOR_0", colAcc);
                    }
                    writer.WriteEndObject();

                    writer.WriteNumber("indices", p.IndicesAccessor);
                    if (p.MaterialIndex is { } matIdx)
                    {
                        writer.WriteNumber("material", matIdx);
                    }

                    writer.WriteNumber("mode", p.Mode);
                    writer.WriteEndObject();
                }
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

                if ((materialsWithBlend != null && materialsWithBlend.Contains(i)) || mat.Color.A < 1.0f)
                {
                    writer.WriteString("alphaMode", "BLEND");
                }

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

    // glTF requires COLOR_0 floats to be finite and within [0,1]. A non-finite component is written as 0, as the
    // material factors are; the written alpha is also the one that decides BLEND.
    private static float ColorComponent(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

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

    // Falls back to memory when TEMP is not writable (sandboxes, read-only containers), as before the spill.
    private static Stream CreateBinBuffer()
    {
        try
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"nwcio-{Guid.NewGuid():N}.bin");
            return new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return new MemoryStream();
        }
    }

    private readonly record struct BufferViewDef(long ByteOffset, int ByteLength, int Target);

    private readonly record struct AccessorDef(
        int BufferView,
        int ByteOffset,
        int ComponentType,
        int Count,
        string Type,
        float[]? Min,
        float[]? Max);

    private readonly record struct PrimitiveDef(
        int PositionAccessor,
        int? NormalAccessor,
        int? ColorAccessor,
        int IndicesAccessor,
        int? MaterialIndex,
        int Mode);

    private readonly record struct MeshDef(
        string Name,
        IReadOnlyList<PrimitiveDef> Primitives);
}
