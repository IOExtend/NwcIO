using NwcIO.Model;

namespace NwcIO.Internal.Container;

internal static class NativeCacheGeometryWriter
{
    private const int ChunkSize = 65536;
    private const string SegmentName = @"cache\LcOpNwdGeometry";

    public static (byte[] Data, BoundingBox3d Bounds) WriteMesh(NwcMesh mesh, double feetPerUnit)
    {
        bool lines = mesh.LineIndices.Count != 0;
        IReadOnlyList<int> indices = lines ? mesh.LineIndices : mesh.Indices;
        int verticesPerPrimitive = lines ? 2 : 3;
        if (indices.Count == 0 || indices.Count % verticesPerPrimitive != 0 || (lines && mesh.Indices.Count != 0))
            throw new ArgumentException("Native NWC output requires a nonempty triangle or line list per mesh.", nameof(mesh));
        if (mesh.LineColors is { } colors && (!lines || colors.Count != mesh.Vertices.Count))
            throw new ArgumentException("Native NWC line colors must match the source vertex count.", nameof(mesh));

        using var record = new NativeCacheBinaryWriter();
        int vertexCount = indices.Count;
        var vertices = new Vector3d[vertexCount];
        var min = new Vector3d(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new Vector3d(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        record.Write(100);
        record.Write(lines ? 95 : 94);
        record.Write(101);
        record.Write(60);
        record.Write(vertexCount);
        record.Write(checked(3 * vertexCount));
        for (int i = 0; i < vertexCount; i++)
        {
            int index = indices[i];
            if ((uint)index >= (uint)mesh.Vertices.Count)
                throw new ArgumentException("A native NWC index is outside its vertex buffer.", nameof(mesh));

            Vector3d position = mesh.Vertices[index] * feetPerUnit;
            float x = (float)position.X, y = (float)position.Y, z = (float)position.Z;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                throw new ArgumentException("A native NWC vertex is outside the finite single-precision range.", nameof(mesh));

            record.Write(x);
            record.Write(y);
            record.Write(z);
            vertices[i] = new(x, y, z);
            min = new(Math.Min(min.X, x), Math.Min(min.Y, y), Math.Min(min.Z, z));
            max = new(Math.Max(max.X, x), Math.Max(max.Y, y), Math.Max(max.Z, z));
        }

        record.Write(0); // UV slot
        if (lines)
        {
            record.Write(0); // Normal slot
            WriteLineColors(record, mesh);
            WritePrimitiveLengths(record, vertexCount / 2, 2);
            return (record.Finish(), new(min, max));
        }

        record.Write(102);
        record.Write(100);
        record.Write(-vertexCount);
        record.Write(vertexCount);
        uint pendingWord = 0;
        bool highHalf = true;
        for (int i = 0; i < vertexCount; i += 3)
        {
            Vector3d normal = Vector3d.Cross(vertices[i + 1] - vertices[i], vertices[i + 2] - vertices[i]).Normalize();
            ushort x = PackNormal(normal.X), y = PackNormal(normal.Y), z = PackNormal(normal.Z);
            for (int corner = 0; corner < 3; corner++)
            {
                WriteNormal(x);
                WriteNormal(y);
                WriteNormal(z);
            }
        }

        if (!highHalf)
            record.Write(pendingWord);

        record.Write(0); // colour slot
        // Each triangle is an independent strip with three render vertices in index order.
        WritePrimitiveLengths(record, vertexCount / 3, 3);
        record.Write(0); // Corner vertices are already in strip order.
        return (record.Finish(), new(min, max));

        void WriteNormal(ushort part)
        {
            if (highHalf)
                pendingWord = (uint)part << 16;
            else
                record.Write(pendingWord | part);
            highHalf = !highHalf;
        }
    }

    private static void WriteLineColors(NativeCacheBinaryWriter record, NwcMesh mesh)
    {
        if (mesh.LineColors is not { } colors)
        {
            record.Write(0);
            return;
        }

        record.Write(102);
        record.Write(58);
        record.Write(5634);
        record.Write(-mesh.LineIndices.Count);
        record.Write(mesh.LineIndices.Count);
        foreach (int index in mesh.LineIndices)
        {
            var color = colors[index];
            record.Write(PackColor(color.A));
            record.Write(PackColor(color.B));
            record.Write(PackColor(color.G));
            record.Write(PackColor(color.R));
        }

        static byte PackColor(float channel) =>
            float.IsFinite(channel) && channel is >= 0 and <= 1
                ? (byte)Math.Round(channel * 255)
                : throw new ArgumentException("Native NWC line color channels must be finite and between 0 and 1.");
    }

    private static void WritePrimitiveLengths(NativeCacheBinaryWriter record, int count, int length)
    {
        record.Write(count);
        record.Write(2);
        record.Write(count);
        uint packed = 0x55555555u * (uint)length;
        for (int i = 0; i < count / 16; i++)
            record.Write(packed);
        if (count % 16 != 0)
            record.Write(packed & (uint.MaxValue << (32 - 2 * (count % 16))));
    }

    public static NativeCacheSegment BuildSegment(MemoryStream geometry, IReadOnlyList<int> recordLengths)
    {
        using var segment = new NativeCacheBinaryWriter();
        using var toc = new NativeCacheBinaryWriter();
        segment.Write(ChunkSize);
        ReadOnlySpan<byte> bytes = geometry.GetBuffer().AsSpan(0, checked((int)geometry.Length));
        int chunks = 1 + (bytes.Length - 1) / ChunkSize;
        toc.Write(recordLengths.Count);
        toc.Write(chunks);
        while (!bytes.IsEmpty)
        {
            int size = Math.Min(ChunkSize, bytes.Length);
            byte[] compressed = NativeCacheContainerWriter.Compress(bytes[..size]);
            segment.Write(compressed);
            toc.Write(compressed.Length);
            bytes = bytes[size..];
        }

        foreach (int length in recordLengths)
            toc.Write(length);
        byte[] compressedToc = NativeCacheContainerWriter.Compress(toc.Finish());
        segment.Write(compressedToc);
        return new(SegmentName, ((MemoryStream)segment.BaseStream).ToArray(), PrefixBytes: 4, TocBytes: compressedToc.Length);
    }

    private static ushort PackNormal(double value) => unchecked((ushort)(short)Math.Round(value * 32767));
}
