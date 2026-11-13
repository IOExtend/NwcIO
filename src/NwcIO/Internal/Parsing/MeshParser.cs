using System.Buffers.Binary;
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
        if (tocOffset + 4 * recordCount > tocData.Length)
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
            totalBytes += container.Streams[i].Data.Length;
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
            if (rLen >= 24 && curPos + 24 <= combined.Length)
            {
                int recTag = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 4));
                int subTag = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 12));

                if ((recTag is 94 or 95) && subTag == 60)
                {
                    int p1 = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 16));
                    int p2 = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos + 20));

                    int vCount = -1;
                    if (p2 > 0 && p2 % 3 == 0 && (p2 / 3) * 12 + 24 <= rLen)
                    {
                        vCount = p2 / 3;
                    }
                    else if (p1 > 0 && p1 * 12 + 24 <= rLen)
                    {
                        vCount = p1;
                    }

                    if (vCount >= MinMeshVertices && vCount <= MaxPlausibleVertices)
                    {
                        var mesh = TryBuildMesh(combined, curPos + 24, vCount, meshes.Count);
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

                var mesh = TryBuildMesh(data, i + 16, vCount, meshes.Count);
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

    private static NwcMesh? TryBuildMesh(byte[] buffer, int vertexOffset, int vertexCount, int meshId)
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

        // Generate triangle strip indices: (vCount - 2) triangles with alternating orientation
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

        return new NwcMesh
        {
            Id = meshId,
            Name = $"Mesh_{meshId}",
            Vertices = vertices,
            Indices = indices,
            BoundingBox = new BoundingBox3d(new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ)),
        };
    }
}
