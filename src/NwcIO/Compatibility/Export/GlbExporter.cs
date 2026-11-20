using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using NwcIO.Model;

namespace NwcIO.Internal.Export;

/// <summary>
/// Exports 3D meshes, materials and saved cameras
/// to standardized glTF 2.0 Binary container format (.glb).
/// </summary>
internal static class GlbExporter
{
    private const uint GlbHeaderMagic = 0x46546C67; // "glTF"
    private const uint GlbVersion = 2;
    private const uint ChunkTypeJson = 0x4E4F534A; // "JSON"
    private const uint ChunkTypeBin = 0x004E4942;  // "BIN\0"
    private const uint MaxAlignedLength = uint.MaxValue - 3;

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
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null) =>
        WriteFile(meshes, materials, path, NwcGlbExportOptions.ForCoordinates(worldCoordinates), generatorName, viewpoints);

    public static void WriteFile(
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcMaterial> materials,
        string path,
        NwcGlbExportOptions options,
        string? generatorName = null,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(path);

        AtomicFileWriter.Write(path, output => Write(meshes, materials, output, options, generatorName, viewpoints), default);
    }

    public static void Write(
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcMaterial> materials,
        Stream output,
        bool worldCoordinates = true,
        string? generatorName = null,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null,
        CancellationToken cancellationToken = default) =>
        Write(meshes, materials, output, NwcGlbExportOptions.ForCoordinates(worldCoordinates), generatorName, viewpoints, cancellationToken);

    public static void Write(
        IReadOnlyList<NwcMesh> meshes,
        IReadOnlyList<NwcMaterial> materials,
        Stream output,
        NwcGlbExportOptions options,
        string? generatorName = null,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(options.UnitScale) || options.UnitScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.UnitScale, "UnitScale must be positive and finite.");
        }

        bool worldCoordinates = options.WorldCoordinates;
        bool convertAxis = options.ConvertZUpToYUp;
        var center = options.RecenterToOrigin ? FindCenter(meshes, worldCoordinates, cancellationToken) : null;
        bool transformPositions = center.HasValue || convertAxis || options.UnitScale != 1;

        using var bin = new BinChunk();
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
        var imageUris = new Dictionary<string, int>(StringComparer.Ordinal);
        var images = new List<string>();
        var texturedVariants = new Dictionary<int, int>();
        var materialTextures = new Dictionary<int, int>();

        for (int m = 0; m < meshes.Count; m++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mesh = meshes[m];
            var vertices = worldCoordinates ? mesh.GetWorldVertices() : mesh.Vertices;
            int vCount = vertices.Count;
            if (vCount < 2 || (mesh.Indices.Count < 3 && mesh.LineIndices.Count < 2))
            {
                continue;
            }

            if (vertices.Any(static position => !FitsFloat(position)))
            {
                continue;
            }

            // Keep only complete triangles that reference existing vertices.
            var faces = worldCoordinates ? mesh.GetWorldIndices() : mesh.Indices;
            var indices = new List<int>();
            for (int i = 0; i <= faces.Count - 3; i += 3)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
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
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
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
            long posLength = (long)vCount * 12;
            long posOffset = bin.BeginView(posLength);
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

            for (int v = 0; v < vCount;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = bin.ReserveRecords(vCount - v, 12, out int count);
                for (int i = 0; i < count; i++, v++)
                {
                    var pt = vertices[v];
                    if (transformPositions)
                    {
                        pt = TransformPosition(pt, center, options);
                        if (!FitsFloat(pt)) throw new InvalidOperationException("The converted GLB mesh position is outside the finite float32 range.");
                    }
                    var packed = batch[(i * 12)..];
                    BinaryPrimitives.WriteSingleLittleEndian(packed, (float)pt.X);
                    BinaryPrimitives.WriteSingleLittleEndian(packed[4..], (float)pt.Y);
                    BinaryPrimitives.WriteSingleLittleEndian(packed[8..], (float)pt.Z);

                    if (pt.X < minX) minX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Z < minZ) minZ = pt.Z;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y > maxY) maxY = pt.Y;
                    if (pt.Z > maxZ) maxZ = pt.Z;
                }
            }

            int posViewIdx = bufferViews.Count;
            bufferViews.Add(new BufferViewDef(posOffset, posLength, TargetArrayBuffer));

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
                long normOffset = bin.BeginView(posLength);
                var normals = AccumulateNormals(vertices, indices);
                for (int v = 0; v < vCount;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = bin.ReserveRecords(vCount - v, 12, out int count);
                    for (int i = 0; i < count; i++, v++)
                    {
                        var n = normals[v];
                        double len = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
                        var (x, y, z) = len > 1e-6
                            ? ((float)(n.X / len), (float)(n.Y / len), (float)(n.Z / len))
                            : (0.0f, 0.0f, 1.0f);
                        if (convertAxis) (y, z) = (z, -y);
                        var packed = batch[(i * 12)..];
                        BinaryPrimitives.WriteSingleLittleEndian(packed, x);
                        BinaryPrimitives.WriteSingleLittleEndian(packed[4..], y);
                        BinaryPrimitives.WriteSingleLittleEndian(packed[8..], z);
                    }
                }

                int normViewIdx = bufferViews.Count;
                bufferViews.Add(new BufferViewDef(normOffset, posLength, TargetArrayBuffer));

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
                long colLength = (long)vCount * 16;
                long colOffset = bin.BeginView(colLength);
                for (int v = 0; v < vCount;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = bin.ReserveRecords(vCount - v, 16, out int count);
                    for (int i = 0; i < count; i++, v++)
                    {
                        var c = lineColors[v];
                        var packed = batch[(i * 16)..];
                        BinaryPrimitives.WriteSingleLittleEndian(packed, ColorComponent(c.R));
                        BinaryPrimitives.WriteSingleLittleEndian(packed[4..], ColorComponent(c.G));
                        BinaryPrimitives.WriteSingleLittleEndian(packed[8..], ColorComponent(c.B));
                        BinaryPrimitives.WriteSingleLittleEndian(packed[12..], ColorComponent(c.A));
                    }
                }

                int colViewIdx = bufferViews.Count;
                bufferViews.Add(new BufferViewDef(colOffset, colLength, TargetArrayBuffer));

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

            int? uvAccessor = null;
            if (indices.Count > 0 && TextureExport.GetCoordinates(mesh, vCount, cancellationToken) is { } uv)
            {
                long uvLength = (long)vCount * 8;
                long offset = bin.BeginView(uvLength);
                for (int v = 0; v < vCount;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = bin.ReserveRecords(vCount - v, 8, out int count);
                    for (int i = 0; i < count; i++, v++)
                    {
                        var coordinate = uv[v];
                        BinaryPrimitives.WriteSingleLittleEndian(batch[(8 * i)..], coordinate.X);
                        // glTF's texture origin is the upper left. NWC and OBJ use the lower left.
                        BinaryPrimitives.WriteSingleLittleEndian(batch[(8 * i + 4)..], 1f - coordinate.Y);
                    }
                }

                uvAccessor = accessors.Count;
                accessors.Add(new AccessorDef(bufferViews.Count, 0, ComponentTypeFloat, vCount, "VEC2", null, null));
                bufferViews.Add(new BufferViewDef(offset, uvLength, TargetArrayBuffer));
            }

            int? triIdxAccessor = WriteIndexAccessor(bin, indices, vCount, bufferViews, accessors);
            int? lineIdxAccessor = WriteIndexAccessor(bin, lineIndices, vCount, bufferViews, accessors);

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
                int? triangleMaterial = matIdx;
                if (uvAccessor.HasValue && matIdx is { } baseIndex && TextureExport.GetImageUri(mesh.Material!.DiffuseTexture) is { } uri)
                {
                    if (!texturedVariants.TryGetValue(baseIndex, out int variant))
                    {
                        if (!imageUris.TryGetValue(uri, out int image))
                        {
                            image = images.Count;
                            images.Add(uri);
                            imageUris.Add(uri, image);
                        }
                        variant = materialTable.Count;
                        materialTable.Add(mesh.Material);
                        texturedVariants.Add(baseIndex, variant);
                        materialTextures.Add(variant, image);
                    }
                    triangleMaterial = variant;
                }
                primitives.Add(new PrimitiveDef(
                    posAccessorIdx,
                    normAccessorIdx.Value,
                    null,
                    uvAccessor,
                    triIdxAccessor.Value,
                    triangleMaterial,
                    4)); // TRIANGLES
            }

            if (lineIdxAccessor.HasValue)
            {
                primitives.Add(new PrimitiveDef(
                    posAccessorIdx,
                    null,
                    colAccessorIdx,
                    null,
                    lineIdxAccessor.Value,
                    lineMatIdx,
                    1)); // LINES
            }

            gltfMeshes.Add(new MeshDef(mesh.Name, primitives));
        }

        var binFile = bin.Complete();
        var jsonData = BuildJsonChunk(
            materialTable,
            gltfMeshes,
            accessors,
            bufferViews,
            GetBinChunkLength(binFile?.Length ?? 0),
            generatorName ?? "NwcIO glTF 2.0 Exporter",
            options.ExportCameras ? viewpoints : null,
            center,
            options,
            materialsWithBlend,
            images,
            materialTextures,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        WriteContainer(output, jsonData.Span, binFile);
    }

    internal static void WriteContainer(Stream output, ReadOnlySpan<byte> jsonData, Stream? bin)
    {
        long binLength = bin?.Length ?? 0;
        long binChunkLength = GetBinChunkLength(binLength);
        long jsonChunkLength = Align4(jsonData.Length);
        long totalLength = 12 + 8 + jsonChunkLength + (binChunkLength > 0 ? 8 + binChunkLength : 0);
        if (totalLength > MaxAlignedLength)
        {
            throw new InvalidOperationException(
                $"The GLB file would be {totalLength} bytes including headers and padding. The 4-byte-aligned limit is {MaxAlignedLength} bytes.");
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
        for (long p = jsonData.Length; p < jsonChunkLength; p++)
        {
            bw.Write((byte)0x20); // space padding
        }

        // Chunk 1: BIN, copied from the temporary file without a managed array copy
        if (bin is not null && binChunkLength > 0)
        {
            bw.Write((uint)binChunkLength);
            bw.Write(ChunkTypeBin);
            bw.Flush();
            bin.Position = 0;
            bin.CopyTo(output);
            for (long p = binLength; p < binChunkLength; p++)
            {
                bw.Write((byte)0);
            }
        }

        bw.Flush();
    }

    private static ReadOnlyMemory<byte> BuildJsonChunk(
        List<NwcMaterial> materials,
        List<MeshDef> meshes,
        List<AccessorDef> accessors,
        List<BufferViewDef> bufferViews,
        long binByteLength,
        string generator,
        IReadOnlyList<NwcSavedViewpoint>? viewpoints,
        Vector3d? center,
        NwcGlbExportOptions options,
        HashSet<int>? materialsWithBlend,
        List<string> images,
        Dictionary<int, int> materialTextures,
        CancellationToken cancellationToken)
    {
        var json = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(json))
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
            bool addRoot = center.HasValue && totalNodes > 0;

            // scenes
            writer.WriteStartArray("scenes");
            writer.WriteStartObject();
            if (totalNodes > 0) writer.WriteStartArray("nodes");
            if (addRoot)
            {
                writer.WriteNumberValue(totalNodes);
            }
            else
            {
                for (int i = 0; i < totalNodes; i++) writer.WriteNumberValue(i);
            }

            if (totalNodes > 0) writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            // nodes
            if (totalNodes > 0) writer.WriteStartArray("nodes");
            for (int i = 0; i < meshes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString("name", meshes[i].Name);
                writer.WriteNumber("mesh", i);
                writer.WriteEndObject();
            }

            if (cameras is not null)
            {
                for (int i = 0; i < cameras.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var vp = cameras[i];
                    var cam = vp.Camera;
                    writer.WriteStartObject();
                    writer.WriteString("name", vp.Name);
                    writer.WriteNumber("camera", i);

                    WriteTranslation(writer, TransformPosition(cam.Position, center, options));
                    var rotation = options.ConvertZUpToYUp ? ToYUp(cam.Rotation) : cam.Rotation;

                    writer.WriteStartArray("rotation");
                    writer.WriteNumberValue(rotation.X);
                    writer.WriteNumberValue(rotation.Y);
                    writer.WriteNumberValue(rotation.Z);
                    writer.WriteNumberValue(rotation.W);
                    writer.WriteEndArray();

                    writer.WriteEndObject();
                }
            }

            if (addRoot)
            {
                writer.WriteStartObject();
                WriteTranslation(writer, TransformPosition(center.GetValueOrDefault(), null, options));
                writer.WriteStartArray("children");
                for (int i = 0; i < totalNodes; i++) writer.WriteNumberValue(i);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            if (totalNodes > 0) writer.WriteEndArray();

            // cameras
            if (cameras is not null && cameras.Count > 0)
            {
                writer.WriteStartArray("cameras");
                for (int i = 0; i < cameras.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                        writer.WriteNumber("xmag", ScaleLength(xmag, options.UnitScale));
                        writer.WriteNumber("ymag", ScaleLength(PositiveOr(ymag, xmag), options.UnitScale));
                        writer.WriteNumber("zfar", ScaleLength(PositiveOr(cam.FarDistance, 10000.0), options.UnitScale));
                        writer.WriteNumber("znear", ScaleLength(PositiveOr(cam.NearDistance, 0.1), options.UnitScale));
                        writer.WriteEndObject();
                    }
                    else
                    {
                        writer.WriteString("type", "perspective");
                        writer.WriteStartObject("perspective");
                        writer.WriteNumber("aspectRatio", PositiveOr(cam.AspectRatio, 1.0));
                        writer.WriteNumber("yfov", PositiveOr(cam.FieldOfView * (Math.PI / 180.0), 1.04719755));
                        writer.WriteNumber("zfar", ScaleLength(PositiveOr(cam.FarDistance, 10000.0), options.UnitScale));
                        writer.WriteNumber("znear", ScaleLength(PositiveOr(cam.NearDistance, 0.1), options.UnitScale));
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
                cancellationToken.ThrowIfCancellationRequested();
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
                    if (p.UvAccessor is { } uvAcc) writer.WriteNumber("TEXCOORD_0", uvAcc);
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
                cancellationToken.ThrowIfCancellationRequested();
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
                if (materialTextures.TryGetValue(i, out int texture))
                {
                    writer.WriteStartObject("baseColorTexture");
                    writer.WriteNumber("index", texture);
                    writer.WriteEndObject();
                }
                WriteSafeFloat(writer, "metallicFactor", mat.Metallic);
                WriteSafeFloat(writer, "roughnessFactor", mat.Roughness);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            if (materials.Count > 0) writer.WriteEndArray();

            if (images.Count > 0)
            {
                writer.WriteStartArray("images");
                foreach (string uri in images)
                {
                    writer.WriteStartObject();
                    writer.WriteString("uri", uri);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteStartArray("textures");
                for (int i = 0; i < images.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("source", i);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            // accessors
            if (accessors.Count > 0) writer.WriteStartArray("accessors");
            for (int i = 0; i < accessors.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                cancellationToken.ThrowIfCancellationRequested();
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

        return json.WrittenMemory;
    }

    private static Vector3d? FindCenter(IReadOnlyList<NwcMesh> meshes, bool worldCoordinates, CancellationToken cancellationToken)
    {
        var bounds = BoundingBox3d.Empty;
        foreach (var mesh in meshes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (mesh.Indices.Count < 3 && mesh.LineIndices.Count < 2) continue;

            var vertices = worldCoordinates ? mesh.TransformVertices(cancellationToken) : mesh.Vertices;
            if (vertices.Count < 2 || (!HasPrimitive(mesh.Indices, 3, vertices.Count, cancellationToken) &&
                !HasPrimitive(mesh.LineIndices, 2, vertices.Count, cancellationToken))) continue;

            var meshBounds = BoundingBox3d.Empty;
            for (int i = 0; i < vertices.Count; i++)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                var vertex = vertices[i];
                if (!FitsFloat(vertex))
                {
                    meshBounds = BoundingBox3d.Empty;
                    break;
                }
                meshBounds = meshBounds.Union(vertex);
            }
            bounds = bounds.Union(meshBounds);
        }

        return bounds.IsEmpty ? null : bounds.Center;
    }

    private static bool HasPrimitive(IReadOnlyList<int> indices, int size, int vertexCount, CancellationToken cancellationToken)
    {
        for (int i = 0; i <= indices.Count - size; i += size)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if ((uint)indices[i] < (uint)vertexCount && (uint)indices[i + 1] < (uint)vertexCount &&
                (size == 2 || (uint)indices[i + 2] < (uint)vertexCount)) return true;
        }
        return false;
    }

    private static Vector3d TransformPosition(Vector3d position, Vector3d? center, NwcGlbExportOptions options)
    {
        if (center is { } origin) position -= origin;
        if (options.UnitScale != 1) position *= options.UnitScale;
        return options.ConvertZUpToYUp ? new Vector3d(position.X, position.Z, -position.Y) : position;
    }

    private static Quaterniond ToYUp(Quaterniond rotation)
    {
        // Left-compose -90 degrees around X. The camera's canonical local axes stay the same.
        const double h = 0.7071067811865476;
        var q = rotation.Normalized();
        return new Quaterniond(h * (q.X - q.W), h * (q.Y + q.Z), h * (q.Z - q.Y), h * (q.W + q.X));
    }

    private static void WriteTranslation(Utf8JsonWriter writer, Vector3d position)
    {
        if (!IsFinite(position)) throw new InvalidOperationException("The converted GLB node translation is not finite.");
        writer.WriteStartArray("translation");
        writer.WriteNumberValue(position.X);
        writer.WriteNumberValue(position.Y);
        writer.WriteNumberValue(position.Z);
        writer.WriteEndArray();
    }

    private static double ScaleLength(double length, double scale)
    {
        double converted = length * scale;
        return double.IsFinite(converted) && converted > 0 ? converted :
            throw new InvalidOperationException("The converted GLB camera length must be positive and finite.");
    }

    private static bool FitsFloat(Vector3d position) =>
        float.IsFinite((float)position.X) && float.IsFinite((float)position.Y) && float.IsFinite((float)position.Z);

    private static (double X, double Y, double Z)[] AccumulateNormals(IReadOnlyList<Vector3d> vertices, List<int> indices)
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

        return normals;
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

    private static int? WriteIndexAccessor(
        BinChunk bin,
        List<int> indices,
        int vertexCount,
        List<BufferViewDef> bufferViews,
        List<AccessorDef> accessors)
    {
        if (indices.Count == 0)
        {
            return null;
        }

        bool useShort = vertexCount <= ushort.MaxValue;
        int stride = useShort ? sizeof(ushort) : sizeof(uint);
        long byteLength = (long)indices.Count * stride;
        long offset = bin.BeginView(byteLength);
        var values = CollectionsMarshal.AsSpan(indices);
        while (!values.IsEmpty)
        {
            var batch = bin.ReserveRecords(values.Length, stride, out int count);
            if (useShort)
            {
                for (int i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(batch[(i * sizeof(ushort))..], (ushort)values[i]);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(batch[(i * sizeof(uint))..], (uint)values[i]);
                }
            }

            values = values[count..];
        }

        int viewIndex = bufferViews.Count;
        bufferViews.Add(new BufferViewDef(offset, byteLength, TargetElementArrayBuffer));
        int accessorIndex = accessors.Count;
        accessors.Add(new AccessorDef(
            viewIndex, 0, useShort ? ComponentTypeUnsignedShort : ComponentTypeUnsignedInt, indices.Count, "SCALAR", null, null));
        return accessorIndex;
    }

    private static long Align4(long length) => (length + 3) & ~3L;

    private static long GetBinChunkLength(long length)
    {
        if (length > MaxAlignedLength)
        {
            throw new InvalidOperationException(
                $"The GLB BIN chunk payload is {length} bytes. The 4-byte-aligned chunk limit is {MaxAlignedLength} bytes.");
        }

        return Align4(length);
    }

    internal static FileStream CreateBinBuffer(string directory)
    {
        string path = Path.Combine(directory, $"nwcio-{Guid.NewGuid():N}.bin");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 0, // BinChunk already batches writes.
            Options = FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    private sealed class BinChunk : IDisposable
    {
        private const int BatchCapacity = 64 * 1024;
        private FileStream? _file;
        private byte[]? _packed;
        private int _used;

        public long BeginView(long length)
        {
            var file = _file ??= CreateBinBuffer(Path.GetTempPath());
            long position = file.Position + _used;
            long offset = Align4(position);
            GetBinChunkLength(offset + length);
            if (offset > position)
            {
                Reserve((int)(offset - position)).Clear();
            }

            return offset;
        }

        public Span<byte> ReserveRecords(int remaining, int stride, out int count)
        {
            count = Math.Min(remaining, BatchCapacity / stride);
            return Reserve(count * stride);
        }

        private Span<byte> Reserve(int length)
        {
            var packed = _packed ??= new byte[BatchCapacity];
            if (_used + length > packed.Length)
            {
                FlushPacked();
            }

            var slice = packed.AsSpan(_used, length);
            _used += length;
            return slice;
        }

        public FileStream? Complete()
        {
            FlushPacked();
            return _file;
        }

        private void FlushPacked()
        {
            if (_used > 0)
            {
                _file!.Write(_packed.AsSpan(0, _used));
                _used = 0;
            }
        }

        public void Dispose() => _file?.Dispose();
    }

    private readonly record struct BufferViewDef(long ByteOffset, long ByteLength, int Target);

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
        int? UvAccessor,
        int IndicesAccessor,
        int? MaterialIndex,
        int Mode);

    private readonly record struct MeshDef(
        string Name,
        IReadOnlyList<PrimitiveDef> Primitives);
}
