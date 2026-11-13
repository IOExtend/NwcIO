using NwcIO.Internal.Parsing;
using NwcIO.Model;
using Xunit;

namespace NwcIO.Tests;

public class FragmentParserTests
{
    [Fact]
    public void Read_F1_DecodesPosesBoundsAndRecords()
    {
        var f = FragmentParser.Read(FormatVectors.F1);
        var box = new BoundingBox3d(new(0, 0, 0), new(1, 1, 0));
        Assert.Equal(2, f.Length);
        Assert.Equal(new Fragment(2, 1, 2, new(10, 0, 0), Quaterniond.Identity, 1.0, box), f[0]);
        Assert.Equal(new Fragment(7, 1, 2, Vector3d.Zero, Quaterniond.Identity, -1.0, box), f[1]);
    }

    // F1 byte edits (offsets from the F1 layout in Appendix A), applied with FormatVectors.Patched
    [Theory]
    [InlineData(252, new[] { 0 })]                 // null fragment pointer
    [InlineData(252, new[] { 1, 100 })]            // fragment pointer as back-reference
    [InlineData(288, new[] { 1, 104 })]            // pose pointer -> class-93 object
    [InlineData(12, new[] { 1 })]                  // non-zero f64 pad
    public void Read_InvalidFragments_Throws(int offset, int[] ints)
    {
        var bytes = FormatVectors.Patched(FormatVectors.F1, offset, ints.SelectMany(BitConverter.GetBytes).ToArray());
        var ex = Assert.Throws<InvalidDataException>(() => FragmentParser.Read(bytes));
        Assert.StartsWith("LcOpNwdFragments at byte ", ex.Message);
    }

    [Theory]
    [InlineData(352, 0.0, "pose scale is 0")]
    [InlineData(352, double.NaN, "pose value is not finite")]
    [InlineData(48, double.PositiveInfinity, "pose value is not finite")]   // class-15 translation x
    [InlineData(96, double.NaN, "pose value is not finite")]                // class-15 rotation w
    public void Read_InvalidPoseValue_Throws(int offset, double value, string rule)
    {
        var bytes = FormatVectors.Patched(FormatVectors.F1, offset, BitConverter.GetBytes(value));
        var ex = Assert.Throws<InvalidDataException>(() => FragmentParser.Read(bytes));
        Assert.StartsWith("LcOpNwdFragments at byte ", ex.Message);
        Assert.EndsWith(rule, ex.Message);
    }

    [Theory]
    [InlineData(188, float.PositiveInfinity)]   // class-93 box min x
    [InlineData(208, float.NaN)]                // class-93 box max z
    public void Read_NonFiniteGeometryBox_Throws(int offset, float value)
    {
        var bytes = FormatVectors.Patched(FormatVectors.F1, offset, BitConverter.GetBytes(value));
        var ex = Assert.Throws<InvalidDataException>(() => FragmentParser.Read(bytes));
        Assert.StartsWith("LcOpNwdFragments at byte ", ex.Message);
        Assert.EndsWith("geometry box value is not finite", ex.Message);
    }

    [Fact]
    public void Read_ZeroBytesAfterTheEnd_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => FragmentParser.Read(FormatVectors.Patched(FormatVectors.F1, -1, new byte[8])));
        Assert.StartsWith("LcOpNwdFragments at byte ", ex.Message);
    }

    [Fact]
    public void LcGraphWriter_RebuildsF1ByteForByte() => Assert.Equal(FormatVectors.F1, BuildF1());

    // each variant would parse under a reader that allows it, so only the observed-slot rule rejects it
    [Theory]
    [InlineData("sharedPose")]      // fragment 1's pose = Ref(101), fragment 0's finished class-15 pose
    [InlineData("appearanceNull")]  // fragment 1's appearance = Null()
    [InlineData("materialNull")]    // class 55 with a null material and no class-54 object
    public void Read_ObservedSlotRuleBroken_Throws(string defect)
    {
        var ex = Assert.Throws<InvalidDataException>(() => FragmentParser.Read(BuildF1(defect)));
        Assert.StartsWith("LcOpNwdFragments at byte ", ex.Message);
    }

    // flags & 32 adds a class-185 slot after the material; null and a new class 185 both read as F1 does
    [Theory]
    [InlineData("extra185")]
    [InlineData("extraNull")]
    public void Read_AppearanceWithExtraSlot_ReadsLikeF1(string defect) =>
        Assert.Equal(FragmentParser.Read(FormatVectors.F1), FragmentParser.Read(BuildF1(defect)));

    // The F1 layout of Appendix A; a defect changes the shared class 55 or fragment 1 as its row says.
    private static byte[] BuildF1(string? defect = null)
    {
        var w = new LcGraphWriter();
        w.Int(2);
        w.New(13);
        w.Double(1.0).Int(2).Int(4).Int(0).Int(2);
        int pose = w.New(15);
        w.Double(10).Double(0).Double(0);
        w.Double(0).Double(0).Double(0).Double(1);
        int appearance = w.New(55);
        Appearance(w, defect);
        int geometry = w.New(93);
        w.Float(0).Float(0).Float(0).Float(1).Float(1).Float(0);
        w.Double(0.5).Double(0.5).Double(0);
        w.Float(0).Int(7).Int(1);

        w.New(13);
        w.Double(1.0).Int(2).Int(4).Int(0).Int(7);
        if (defect == "sharedPose")
        {
            w.Ref(pose);
        }
        else
        {
            w.New(16);
            w.Double(0).Double(0).Double(0);
            w.Double(0).Double(0).Double(0).Double(1);
            w.Double(-1);
        }

        if (defect == "appearanceNull")
        {
            w.Null();
        }
        else
        {
            w.Ref(appearance);
        }

        w.Ref(geometry);
        return w.ToArray();
    }

    // Class-55 body: [flags][ptr 54], then [ptr 185 or null] when flags & 32.
    private static void Appearance(LcGraphWriter w, string? defect)
    {
        w.Int(defect is "extra185" or "extraNull" ? 33 : 1);
        if (defect == "materialNull")
        {
            w.Null();
            return;
        }

        w.New(54);
        for (int i = 0; i < 14; i++)
        {
            w.Int(0);
        }

        if (defect == "extra185")
        {
            w.New(185);
            w.Str("j").Str("i").Int(0);
        }
        else if (defect == "extraNull")
        {
            w.Null();
        }
    }
}
