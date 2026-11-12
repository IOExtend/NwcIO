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

        int start = data.AsSpan().IndexOf((byte)'{');
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
            JsonDocument? document = null;
            while (document is null)
            {
                try
                {
                    document = JsonDocument.Parse(memory);
                }
                catch (JsonException)
                {
                    // The binary prefix can hold a '{' byte: retry from the next one. A nested object is never
                    // complete JSON on its own, as the rest of the root follows it, so the root is still the one found.
                    int next = memory.Span[1..].IndexOf((byte)'{');
                    if (next < 0)
                    {
                        return null;
                    }

                    memory = memory[(next + 1)..];
                }
            }

            using var doc = document;
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
