using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// One class-13 fragment (RS §5): <paramref name="Seq"/> is its leaf's expanded preorder index, <paramref name="Record"/>
/// the 1-based geometry record (class-93 <c>rec</c>), <paramref name="PrimitiveCount"/> the fragment int <c>a</c>, and
/// the pose places the record's positions as <c>Scale * R(Rotation) * v + Translation</c>.
/// </summary>
internal readonly record struct Fragment(
    int Seq,
    int Record,
    int PrimitiveCount,
    Vector3d Translation,
    Quaterniond Rotation,
    double Scale,
    BoundingBox3d Bounds);

/// <summary>Strict reader of the <c>LcOpNwdFragments</c> object graph (RS §2.3, §5).</summary>
internal static class FragmentParser
{
    private const string Segment = "LcOpNwdFragments";

    // Appearance, material and extra bodies are parsed and dropped.
    private static readonly object Dropped = new();

    // Slot families as observed (RS §5).
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

        // [n] PTR{n}, each a new class 13, then the zero pad to 8 (RS §2.3).
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

        // Class 13: [f64 D][a][b][c][seq][ptr pose][ptr appearance][ptr geometry]; D, b, c and the appearance are dropped.
        private Fragment ReadFragment()
        {
            _reader.ReadDouble();
            int primitiveCount = _reader.ReadInt32();
            _reader.ReadInt32();
            _reader.ReadInt32();
            int seq = _reader.ReadInt32();

            // A pose is never shared, so it is always a new object.
            var pose = (Pose)_reader.ReadPointer(PoseClasses, _readBody, allowNull: false, allowBackReference: false)!;
            _reader.ReadPointer(AppearanceClasses, _readBody, allowNull: false);
            var geometry = (GeometryReference)_reader.ReadPointer(GeometryClasses, _readBody, allowNull: false)!;
            return new Fragment(seq, geometry.Record, primitiveCount, pose.Translation, pose.Rotation, pose.Scale, geometry.Bounds);
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

        // Class 55: [flags][ptr 54], then [ptr 185 or null] when flags & 32.
        private object ReadAppearance()
        {
            int flags = _reader.ReadInt32();
            _reader.ReadPointer(MaterialClasses, _readBody, allowNull: false);
            if ((flags & 32) != 0)
            {
                _reader.ReadPointer(ExtraClasses, _readBody);
            }

            return Dropped;
        }

        // Class 54: 14 x i32.
        private object ReadMaterial()
        {
            for (int i = 0; i < 14; i++)
            {
                _reader.ReadInt32();
            }

            return Dropped;
        }

        // Class 93: [6 f32 bbox: min xyz, max xyz][3 f64 c][f32 f][key][rec]; c, f and key are dropped.
        private GeometryReference ReadGeometry()
        {
            var min = new Vector3d(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
            var max = new Vector3d(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
            for (int i = 0; i < 3; i++)
            {
                _reader.ReadDouble();
            }

            _reader.ReadSingle();
            _reader.ReadInt32();
            return new GeometryReference(new BoundingBox3d(min, max), _reader.ReadInt32());
        }
    }
}
