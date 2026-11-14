using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// One class-13 fragment (docs/format-notes.md §2.4): <paramref name="Seq"/> is its leaf's expanded preorder index, <paramref name="Record"/>
/// the 1-based geometry record (class-93 <c>rec</c>), <paramref name="PrimitiveCount"/> the fragment int <c>a</c>, <paramref name="Look"/>
/// the colour of its appearance, and the pose places the record's positions as <c>Scale * R(Rotation) * v + Translation</c>.
/// </summary>
internal readonly record struct Fragment(
    int Seq,
    int Record,
    int PrimitiveCount,
    Vector3d Translation,
    Quaterniond Rotation,
    double Scale,
    BoundingBox3d Bounds,
    FragmentLook Look);

/// <summary>The class-54 diffuse colour (floats 3, 4, 5) and transparency (float 13) of a fragment's appearance (docs/format-notes.md §2.4).</summary>
internal readonly record struct FragmentLook(float R, float G, float B, float Transparency);

/// <summary>Strict reader of the <c>LcOpNwdFragments</c> object graph (docs/format-notes.md §2.2, §2.4).</summary>
internal static class FragmentParser
{
    private const string Segment = "LcOpNwdFragments";

    // Slot families as observed (docs/format-notes.md §2.4).
    private static ReadOnlySpan<int> FragmentClasses => [13];

    private static ReadOnlySpan<int> PoseClasses => [15, 16];

    private static ReadOnlySpan<int> AppearanceClasses => [55];

    private static ReadOnlySpan<int> MaterialClasses => [54];

    private static ReadOnlySpan<int> ExtraClasses => [185];

    private static ReadOnlySpan<int> GeometryClasses => [93];

    public static Fragment[] Read(byte[] data) => new Parser(new LcObjectReader(data, Segment)).ReadStream();

    private sealed record Pose(Vector3d Translation, Quaterniond Rotation, double Scale);

    private sealed record GeometryReference(BoundingBox3d Bounds, int Record);

    private sealed class Parser
    {
        // A fragment takes at least [id][13], D, four ints, a new class-15 pose and two back-references.
        private const int MinFragmentBytes = 8 + 8 + 16 + 8 + 56 + 8 + 8;

        private readonly LcObjectReader _reader;
        private readonly Func<int, object> _readBody;

        public Parser(LcObjectReader reader)
        {
            _reader = reader;
            _readBody = ReadBody;
        }

        // [n] PTR{n}, each a new class 13, then the zero pad to 8 (docs/format-notes.md §2.4).
        public Fragment[] ReadStream()
        {
            var fragments = new Fragment[_reader.ReadCount(MinFragmentBytes)];
            for (int i = 0; i < fragments.Length; i++)
            {
                fragments[i] = (Fragment)_reader.ReadPointer(FragmentClasses, _readBody, allowNull: false, allowBackReference: false)!;
            }

            _reader.ReadEnd();
            return fragments;
        }

        private object ReadBody(int classId) => classId switch
        {
            13 => ReadFragment(),
            15 => ReadPose(scaled: false),
            16 => ReadPose(scaled: true),
            55 => ReadAppearance(),
            54 => ReadMaterial(),
            93 => ReadGeometry(),
            185 => HierarchyParser.ReadMaterialJson(_reader),
            _ => throw new InvalidOperationException($"class {classId} has no body reader"),
        };

        // Class 13: [f64 D][a][b][c][seq][ptr pose][ptr appearance][ptr geometry]; D, b and c are dropped.
        private Fragment ReadFragment()
        {
            _reader.ReadDouble();
            int primitiveCount = _reader.ReadInt32();
            _reader.ReadInt32();
            _reader.ReadInt32();
            int seq = _reader.ReadInt32();

            // A pose is never shared, so it is always a new object.
            var pose = (Pose)_reader.ReadPointer(PoseClasses, _readBody, allowNull: false, allowBackReference: false)!;
            var look = (FragmentLook)_reader.ReadPointer(AppearanceClasses, _readBody, allowNull: false)!;
            var geometry = (GeometryReference)_reader.ReadPointer(GeometryClasses, _readBody, allowNull: false)!;
            return new Fragment(seq, geometry.Record, primitiveCount, pose.Translation, pose.Rotation, pose.Scale, geometry.Bounds, look);
        }

        // Class 15: [3 f64 t][4 f64 q xyzw]; class 16 adds [f64 s].
        private Pose ReadPose(bool scaled)
        {
            var translation = new Vector3d(ReadFinite(), ReadFinite(), ReadFinite());
            var rotation = new Quaterniond(ReadFinite(), ReadFinite(), ReadFinite(), ReadFinite());
            double scale = scaled ? ReadFinite() : 1.0;
            if (scale == 0)
            {
                throw _reader.Fail("pose scale is 0");
            }

            return new Pose(translation, rotation, scale);
        }

        private double ReadFinite()
        {
            double value = _reader.ReadDouble();
            return double.IsFinite(value) ? value : throw _reader.Fail("pose value is not finite");
        }

        // Class 55: [flags][ptr 54], then [ptr 185 or null] when flags & 32; the look is its material's, the extra is dropped.
        private FragmentLook ReadAppearance()
        {
            int flags = _reader.ReadInt32();
            var look = (FragmentLook)_reader.ReadPointer(MaterialClasses, _readBody, allowNull: false)!;
            if ((flags & 32) != 0)
            {
                _reader.ReadPointer(ExtraClasses, _readBody);
            }

            return look;
        }

        // Class 54: 14 x f32: ambient 0-2, diffuse 3-5, specular 6-8, emissive 9-11, shininess 12, transparency 13.
        private FragmentLook ReadMaterial()
        {
            Span<float> v = stackalloc float[14];
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = _reader.ReadSingle();
                if (!float.IsFinite(v[i]))
                {
                    throw _reader.Fail("class-54 value is not finite");
                }
            }

            return new FragmentLook(v[3], v[4], v[5], v[13]);
        }

        // Class 93: [6 f32 bbox: min xyz, max xyz][3 f64 c][f32 f][key][rec]; c, f and key are dropped.
        private GeometryReference ReadGeometry()
        {
            var min = new Vector3d(ReadBoxValue(), ReadBoxValue(), ReadBoxValue());
            var max = new Vector3d(ReadBoxValue(), ReadBoxValue(), ReadBoxValue());
            for (int i = 0; i < 3; i++)
            {
                _reader.ReadDouble();
            }

            _reader.ReadSingle();
            _reader.ReadInt32();
            return new GeometryReference(new BoundingBox3d(min, max), _reader.ReadInt32());
        }

        private float ReadBoxValue()
        {
            float value = _reader.ReadSingle();
            return float.IsFinite(value) ? value : throw _reader.Fail("geometry box value is not finite");
        }
    }
}
