using NwcIO.Model;

namespace NwcIO.Tests;

/// <summary>Class 55 [i32 1][new 54: ambient R,G,B, diffuse R,G,B, specular 0,0,0, emissive 0,0,0, Shininess, Transparency] as 14 f32.</summary>
internal sealed record Look(float R, float G, float B, float Shininess, float Transparency);

/// <summary>One fragment: S 1 writes a class-15 pose, else class 16; a new class 93 with bbox (0,0,0)-(MaxX,MaxY,MaxZ), or a back-reference
/// to fragment SharesGeometryOf's. Look writes a new appearance; null back-references fragment SharesLookOf's (fragment 0 writes Look(0,0,0,0,0)).</summary>
internal sealed record Frag(int Seq, Vector3d T, double S, int A, int B, int Rec = 0, float MaxZ = 0, int SharesGeometryOf = -1,
                            float MaxX = 1, float MaxY = 1, Look? Look = null, int SharesLookOf = 0);

/// <summary>Writes <c>LcOpNwdFragments</c> streams in the F1 layout (docs/format-notes.md §2.4).</summary>
internal static class SyntheticFragments
{
    public static byte[] Build(params Frag[] fragments)
    {
        var w = new LcGraphWriter();
        w.Int(fragments.Length);
        var appearance = new int[fragments.Length];
        var geometry = new int[fragments.Length];
        for (int k = 0; k < fragments.Length; k++)
        {
            var f = fragments[k];
            w.New(13);
            w.Double(1.0).Int(f.A).Int(f.B).Int(0).Int(f.Seq);
            w.New(f.S == 1 ? 15 : 16);
            w.Double(f.T.X).Double(f.T.Y).Double(f.T.Z);
            w.Double(0).Double(0).Double(0).Double(1);
            if (f.S != 1)
            {
                w.Double(f.S);
            }

            var look = f.Look ?? (k == 0 ? new Look(0, 0, 0, 0, 0) : null);
            if (look is null)
            {
                appearance[k] = appearance[f.SharesLookOf];
                w.Ref(appearance[k]);
            }
            else
            {
                appearance[k] = w.New(55);
                w.Int(1);
                w.New(54);
                foreach (float v in (float[])[look.R, look.G, look.B, look.R, look.G, look.B, 0, 0, 0, 0, 0, 0, look.Shininess, look.Transparency])
                {
                    w.Float(v);
                }
            }

            if (f.SharesGeometryOf >= 0)
            {
                geometry[k] = geometry[f.SharesGeometryOf];
                w.Ref(geometry[k]);
            }
            else
            {
                geometry[k] = w.New(93);
                w.Float(0).Float(0).Float(0).Float(f.MaxX).Float(f.MaxY).Float(f.MaxZ);
                w.Double(0.5).Double(0.5).Double(0);
                w.Float(0).Int(7).Int(f.Rec);
            }
        }

        return w.ToArray();
    }
}
