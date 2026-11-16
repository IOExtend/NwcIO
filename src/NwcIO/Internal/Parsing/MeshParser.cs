using System.Buffers.Binary;
using System.Text;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Extracts 3D polygon meshes from Navisworks geometry streams and TOC tables.
/// </summary>
internal static class MeshParser
{
    internal const int MinMeshVertices = 3;
    internal const int MaxPlausibleVertices = 200_000;
    internal const double MaxCoordinateMagnitude = 1e8;

    /// <summary>
    /// Parses all 3D meshes found in the container streams.
    /// </summary>
    /// <param name="maxVertices">Cap on one record's vertices; records above it are skipped. Defaults to <see cref="MaxPlausibleVertices"/>.</param>
    /// <param name="transformIndices">Per-mesh indices into <paramref name="transforms"/> from the writer-side link stream; positional binding when null.</param>
    public static IReadOnlyList<NwcMesh> Parse(
        NwcContainer container,
        IReadOnlyList<NwcTransform>? transforms = null,
        int maxVertices = MaxPlausibleVertices,
        IReadOnlyList<int>? transformIndices = null)
        => Parse(container, transforms, maxVertices, transformIndices, out _);

    /// <summary>
    /// Parses all 3D meshes and reports how many geometry records were recognised but skipped
    /// (vertex count out of range, record truncated or malformed).
    /// </summary>
    public static IReadOnlyList<NwcMesh> Parse(
        NwcContainer container,
        IReadOnlyList<NwcTransform>? transforms,
        int maxVertices,
        IReadOnlyList<int>? transformIndices,
        out int skippedRecords)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (maxVertices < MinMeshVertices)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVertices), maxVertices, $"The vertex cap is at least {MinMeshVertices}.");
        }

        var meshes = new List<NwcMesh>();
        int tocSkipped = 0;
        int scanSkipped = 0;

        // 1. Primary path: parse via Table of Contents (TOC) stream and chunked geometry block.
        var tocStream = container.Streams.FirstOrDefault(s => s.Kind == NwcStreamKind.Toc);
        if (tocStream is not null)
        {
            ParseViaToc(container, tocStream, meshes, transforms, maxVertices, transformIndices, ref tocSkipped);
        }

        // 2. Secondary fallback: if TOC yielded no meshes, scan individual streams for vertex buffers.
        if (meshes.Count == 0)
        {
            ParseViaHeuristicScan(container, meshes, transforms, maxVertices, transformIndices, ref scanSkipped);
        }

        // The fallback replaces the TOC attempt; take the larger count so neither path hides a skipped record.
        skippedRecords = Math.Max(tocSkipped, scanSkipped);
        return meshes;
    }

    private static void AssignTransform(
        NwcMesh mesh,
        int index,
        IReadOnlyList<NwcTransform>? transforms,
        IReadOnlyList<int>? transformIndices)
    {
        if (transformIndices is not null)
        {
            if (index < transformIndices.Count)
            {
                int transformIndex = transformIndices[index];
                if (transformIndex >= 0 && transforms is not null && transformIndex < transforms.Count)
                {
                    mesh.Transform = transforms[transformIndex];
                }
            }

            return;
        }

        if (transforms is not null && index < transforms.Count)
        {
            mesh.Transform = transforms[index];
        }
    }

    private static void ParseViaToc(
        NwcContainer container,
        LcRawStream tocStream,
        List<NwcMesh> meshes,
        IReadOnlyList<NwcTransform>? transforms,
        int maxVertices,
        IReadOnlyList<int>? transformIndices,
        ref int skippedRecords)
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
        var usedIds = new MeshIdAllocator();
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

                    bool lines = recTag == 95 || vCount == 2;
                    int minVertices = lines ? 2 : MinMeshVertices;
                    if (vCount >= minVertices && vCount <= maxVertices)
                    {
                        int recId = BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(curPos));
                        // Ids and transforms follow record order, not the count of accepted meshes: a skipped
                        // record must not shift the placement of the records after it.
                        var mesh = TryBuildMesh(combined, curPos, curPos + 24, vCount, recId, rLen, r, lines, usedIds);
                        if (mesh is not null)
                        {
                            AssignTransform(mesh, r, transforms, transformIndices);

                            meshes.Add(mesh);
                        }
                        else
                        {
                            skippedRecords++;
                        }
                    }
                    else
                    {
                        skippedRecords++;
                    }
                }
            }

            curPos += rLen;
        }
    }

    private static void ParseViaHeuristicScan(
        NwcContainer container,
        List<NwcMesh> meshes,
        IReadOnlyList<NwcTransform>? transforms,
        int maxVertices,
        IReadOnlyList<int>? transformIndices,
        ref int skippedRecords)
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
                bool lines = vCount == 2;
                int minVertices = lines ? 2 : MinMeshVertices;
                if (vCount < minVertices || vCount > maxVertices)
                {
                    skippedRecords++;
                    continue;
                }

                // vCount can reach int.MaxValue through MaxMeshVertices: compare without multiplying.
                if (vCount > (data.Length - (i + 16)) / 12)
                {
                    skippedRecords++;
                    continue;
                }

                var mesh = TryBuildMesh(data, i, i + 16, vCount, 0, 0, meshes.Count, lines);
                if (mesh is not null)
                {
                    AssignTransform(mesh, meshes.Count, transforms, transformIndices);

                    meshes.Add(mesh);
                    i += 12 + vCount * 12;
                }
                else
                {
                    skippedRecords++;
                }
            }
        }
    }

    /// <summary>Tracks claimed mesh ids. Fallback probing resumes from the last fallback, so many duplicates stay linear.</summary>
    private sealed class MeshIdAllocator
    {
        private readonly HashSet<int> _used = [];
        private int _nextFallback = int.MinValue;

        public int Claim(int id, int fallbackStart)
        {
            if (_used.Add(id))
            {
                return id;
            }

            id = Math.Max(fallbackStart, _nextFallback);
            while (!_used.Add(id))
            {
                id++;
            }

            _nextFallback = id + 1;
            return id;
        }
    }

    private static NwcMesh? TryBuildMesh(
        byte[] buffer,
        int recordOffset,
        int vertexOffset,
        int vertexCount,
        int storedId,
        int recordLength,
        int meshId,
        bool lines,
        MeshIdAllocator? usedIds = null)
    {
        if (vertexOffset < 0 || vertexCount > (buffer.Length - vertexOffset) / 12)
        {
            return null;
        }

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
        IReadOnlyList<int> indices = lines ? [] : BuildStripIndices(vertexCount);

        // The writer's own records append the exact triangle indices and the mesh name after the positions.
        // Real Navisworks records have no such trailer, so anything that does not validate keeps the strip.
        if (!lines && recordLength > 0 &&
            TryReadMeshTrailer(buffer, recordOffset, recordLength, vertexCount, storedId, out var trailerId, out var trailerIndices, out var trailerName))
        {
            id = trailerId;
            name = SanitizeMeshName(trailerName, id);
            indices = trailerIndices;
        }

        // A merged document can repeat ids. Keep the first occurrence exact and give later duplicates a
        // unique fallback, so clash detection and IFC export do not silently pair unrelated meshes.
        if (usedIds is not null)
        {
            id = usedIds.Claim(id, meshId);
        }

        return new NwcMesh
        {
            Id = id,
            Name = name,
            Vertices = vertices,
            Indices = indices,
            LineIndices = lines ? BuildLineIndices(vertexCount) : [],
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
        // magic, index count, mesh id and name length are all required before the optional indices.
        if (recordLength < baseLength + 16 || trailerOffset + 16 > buffer.Length || recordEnd > buffer.Length)
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
        if (indicesEnd + 8 > recordEnd)
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

        int trailerId = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)indicesEnd));
        long nameLengthAt = indicesEnd + 4;
        int nameLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)nameLengthAt));
        long nameEnd = nameLengthAt + 4 + nameLength;
        if (nameLength < 0 || nameEnd > recordEnd)
        {
            return false;
        }

        id = trailerId;
        indices = parsedIndices;
        name = nameLength == 0 ? null : Encoding.UTF8.GetString(buffer, (int)nameLengthAt + 4, nameLength);
        return true;
    }

    private static string SanitizeMeshName(string? name, int id)
    {
        if (string.IsNullOrEmpty(name))
        {
            return $"Mesh_{id}";
        }

        var characters = name.Where(c => !(char.IsControl(c) || c is '\u2028' or '\u2029')).ToArray();
        string trimmed = new string(characters).Trim();
        return trimmed.Length == 0 ? $"Mesh_{id}" : trimmed;
    }

    /// <summary>
    /// Consecutive segment pairs over the vertices: (0,1), (1,2), ... — one open polyline. The lenient path does not read the polyline lengths,
    /// so a record that holds several polylines gets a segment between each pair of neighbours.
    /// </summary>
    internal static int[] BuildLineIndices(int vertexCount)
    {
        var indices = new int[Math.Max(0, (vertexCount - 1) * 2)];
        int idx = 0;
        for (int i = 0; i < vertexCount - 1; i++)
        {
            indices[idx++] = i;
            indices[idx++] = i + 1;
        }

        return indices;
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
