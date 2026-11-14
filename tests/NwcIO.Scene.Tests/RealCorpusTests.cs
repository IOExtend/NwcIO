using System.Globalization;
using IOScene;
using NwcIO.Tests;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace NwcIO.Scene.Tests;

/// <summary>Marks a test that needs a private real-model fixture; xUnit reports it as skipped with a reason when none is available.</summary>
internal sealed class RealCorpusFactAttribute : FactAttribute
{
    public RealCorpusFactAttribute()
    {
        string? skipReason = TestFiles.DescribeSkipReason();
        if (skipReason is not null)
        {
            Skip = skipReason;
        }
    }
}

/// <summary>
/// Converts every local .nwc and checks the scene. Privacy: nothing from a model reaches an Assert call or the
/// output; failures carry a file index and a fixed rule label only.
/// </summary>
public sealed class RealCorpusTests
{
    private const string ElementSetKey = "nwc:LcRevitData_Element";
    private const string MaterialSetKey = "nwc:LcRevitMaterialProperties";
    private const string ColorKey = "nwc:LcRevitMaterialProperties.revit_Color";

    private readonly ITestOutputHelper _output;

    public RealCorpusTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class Violation : XunitException
    {
        public Violation(string message) : base(message)
        {
        }
    }

    [RealCorpusFact]
    public void Read_EveryNwc_ConvertsCleanly()
    {
        IReadOnlyList<string> files;
        try
        {
            files = TestFiles.EnumerateAll();
        }
        catch (Exception ex)
        {
            // An IO error message can carry a fixture path.
            throw new XunitException($"fixture enumeration: {ex.GetType().Name}");
        }

        long read = 0, elements = 0, instances = 0, geometries = 0, materials = 0, levels = 0, gridLines = 0, compared = 0, mismatched = 0;
        var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].EndsWith(".nwd", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var result = NwcSceneReader.Read(files[i]);
                var doc = result.Document;
                read++;
                elements += doc.Elements.Count;
                instances += doc.Instances.Count;
                geometries += doc.Geometries.Count;
                materials += doc.Materials.Count;
                levels += doc.Levels.Count;
                gridLines += doc.Grids.Sum(g => g.Lines.Count);
                foreach (var d in result.Diagnostics)
                {
                    codes[d.Code] = codes.GetValueOrDefault(d.Code) + 1;
                }

                Check(doc.Validate().Count == 0, i, "validate-empty");
                CheckElements(i, doc);
                Check(!result.Diagnostics.Any(d => d.Code == "no-source-id"), i, "no-source-id-absent");
                var (c, m) = CompareColours(doc);
                compared += c;
                mismatched += m;
                Check(m == 0, i, $"colour-matches-revit ({m} of {c})");
            }
            catch (Exception ex)
            {
                var safe = ex is InvalidDataException or NotSupportedException or Violation ? ex.Message : string.Empty;
                throw new XunitException($"file #{i}: {ex.GetType().Name}: {safe}");
            }

            _output.WriteLine($"#{i} ok");
        }

        _output.WriteLine($"files {read}, elements {elements}, instances {instances}, geometries {geometries}, materials {materials}");
        _output.WriteLine($"levels {levels}, grid lines {gridLines}, colours compared {compared}, mismatched {mismatched}");
        _output.WriteLine("diagnostics " + string.Join(", ", codes.Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} {kv.Value}"))));
    }

    private static void Check(bool condition, int file, string rule)
    {
        if (!condition)
        {
            throw new Violation($"file #{file}: {rule}");
        }
    }

    private static void CheckElements(int file, SceneDocument doc)
    {
        var byId = doc.Elements.ToDictionary(e => e.Id);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in doc.Elements)
        {
            if (e.SourceId is not null)
            {
                Check(sourceIds.Add(e.SourceId), file, "source-id-unique");
                Check(e.PropertySetIds.Any(id => doc.PropertySets[id - 1] is { } s && s.Key == ElementSetKey && s.Properties.Count >= 1),
                    file, "element-own-properties");
            }

            if (e.Kind != SceneNodeKind.Element)
            {
                continue;
            }

            var cur = e;
            while (cur.SourceId is null && cur.ParentId is { } parent)
            {
                cur = byId[parent];
            }

            Check(cur.SourceId is not null, file, "element-under-source-id");
        }
    }

    private static (long Compared, long Mismatched) CompareColours(SceneDocument doc)
    {
        var byId = doc.Elements.ToDictionary(e => e.Id);
        var materials = doc.Materials.ToDictionary(m => m.Id);
        long compared = 0, mismatched = 0;
        foreach (var inst in doc.Instances)
        {
            if (inst.MaterialId is not { } mid || !materials.TryGetValue(mid, out var material))
            {
                continue;
            }

            var text = byId[inst.ElementId].PropertySetIds
                .Select(id => doc.PropertySets[id - 1])
                .Where(s => s.Key == MaterialSetKey)
                .SelectMany(s => s.Properties)
                .Where(p => p.Key == ColorKey)
                .Select(p => p.Value.Text)
                .FirstOrDefault(t => t is not null);
            if (text is null)
            {
                continue;
            }

            var parts = text.Split(',');
            if (parts.Length != 3
                || !byte.TryParse(parts[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var r)
                || !byte.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var g)
                || !byte.TryParse(parts[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var b))
            {
                continue;
            }

            compared++;
            if (material.Color.R != r || material.Color.G != g || material.Color.B != b)
            {
                mismatched++;
            }
        }

        return (compared, mismatched);
    }
}
