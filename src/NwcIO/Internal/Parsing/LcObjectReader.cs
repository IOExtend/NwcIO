using System.Buffers.Binary;
using System.Text;
using System.Text.Unicode;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Strict reader of one object-graph stream (RS §2): little-endian primitives, f64 and i64 aligned to 8 from the stream
/// start, zero-padded UTF-8 strings, and pointers that are null, a back-reference to a finished object or a new object.
/// </summary>
internal sealed class LcObjectReader
{
    // shortcut: fixed cap on pointer nesting and expanded tree height, so parsing and the recursive tree walkers (MaterialParser, JsonExporter, diff, sets) cannot overflow the stack; raise it if a measured real tree comes near it
    public const int MaxNestingDepth = 256;

    private const int FirstId = 100;

    private readonly byte[] _data;
    private readonly string _segment;

    // Index = id - FirstId; Body stays null until the object's body is finished.
    private readonly List<(int ClassId, object? Body)> _objects = [];
    private int _depth;

    public LcObjectReader(byte[] data, string segment)
    {
        _data = data;
        _segment = segment;
    }

    public int Position { get; private set; }

    /// <summary>The id the next new object takes; inside a body, the body's own id is <c>Next - 1</c> until it reads a pointer.</summary>
    public int Next => FirstId + _objects.Count;

    public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4, "i32"));

    public float ReadSingle() => BinaryPrimitives.ReadSingleLittleEndian(Take(4, "f32"));

    public double ReadDouble()
    {
        Align8("f64");
        return BinaryPrimitives.ReadDoubleLittleEndian(Take(8, "f64"));
    }

    public long ReadInt64()
    {
        Align8("i64");
        return BinaryPrimitives.ReadInt64LittleEndian(Take(8, "i64"));
    }

    /// <summary>[len][UTF-8 bytes][zero pad to 4]; a length of -1 is null.</summary>
    public string? ReadString()
    {
        int length = ReadInt32();
        if (length == -1)
        {
            return null;
        }

        int start = Position;
        SkipPadded(length);
        var bytes = _data.AsSpan(start, length);
        if (!Utf8.IsValid(bytes))
        {
            throw Fail("string is not valid UTF-8");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Skips <paramref name="length"/> bytes and the zero pad after them to a multiple of 4.</summary>
    public void SkipPadded(long length)
    {
        if (length < 0)
        {
            throw Fail($"byte length {length} is negative");
        }

        int remaining = _data.Length - Position;
        if (length > remaining || ((length + 3) & ~3) > remaining)
        {
            throw Fail($"{length} bytes pass the end of the data");
        }

        int end = Position + (int)length;
        int padded = (int)((length + 3) & ~3);
        if (_data.AsSpan(end, padded - (int)length).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw Fail("pad after the bytes is not zero");
        }

        Position += padded;
    }

    /// <summary>Reads a count that is not negative and whose items, each at least <paramref name="minBytesPerItem"/> bytes, fit in the remaining data.</summary>
    public int ReadCount(int minBytesPerItem)
    {
        int count = ReadInt32();
        if (count < 0 || (long)count * minBytesPerItem > _data.Length - Position)
        {
            throw Fail($"count {count} is negative or exceeds the remaining bytes");
        }

        return count;
    }

    /// <summary>The hierarchy's implicit root (RS §2.1): an id with no class and no body, never finished.</summary>
    public void ReadImplicitRoot()
    {
        int expected = Next;
        if (ReadInt32() != expected)
        {
            throw Fail($"implicit root id is not {expected}");
        }

        _objects.Add((0, null));
    }

    /// <summary>
    /// Reads a pointer slot (RS §2.1): <c>0</c> null, <c>[1][id]</c> a finished object, <c>[Next][class]</c> a new object
    /// whose body <paramref name="readBody"/> reads from its class id. Only <paramref name="allowedClasses"/> may fill the slot.
    /// </summary>
    public object? ReadPointer(
        ReadOnlySpan<int> allowedClasses,
        Func<int, object> readBody,
        bool allowNull = true,
        bool allowBackReference = true)
    {
        int lead = ReadInt32();
        if (lead == 0)
        {
            return allowNull ? null : throw Fail("null pointer in a slot that needs an object");
        }

        if (lead == 1)
        {
            if (!allowBackReference)
            {
                throw Fail("back-reference in a slot that needs a new object");
            }

            int id = ReadInt32();
            if (id < FirstId || id >= Next)
            {
                throw Fail($"back-reference to unknown id {id}");
            }

            var (classId, body) = _objects[id - FirstId];
            if (body is null)
            {
                throw Fail($"back-reference to unfinished object {id}");
            }

            if (!allowedClasses.Contains(classId))
            {
                throw Fail($"back-reference to class {classId}, which this slot does not allow");
            }

            return body;
        }

        if (lead != Next)
        {
            throw Fail($"pointer {lead} is neither 0, 1 nor the next id {Next}");
        }

        int newClass = ReadInt32();
        if (!allowedClasses.Contains(newClass))
        {
            throw Fail($"class {newClass}, which this slot does not allow");
        }

        if (_depth == MaxNestingDepth)
        {
            throw Fail($"nesting deeper than {MaxNestingDepth}");
        }

        int index = _objects.Count;
        _objects.Add((newClass, null));
        _depth++;
        object read = readBody(newClass);
        _depth--;
        _objects[index] = (newClass, read);
        return read;
    }

    /// <summary>Zero bytes up to a multiple of 8, then the end of the data.</summary>
    public void ReadEnd()
    {
        if (_data.Length != ((Position + 7) & ~7))
        {
            throw Fail("data does not end at the zero pad to a multiple of 8");
        }

        if (_data.AsSpan(Position).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw Fail("end pad is not zero");
        }

        Position = _data.Length;
    }

    public InvalidDataException Fail(string rule) => new($"{_segment} at byte {Position}: {rule}");

    private ReadOnlySpan<byte> Take(int length, string what)
    {
        if (_data.Length - Position < length)
        {
            throw Fail($"{what} passes the end of the data");
        }

        var span = _data.AsSpan(Position, length);
        Position += length;
        return span;
    }

    // Every read keeps the cursor at 0 or 4 mod 8, so one zero i32 is the whole pad.
    private void Align8(string what)
    {
        if (Position % 8 == 4 && ReadInt32() != 0)
        {
            throw Fail($"{what} alignment pad is not zero");
        }
    }
}
