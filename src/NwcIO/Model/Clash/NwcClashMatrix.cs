using System.Globalization;
using System.Text;
using System.Text.Json;
using NwcIO.Internal.Export;
using NwcIO.Model.Sets;

namespace NwcIO.Model.Clash;

/// <summary>
/// Definition of a BIM discipline participating in a multi-model coordination clash matrix.
/// </summary>
public sealed class NwcClashDiscipline
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ModelNameFilter { get; set; }
    public string? SetName { get; set; }
    public NwcSearchCondition? FilterCondition { get; set; }

    public NwcClashDiscipline() { }

    public NwcClashDiscipline(string name, string? modelNameFilter = null)
    {
        Name = name;
        ModelNameFilter = modelNameFilter;
    }

    /// <summary>
    /// Checks whether an element mesh or node matches this discipline.
    /// </summary>
    public bool Matches(NwcMesh mesh, NwcDocument doc)
    {
        if (!string.IsNullOrEmpty(ModelNameFilter))
        {
            string docName = Path.GetFileNameWithoutExtension(doc.FileInfo.SourcePath ?? string.Empty);
            if (!docName.Contains(ModelNameFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (FilterCondition is not null)
        {
            return mesh.Node is not null && FilterCondition.Evaluate(mesh.Node);
        }

        return true;
    }
}

/// <summary>
/// An individual pairwise clash test cell in a multi-discipline coordination matrix.
/// </summary>
public sealed class NwcClashMatrixCell
{
    public string DisciplineA { get; set; } = string.Empty;
    public string DisciplineB { get; set; } = string.Empty;
    public NwcClashTestType TestType { get; set; } = NwcClashTestType.Hard;
    public double Tolerance { get; set; } = 0.001; // in meters
    public bool Enabled { get; set; } = true;
    public NwcClashTest? ExecutedTest { get; set; }

    public int ClashCount => ExecutedTest?.TotalClashCount ?? 0;
    public IReadOnlyList<NwcClashResult> Clashes => ExecutedTest?.Results ?? (IReadOnlyList<NwcClashResult>)Array.Empty<NwcClashResult>();

    public NwcClashMatrixCell() { }

    public NwcClashMatrixCell(string discA, string discB, double tolerance = 0.001, NwcClashTestType type = NwcClashTestType.Hard)
    {
        DisciplineA = discA;
        DisciplineB = discB;
        Tolerance = tolerance;
        TestType = type;
    }
}

/// <summary>
/// Manages and executes a multi-discipline Clash Matrix across architectural, structural, and MEP models,
/// generating consolidated cross-discipline reports, HTML dashboards, and BCF 2.1 archives.
/// </summary>
public sealed class NwcClashMatrix
{
    public string Name { get; set; } = "BIM Clash Matrix";
    public List<NwcClashDiscipline> Disciplines { get; } = [];
    public List<NwcClashMatrixCell> Cells { get; } = [];
    public List<NwcClashRule> GlobalRules { get; } = [];

    public int TotalClashesCount => Cells.Sum(c => c.ClashCount);

    public NwcClashMatrix() { }

    public NwcClashMatrix(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Adds a discipline to the matrix.
    /// </summary>
    public NwcClashDiscipline AddDiscipline(string name, string? modelNameFilter = null)
    {
        var d = new NwcClashDiscipline(name, modelNameFilter);
        Disciplines.Add(d);
        return d;
    }

    /// <summary>
    /// Populates standard pairwise cells between all added disciplines.
    /// </summary>
    public void PopulateStandardMatrix(double defaultTolerance = 0.002, bool includeSelfClash = false)
    {
        Cells.Clear();
        for (int i = 0; i < Disciplines.Count; i++)
        {
            int startJ = includeSelfClash ? i : i + 1;
            for (int j = startJ; j < Disciplines.Count; j++)
            {
                var cell = new NwcClashMatrixCell(
                    Disciplines[i].Name,
                    Disciplines[j].Name,
                    tolerance: defaultTolerance,
                    type: NwcClashTestType.Hard);
                Cells.Add(cell);
            }
        }
    }

    /// <summary>
    /// Creates a default clash coordination matrix automatically populated from the contributing models of an assembly.
    /// </summary>
    public static NwcClashMatrix CreateFromAssembly(NwcAssembly assembly, double defaultTolerance = 0.002)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var matrix = new NwcClashMatrix(assembly.Name + " Clash Matrix");
        if (assembly.Models.Count <= 1)
        {
            matrix.AddDiscipline("Architecture", "AR");
            matrix.AddDiscipline("Structure", "ST");
            matrix.AddDiscipline("MEP", "MEP");
        }
        else
        {
            foreach (var doc in assembly.Models)
            {
                string name = Path.GetFileNameWithoutExtension(doc.FileInfo.SourcePath ?? $"Model_{doc.GetHashCode()}");
                matrix.AddDiscipline(name, name);
            }
        }

        matrix.PopulateStandardMatrix(defaultTolerance, includeSelfClash: false);
        return matrix;
    }

    /// <summary>
    /// Executes all enabled clash tests in the matrix against a federated <see cref="NwcAssembly"/>.
    /// </summary>
    public void Execute(NwcAssembly assembly) => Run(assembly);

    /// <summary>
    /// Executes all enabled clash tests in the matrix against a federated <see cref="NwcAssembly"/>.
    /// </summary>
    public void Run(NwcAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var cell in Cells)
        {
            if (!cell.Enabled) continue;

            var discA = Disciplines.FirstOrDefault(d => string.Equals(d.Name, cell.DisciplineA, StringComparison.OrdinalIgnoreCase));
            var discB = Disciplines.FirstOrDefault(d => string.Equals(d.Name, cell.DisciplineB, StringComparison.OrdinalIgnoreCase));
            if (discA is null || discB is null) continue;

            // Collect participating meshes
            var meshesA = new List<(NwcMesh mesh, NwcDocument doc)>();
            var meshesB = new List<(NwcMesh mesh, NwcDocument doc)>();

            foreach (var doc in assembly.Models)
            {
                foreach (var m in doc.Meshes)
                {
                    if (discA.Matches(m, doc)) meshesA.Add((m, doc));
                    if (discB.Matches(m, doc)) meshesB.Add((m, doc));
                }
            }

            string testName = $"{cell.DisciplineA} vs {cell.DisciplineB}";
            var test = new NwcClashTest(testName, cell.TestType, cell.Tolerance);
            test.Rules.AddRange(GlobalRules);

            // Execute pair detection with spatial pruning
            int clashId = 1;
            for (int i = 0; i < meshesA.Count; i++)
            {
                var (mA, docA) = meshesA[i];
                var boxA = mA.GetWorldBoundingBox();

                int startJ = (cell.DisciplineA == cell.DisciplineB) ? i + 1 : 0;
                for (int j = startJ; j < meshesB.Count; j++)
                {
                    var (mB, docB) = meshesB[j];
                    if (mA.Id == mB.Id && docA == docB) continue;

                    var boxB = mB.GetWorldBoundingBox();
                    var interBox = boxA.Intersect(boxB);
                    if (interBox.IsEmpty) continue;

                    double penetration = Math.Min(interBox.Size.X, Math.Min(interBox.Size.Y, interBox.Size.Z));
                    if (penetration >= cell.Tolerance)
                    {
                        var c1 = new NwcClashObject(mA.Name ?? $"Mesh_{mA.Id}", mA.Id)
                        {
                            ModelName = Path.GetFileNameWithoutExtension(docA.FileInfo.SourcePath ?? "ModelA"),
                            Mesh = mA
                        };
                        var c2 = new NwcClashObject(mB.Name ?? $"Mesh_{mB.Id}", mB.Id)
                        {
                            ModelName = Path.GetFileNameWithoutExtension(docB.FileInfo.SourcePath ?? "ModelB"),
                            Mesh = mB
                        };

                        var clash = new NwcClashResult($"Clash_{clashId++}", c1, c2, interBox.Center, -penetration);

                        // Evaluate ignore rules
                        bool ignore = false;
                        foreach (var rule in test.Rules)
                        {
                            if (!rule.Enabled) continue;
                            if (rule.RuleType == NwcClashRuleType.IgnoreSameFile && string.Equals(c1.ModelName, c2.ModelName, StringComparison.OrdinalIgnoreCase))
                            {
                                ignore = true; break;
                            }
                        }

                        if (!ignore)
                        {
                            test.Results.Add(clash);
                        }
                    }
                }
            }

            cell.ExecutedTest = test;
        }
    }

    /// <summary>
    /// Generates a formatted Markdown summary table of the clash matrix results.
    /// </summary>
    public string ToMarkdownTable()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {Name}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Total Clashes: **{TotalClashesCount}** across **{Cells.Count}** discipline tests.");
        sb.AppendLine();

        var discNames = Disciplines.Select(d => d.Name).ToList();
        sb.Append("| Discipline |");
        foreach (var name in discNames)
        {
            sb.Append(CultureInfo.InvariantCulture, $" {name} |");
        }
        sb.AppendLine();

        sb.Append("|---|");
        foreach (var _ in discNames)
        {
            sb.Append("---|");
        }
        sb.AppendLine();

        for (int i = 0; i < discNames.Count; i++)
        {
            string rowDisc = discNames[i];
            sb.Append(CultureInfo.InvariantCulture, $"| **{rowDisc}** |");
            for (int j = 0; j < discNames.Count; j++)
            {
                string colDisc = discNames[j];
                var cell = Cells.FirstOrDefault(c =>
                    (string.Equals(c.DisciplineA, rowDisc, StringComparison.OrdinalIgnoreCase) && string.Equals(c.DisciplineB, colDisc, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(c.DisciplineA, colDisc, StringComparison.OrdinalIgnoreCase) && string.Equals(c.DisciplineB, rowDisc, StringComparison.OrdinalIgnoreCase)));

                if (cell is not null && cell.ExecutedTest is not null)
                {
                    sb.Append(CultureInfo.InvariantCulture, $" {cell.ClashCount} |");
                }
                else
                {
                    sb.Append(" - |");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports the clash matrix results to a structured JSON file.
    /// </summary>
    public void SaveJson(string filePath, bool indent = true)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = File.Create(filePath);
        using var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = indent });

        writer.WriteStartObject();
        writer.WriteString("name", Name);
        writer.WriteNumber("totalClashes", TotalClashesCount);

        writer.WriteStartArray("disciplines");
        foreach (var d in Disciplines)
        {
            writer.WriteStartObject();
            writer.WriteString("name", d.Name);
            if (!string.IsNullOrEmpty(d.ModelNameFilter)) writer.WriteString("filter", d.ModelNameFilter);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("cells");
        foreach (var c in Cells)
        {
            writer.WriteStartObject();
            writer.WriteString("disciplineA", c.DisciplineA);
            writer.WriteString("disciplineB", c.DisciplineB);
            writer.WriteString("testType", c.TestType.ToString());
            writer.WriteNumber("tolerance", c.Tolerance);
            writer.WriteNumber("clashesCount", c.ClashCount);

            if (c.ExecutedTest is not null && c.ExecutedTest.Results.Count > 0)
            {
                writer.WriteStartArray("clashes");
                foreach (var clash in c.ExecutedTest.Results)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", clash.Name);
                    writer.WriteString("item1", clash.Item1.Name);
                    writer.WriteString("item2", clash.Item2.Name);
                    if (!string.IsNullOrEmpty(clash.GridLocation)) writer.WriteString("grid", clash.GridLocation);
                    writer.WriteNumber("distance", clash.Distance);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();
    }

    /// <summary>
    /// Exports all clashes detected across the entire matrix into a unified buildingSMART BCF 2.1 zip archive.
    /// </summary>
    public void SaveBcf(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var allClashes = Cells
            .Where(c => c.ExecutedTest is not null)
            .SelectMany(c => c.ExecutedTest!.Results)
            .ToList();

        NwcClashBcfExporter.Save(filePath, allClashes, Name);
    }
}
