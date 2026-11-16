using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Extracts 3D polygon meshes from Navisworks geometry streams and TOC tables.
/// </summary>
public static class MeshParser
{
    internal const int MinMeshVertices = 3;
    internal const int MaxPlausibleVertices = 200_000;
    internal const double MaxCoordinateMagnitude = 1e8;

    /// <summary>
    /// Parses all 3D meshes found in the container streams.
    /// </summary>
    public static IReadOnlyList<NwcMesh> Parse(NwcContainer container, IReadOnlyList<NwcTransform>? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(container);

        var meshes = new List<NwcMesh>();

        // 1. Primary path: parse via Table of Contents (TOC) stream and chunked geometry block.
        var tocStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.Toc);
        if (tocStream is not null)
        {
            ParseViaToc(container, tocStream, meshes, transforms);
        }

        // 2. Secondary fallback: if TOC yielded no meshes, scan individual streams for vertex buffers.
        if (meshes.Count == 0)
        {
            ParseViaHeuristicScan(container, meshes, transforms);
        }

        return meshes;
    }

    private static void ParseViaToc(
        NwcContainer container,
        LcRawStream tocStream,
        List<NwcMesh> meshes,
        IReadOnlyList<NwcTransform>? transforms)
    {
        var tocData = tocStream.Data;
        if (tocData.Length < 16)
        {
            return;
        }

        int recordCount = BinaryPrimitives.ReadInt32LittleEndian(tocData.AsSpan(0));
        int chunkCount = BinaryPrimitives.ReadInt32LittleEndian(tocData.AsSpan(4));

        if (recordCount <= 0 || chunkCount <= 0 || chunkCount > 5000)
        {
            return;
        }

        int tocOffset = 8 + 4 * chunkCount;
        if (tocOffset > tocData.Length || recordCount > (tocData.Length - tocOffset) / 4)
        {
            return;
        }

        var recordLengths = new int[recordCount];
        for (int r = 0; r < recordCount; r++)
        {
            recordLengths[r] = BinaryPrimitives.ReadInt32LittleEndian(tocData.AsSpan(tocOffset + 4 * r));
        }

        // Locate geometry chunks: the chunkCount streams preceding the TOC in Section 0
        int startChunkIdx = tocStream.Index - chunkCount;
        if (startChunkIdx < 0)
        {
            startChunkIdx = 1;
        }

        int endChunkIdx = Math.Min(container.Streams.Count, startChunkIdx + chunkCount);

        int totalBytes = 0;
        for (int i = startChunkIdx; i < endChunkIdx; i++)
        {
            var chunkLength = container.Streams[i].Data.Length;
            if (chunkLength > int.MaxValue - totalBytes)
            {
                return;
            }

            totalBytes += chunkLength;
        }

        if (totalBytes == 0)
        {
            return;
        }

        var combined = new byte[totalBytes];
        int copyPos = 0;
        for (int i = startChunkIdx; i < endChunkIdx; i++)
        {
            var s = container.Streams[i];
            Buffer.BlockCopy(s.Data, 0, combined, copyPos, s.Data.Length);
            copyPos += s.Data.Length;
        }

        int curPos = 0;
        for (int r = 0; r < recordCount; r++)
        {
            int rLen = recordLengths[r];
            if (rLen < 0)
            {
                break;
            }

            // A final record may declare trailing bytes absent from the geometry chunks.
            // Bound both header and vertex reads by the bytes actually available.
            rLen = Math.Min(rLen, combined.Length - curPos);

            if (rLen >= 24)
            {
                int recTag = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 4));
                int subTag = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 12));

                if ((recTag is 94 or 95) && subTag == 60)
                {
                    int p1 = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 16));
                    int p2 = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 20));

                    int vCount = -1;
                    if (p2 > 0 && p2 % 3 == 0 && p2 / 3 <= (rLen - 24) / 12)
                    {
                        vCount = p2 / 3;
                    }
                    else if (p1 > 0 && p1 <= (rLen - 24) / 12)
                    {
                        vCount = p1;
                    }

                    if (vCount >= MinMeshVertices && vCount <= MaxPlausibleVertices)
                    {
                        int recId = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos));
                        var mesh = TryBuildMesh(combined, curPos, curPos + 24, vCount, recId, rLen, meshes.Count);
                        if (mesh is not null)
                        {
                            if (transforms is not null && meshes.Count < transforms.Count)
                            {
                                mesh.Transform = transforms[meshes.Count];
                            }

                            meshes.Add(mesh);
                        }
                    }
                }
            }

            curPos += rLen;
        }
    }

    private static void ParseViaHeuristicScan(
        NwcContainer container,
        List<NwcMesh> meshes,
        IReadOnlyList<NwcTransform>? transforms)
    {
        foreach (var stream in container.Streams)
        {
            var data = stream.Data;
            if (data.Length < 28)
            {
                continue;
            }

            for (int i = 0; i <= data.Length - 24; i += 4)
            {
                int tag = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 4));
                if (tag != 60)
                {
                    continue;
                }

                int vCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 8));
                if (vCount < MinMeshVertices || vCount > MaxPlausibleVertices)
                {
                    continue;
                }

                int vBytes = vCount * 12;
                if (i + 16 + vBytes > data.Length)
                {
                    continue;
                }

                var mesh = TryBuildMesh(data, i, i + 16, vCount, 0, 0, meshes.Count);
                if (mesh is not null)
                {
                    if (transforms is not null && meshes.Count < transforms.Count)
                    {
                        mesh.Transform = transforms[meshes.Count];
                    }

                    meshes.Add(mesh);
                    i += 12 + vBytes;
                }
            }
        }
    }

    private static NwcMesh? TryBuildMesh(
        byte[] buffer,
        int recordOffset,
        int vertexOffset,
        int vertexCount,
        int storedId,
        int recordLength,
        int meshId)
    {
        var vertices = new Vector3d[vertexCount];
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;

        for (int v = 0; v < vertexCount; v++)
        {
            int off = vertexOffset + v * 12;
            float x = BitConverter.ToSingle(buffer, off);
            float y = BitConverter.ToSingle(buffer, off + 4);
            float z = BitConverter.ToSingle(buffer, off + 8);

            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) ||
                Math.Abs(x) > MaxCoordinateMagnitude ||
                Math.Abs(y) > MaxCoordinateMagnitude ||
                Math.Abs(z) > MaxCoordinateMagnitude)
            {
                return null;
            }

            vertices[v] = new Vector3d(x, y, z);

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }

        int id = meshId;
        string name = $"Mesh_{meshId}";
        IReadOnlyList<int> indices = BuildStripIndices(vertexCount);

        // The writer's own records append the exact triangle indices and the mesh name after the positions.
        // Real Navisworks records have no such trailer, so anything that does not validate keeps the strip.
        if (recordLength > 0 &&
            TryReadMeshTrailer(buffer, recordOffset, recordLength, vertexCount, storedId, out var trailerId, out var trailerIndices, out var trailerName))
        {
            id = trailerId;
            name = string.IsNullOrEmpty(trailerName) ? $"Mesh_{id}" : trailerName;
            indices = trailerIndices;
        }

        return new NwcMesh
        {
            Id = id,
            Name = name,
            Vertices = vertices,
            Indices = indices,
            BoundingBox = new BoundingBox3d(new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ)),
        };
    }

    private static bool TryReadMeshTrailer(
        byte[] buffer,
        int recordOffset,
        int recordLength,
        int vertexCount,
        int storedId,
        out int id,
        out IReadOnlyList<int> indices,
        out string? name)
    {
        id = storedId;
        indices = [];
        name = null;

        int baseLength = 24 + vertexCount * 12;
        long trailerOffset = (long)recordOffset + baseLength;
        long recordEnd = (long)recordOffset + recordLength;
        if (recordLength < baseLength + 12 || trailerOffset + 12 > buffer.Length || recordEnd > buffer.Length)
        {
            return false;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)trailerOffset)) != NwcStreamSerializer.MeshTrailerMagic)
        {
            return false;
        }

        int indexCount = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)trailerOffset + 4));
        if (indexCount < 0 || indexCount % 3 != 0)
        {
            return false;
        }

        long indicesEnd = trailerOffset + 8 + 4L * indexCount;
        if (indicesEnd + 4 > recordEnd)
        {
            return false;
        }

        var parsedIndices = new int[indexCount];
        for (int i = 0; i < indexCount; i++)
        {
            int index = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)trailerOffset + 8 + 4 * i));
            if (index < 0 || index >= vertexCount)
            {
                return false;
            }

            parsedIndices[i] = index;
        }

        int nameLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)indicesEnd));
        long nameEnd = indicesEnd + 4 + nameLength;
        if (nameLength < 0 || nameEnd > recordEnd)
        {
            return false;
        }

        indices = parsedIndices;
        name = nameLength == 0 ? null : Encoding.UTF8.GetString(buffer, (int)indicesEnd + 4, nameLength);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="indices"/> is exactly the strip <see cref="BuildStripIndices"/> builds for <paramref name="vertexCount"/>:
    /// triangle i of vertexCount - 2 is (i, i + 1, i + 2) for even i and (i + 1, i, i + 2) for odd i. Compares in place, without building it.
    /// </summary>
    internal static bool IsStrip(IReadOnlyList<int> indices, int vertexCount)
    {
        int triangleCount = vertexCount - 2;
        if (indices.Count != triangleCount * 3)
        {
            return false;
        }

        for (int i = 0; i < triangleCount; i++)
        {
            int a = (i & 1) == 0 ? i : i + 1;
            int b = (i & 1) == 0 ? i + 1 : i;
            if (indices[3 * i] != a || indices[3 * i + 1] != b || indices[3 * i + 2] != i + 2)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The single triangle strip rebuilt over the vertices: (vertexCount - 2) triangles with alternating orientation.
    /// </summary>
    internal static int[] BuildStripIndices(int vertexCount)
    {
        int triangleCount = vertexCount - 2;
        var indices = new int[triangleCount * 3];
        int idx = 0;

        for (int i = 0; i < triangleCount; i++)
        {
            if ((i & 1) == 0)
            {
                indices[idx++] = i;
                indices[idx++] = i + 1;
                indices[idx++] = i + 2;
            }
            else
            {
                indices[idx++] = i + 1;
                indices[idx++] = i;
                indices[idx++] = i + 2;
            }
        }

        return indices;
    }
}
