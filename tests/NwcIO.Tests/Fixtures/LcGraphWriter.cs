using System.Buffers.Binary;
using System.Text;

namespace NwcIO.Tests;

/// <summary>Writes object-graph streams byte for byte as <c>LcObjectReader</c> reads them (RS §2).</summary>
internal sealed class LcGraphWriter
{
    private readonly List<byte> _bytes = [];

    /// <summary>The id the next new object takes.</summary>
    public int Next { get; private set; } = 100;

    public LcGraphWriter Int(int v)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, v);
        _bytes.AddRange(bytes);
        return this;
    }

    public LcGraphWriter Float(float v)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, v);
        _bytes.AddRange(bytes);
        return this;
    }

    public LcGraphWriter Double(double v)
    {
        Align8();
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(bytes, v);
        _bytes.AddRange(bytes);
        return this;
    }

    public LcGraphWriter Long(long v)
    {
        Align8();
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, v);
        _bytes.AddRange(bytes);
        return this;
    }

    /// <summary>[len][UTF-8][zero pad to 4]; null writes [-1].</summary>
    public LcGraphWriter Str(string? v)
    {
        if (v is null)
        {
            return Int(-1);
        }

        var bytes = Encoding.UTF8.GetBytes(v);
        Int(bytes.Length);
        _bytes.AddRange(bytes);
        _bytes.AddRange(new byte[(4 - (bytes.Length % 4)) % 4]);
        return this;
    }

    /// <summary>Writes [Next][classId] and returns the new object's id.</summary>
    public int New(int classId)
    {
        int id = Next++;
        Int(id).Int(classId);
        return id;
    }

    /// <summary>Writes [Next], the hierarchy's implicit root, and returns its id.</summary>
    public int ImplicitRoot()
    {
        int id = Next++;
        Int(id);
        return id;
    }

    public LcGraphWriter Ref(int id) => Int(1).Int(id);

    public LcGraphWriter Null() => Int(0);

    public byte[] ToArray(bool padTo8 = true)
    {
        var data = _bytes.ToArray();
        return padTo8 ? [.. data, .. new byte[(8 - (data.Length % 8)) % 8]] : data;
    }

    // f64 and i64 are 8-aligned from the stream start; the stream only ever stands at 0 or 4 mod 8.
    private void Align8()
    {
        if (_bytes.Count % 8 == 4)
        {
            Int(0);
        }
    }
}
