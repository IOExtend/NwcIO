using System.Buffers.Binary;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses 3D transformation streams (<see cref="NwcStreamKind.Transforms"/>):
/// decodes element poses (translation, rotation quaternion) and local bounding boxes.
/// </summary>
internal static class TransformParser
{
    private const int TagTransform = 13;
    private const int TagPose = 15;
    private const int TagBoundingBox = 93;

    public static List<NwcTransform> Parse(LcRawStream stream) => Parse(stream.Data);

    public static List<NwcTransform> Parse(byte[] data)
    {
        var transforms = new List<NwcTransform>();
        if (data.Length < 16)
        {
            return transforms;
        }

        int pos = 4;
        while (pos + 8 <= data.Length)
        {
            int recId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
            int tag = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos + 4));

            if (tag == TagTransform)
            {
                int pInner = pos + 8;
                Vector3d translation = Vector3d.Zero;
                Quaterniond rotation = Quaterniond.Identity;
                BoundingBox3d? bbox = null;

                while (pInner + 8 <= data.Length)
                {
                    int nextTag = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pInner + 4));
                    if (nextTag == TagTransform)
                    {
                        break;
                    }

                    if (nextTag == TagPose && pInner + 64 <= data.Length)
                    {
                        double tx = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pInner + 8));
                        double ty = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pInner + 16));
                        double tz = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pInner + 24));
                        double qx = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pInner + 32));
                        double qy = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pInner + 40));
                        double qz = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pInner + 48));
                        double qw = BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(pInner + 56));

                        translation = new Vector3d(tx, ty, tz);
                        rotation = new Quaterniond(qx, qy, qz, qw);
                        pInner += 64;
                        continue;
                    }

                    if (nextTag == TagBoundingBox && pInner + 32 <= data.Length)
                    {
                        float minX = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pInner + 8));
                        float minY = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pInner + 12));
                        float minZ = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pInner + 16));
                        float maxX = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pInner + 20));
                        float maxY = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pInner + 24));
                        float maxZ = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(pInner + 28));

                        if (float.IsFinite(minX) && float.IsFinite(minY) && float.IsFinite(minZ) &&
                            float.IsFinite(maxX) && float.IsFinite(maxY) && float.IsFinite(maxZ))
                        {
                            bbox = new BoundingBox3d(
                                new Vector3d(minX, minY, minZ),
                                new Vector3d(maxX, maxY, maxZ));
                        }

                        pInner += 32;
                        continue;
                    }

                    pInner += 4;
                }

                transforms.Add(new NwcTransform
                {
                    Id = recId,
                    Translation = translation,
                    Rotation = rotation,
                    BoundingBox = bbox,
                });

                pos = pInner;
            }
            else
            {
                pos += 4;
            }
        }

        return transforms;
    }
}
