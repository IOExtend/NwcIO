using System.Text;
using System.Text.Json;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses the JSON XRefTable embedded in the <see cref="NwcStreamKind.XRefJson"/> stream.
/// </summary>
public static class XRefTableParser
{
    public static NwcXRefTable? Parse(LcRawStream stream)
    {
        return Parse(stream.Data);
    }

    public static NwcXRefTable? Parse(byte[] data)
    {
        if (data.Length < 10)
        {
            return null;
        }

        // Find the start of the JSON object by the literal StreamClassifier recognizes the stream by: the binary
        // prefix before it can hold a '{' byte. Without that literal, fall back to the first '{'.
        int start = data.AsSpan().IndexOf("{\"Type\":"u8);
        if (start < 0)
        {
            start = data.AsSpan().IndexOf((byte)'{');
        }

        if (start < 0)
        {
            return null;
        }

        int length = data.Length - start;
        // Strip trailing null bytes
        while (length > 0 && data[start + length - 1] == 0)
        {
            length--;
        }

        if (length <= 0)
        {
            return null;
        }

        try
        {
            var memory = data.AsMemory(start, length);
            using var doc = JsonDocument.Parse(memory);
            var root = doc.RootElement;

            string type = root.TryGetProperty("Type", out var typeProp) ? typeProp.GetString() ?? "" : "";
            int version = root.TryGetProperty("Version", out var verProp) && verProp.TryGetInt32(out int v) ? v : 1;
            string savedFilename = root.TryGetProperty("SavedFilename", out var fileProp) ? fileProp.GetString() ?? "" : "";

            var references = new List<NwcXRef>();
            if (root.TryGetProperty("Table", out var tableProp) && tableProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tableProp.EnumerateArray())
                {
                    string orig = item.TryGetProperty("OriginalPath", out var origProp) ? origProp.GetString() ?? "" : "";
                    string remap = item.TryGetProperty("RemappedPath", out var remapProp) ? remapProp.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(orig) || !string.IsNullOrEmpty(remap))
                    {
                        references.Add(new NwcXRef(orig, remap));
                    }
                }
            }

            return new NwcXRefTable(type, version, savedFilename, references);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
