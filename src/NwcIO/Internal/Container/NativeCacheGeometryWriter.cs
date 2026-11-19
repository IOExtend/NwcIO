using NwcIO.Model;

namespace NwcIO.Internal.Container;

internal static class NativeCacheGeometryWriter
{
    private const int ChunkSize = 65536;
    private const string SegmentName = @"cache\LcOpNwdGeometry";

    public static (byte[] Data, BoundingBox3d Bounds) WriteMesh(NwcMesh mesh, double feetPerUnit)
    {
        if (mesh.LineIndices.Count != 0 || mesh.Indices.Count == 0 || mesh.Indices.Count % 3 != 0)
            throw new ArgumentException("Native NWC output requires nonempty triangle lists.", nameof(mesh));

        using var record = new NativeCacheBinaryWriter();
        int vertexCount = mesh.Indices.Count;
        var vertices = new Vector3d[vertexCount];
        var min = new Vector3d(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        var max = new Vector3d(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        record.Write(100);
        record.Write(94);
        record.Write(101);
        record.Write(60);
        record.Write(vertexCount);
        record.Write(checked(3 * vertexCount));
        for (int i = 0; i < vertexCount; i++)
        {
            int index = mesh.Indices[i];
            if ((uint)index >= (uint)mesh.Vertices.Count)
                throw new ArgumentException("A native NWC triangle index is outside its vertex buffer.", nameof(mesh));

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
        int triangles = vertexCount / 3;
        record.Write(triangles);
        record.Write(2);
        record.Write(triangles);
        for (int i = 0; i < triangles / 16; i++)
            record.Write(uint.MaxValue);
        if (triangles % 16 != 0)
            record.Write(uint.MaxValue << (32 - 2 * (triangles % 16)));
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
