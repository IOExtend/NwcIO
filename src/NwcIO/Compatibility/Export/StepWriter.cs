using System.Globalization;
using System.Text;

namespace NwcIO.Internal.Export;

/// <summary>
/// Streaming IFC STEP physical file writer (ISO 10303-21). The header is written on construction and each
/// entity is written as it is added, so memory does not grow with the model.
/// </summary>
internal sealed class StepWriter
{
    private readonly TextWriter _writer;
    private int _nextId = 1;

    public StepWriter(
        TextWriter writer,
        string fileName,
        string schema = "IFC2X3",
        string description = "ViewDefinition [CoordinationView_V2.0]",
        string application = "NwcIO",
        string organization = "NwcIO",
        string author = "")
    {
        _writer = writer;
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        _writer.WriteLine("ISO-10303-21;");
        _writer.WriteLine("HEADER;");
        _writer.WriteLine($"FILE_DESCRIPTION(('{Escape(description)}'),'2;1');");
        _writer.WriteLine($"FILE_NAME('{Escape(fileName)}','{now}',('{Escape(author)}'),('{Escape(organization)}'),'{Escape(application)}','NwcIO','');");
        _writer.WriteLine($"FILE_SCHEMA(('{Escape(schema)}'));");
        _writer.WriteLine("ENDSEC;");
        _writer.WriteLine("DATA;");
    }

    public int NextId() => _nextId++;

    /// <summary>Adds an entity and writes it to the output.</summary>
    public int Add(string type, string args)
    {
        int id = _nextId++;
        _writer.Write('#');
        _writer.Write(id);
        _writer.Write('=');
        _writer.Write(type);
        _writer.Write('(');
        _writer.Write(args);
        _writer.WriteLine(");");
        return id;
    }

    /// <summary>Writes the file trailer; no entities can be added afterwards.</summary>
    public void Complete()
    {
        _writer.WriteLine("ENDSEC;");
        _writer.WriteLine("END-ISO-10303-21;");
        _writer.Flush();
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
