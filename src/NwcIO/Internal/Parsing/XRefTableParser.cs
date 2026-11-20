using System.Text;
using System.Text.Json;
using NwcIO.Internal.Container;
using NwcIO.Model;

namespace NwcIO.Internal.Parsing;

/// <summary>
/// Parses the JSON XRefTable embedded in the <see cref="NwcStreamKind.XRefJson"/> stream.
/// </summary>
internal static class XRefTableParser
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
            if (root.ValueKind != JsonValueKind.Object) return null;

            string type = GetString(root, "Type");
            int version = root.TryGetProperty("Version", out var verProp) && verProp.ValueKind == JsonValueKind.Number &&
                verProp.TryGetInt32(out int v) ? v : 1;
            string savedFilename = GetString(root, "SavedFilename");

            var references = new List<NwcXRef>();
            if (root.TryGetProperty("Table", out var tableProp) && tableProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tableProp.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    string orig = GetString(item, "OriginalPath");
                    string remap = GetString(item, "RemappedPath");
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

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
