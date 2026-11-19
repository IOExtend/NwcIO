using System.Text;

namespace NwcIO.Internal.Container;

internal sealed class NativeCacheBinaryWriter() : BinaryWriter(new MemoryStream())
{
    private static readonly byte[] Padding = new byte[8];
    private int _nextId = 100;

    public int NextId() => _nextId++;

    public int NewObject(int type)
    {
        int id = NextId();
        Write(id);
        Write(type);
        return id;
    }

    public void Align(int boundary)
    {
        int count = (int)(-BaseStream.Position & (boundary - 1));
        Write(Padding.AsSpan(0, count));
    }

    public void WriteText(string? text)
    {
        if (text is null)
        {
            Write(-1);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Write(bytes.Length);
        Write(bytes);
        Align(4);
    }

    public void WriteDouble(double value)
    {
        Align(8);
        Write(value);
    }

    public byte[] Finish()
    {
        Align(8);
        return ((MemoryStream)BaseStream).ToArray();
    }
}
