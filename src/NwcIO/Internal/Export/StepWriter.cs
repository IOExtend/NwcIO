using System.Globalization;
using System.Text;

namespace NwcIO.Internal.Export;

/// <summary>
/// Low-level IFC STEP physical file writer (ISO 10303-21).
/// Manages entity IDs, argument formatting, Unicode character escaping, and file structure.
/// </summary>
internal sealed class StepWriter
{
    private int _nextId = 1;
    private readonly List<(int Id, string Type, string Args)> _entities = new(4096);

    public int NextId() => _nextId++;

    /// <summary>Adds an entity and returns its assigned #ID.</summary>
    public int Add(string type, string args)
    {
        int id = _nextId++;
        _entities.Add((id, type, args));
        return id;
    }

    public int EntityCount => _entities.Count;

    public (int Id, string Type, string Args) EntityAt(int index) => _entities[index];

    public void ReplaceArgs(int index, string args)
    {
        var (id, type, _) = _entities[index];
        _entities[index] = (id, type, args);
    }

    /// <summary>Writes the complete IFC STEP file to a <see cref="TextWriter"/>.</summary>
    public void WriteTo(
        TextWriter w,
        string fileName,
        string schema = "IFC2X3",
        string description = "ViewDefinition [CoordinationView_V2.0]",
        string application = "NwcIO",
        string organization = "NwcIO",
        string author = "")
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        w.WriteLine("ISO-10303-21;");
        w.WriteLine("HEADER;");
        w.WriteLine($"FILE_DESCRIPTION(('{Escape(description)}'),'2;1');");
        w.WriteLine($"FILE_NAME('{Escape(fileName)}','{now}',('{Escape(author)}'),('{Escape(organization)}'),'{Escape(application)}','NwcIO','');");
        w.WriteLine($"FILE_SCHEMA(('{Escape(schema)}'));");
        w.WriteLine("ENDSEC;");
        w.WriteLine("DATA;");

        for (int i = 0; i < _entities.Count; i++)
        {
            var (id, type, args) = _entities[i];
            w.Write('#');
            w.Write(id);
            w.Write('=');
            w.Write(type);
            w.Write('(');
            w.Write(args);
            w.WriteLine(");");
        }

        w.WriteLine("ENDSEC;");
        w.WriteLine("END-ISO-10303-21;");
    }

    public static string Ref(int id) => $"#{id}";

    public static string Str(string? s) => s is null ? "$" : $"'{Escape(s)}'";

    public static string Enum(string v) => $".{v}.";

    public static string List(params string[] items) => $"({string.Join(",", items)})";

    public static string List(IEnumerable<string> items) => $"({string.Join(",", items)})";

    public static string Real(double v)
    {
        string text = v.ToString("0.######", CultureInfo.InvariantCulture);
        return text.Contains('.') ? text : text + ".";
    }

    public static string RealExact(double v)
    {
        string text = v.ToString("G15", CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('E') && !text.Contains('e'))
        {
            text += ".";
        }
        return text;
    }

    /// <summary>
    /// Escapes string according to ISO 10303-21 STEP rules:
    /// - Extended Unicode characters (e.g. Cyrillic) encoded as \X2\HHHH...\X0\
    /// - Single quotes escaped as ''
    /// - Backslashes escaped as \\
    /// </summary>
    public static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length * 2);
        bool inUnicode = false;

        foreach (char c in s)
        {
            if (c < 0x100 && (c > 0x7E || c < 0x20))
            {
                if (inUnicode) { sb.Append("\\X0\\"); inUnicode = false; }
                sb.Append("\\X\\").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else if (c > 0x7E || c < 0x20)
            {
                if (!inUnicode) { sb.Append("\\X2\\"); inUnicode = true; }
                sb.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                if (inUnicode) { sb.Append("\\X0\\"); inUnicode = false; }
                if (c == '\\') sb.Append("\\\\");
                else if (c == '\'') sb.Append("''");
                else sb.Append(c);
            }
        }

        if (inUnicode)
        {
            sb.Append("\\X0\\");
        }

        return sb.ToString();
    }
}
