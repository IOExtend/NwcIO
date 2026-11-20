using System.Globalization;
using System.Text;
using System.Text.Json;
using NwcIO.Internal.Export;
using NwcIO.Model.Sets;
using NwcIO.Model.Spatial;

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

        if (!string.IsNullOrEmpty(SetName))
        {
            if (mesh.Node is null)
            {
                return false;
            }

            var set = doc.FindSet(SetName);
            bool member = set switch
            {
                Model.Sets.NwcSelectionSet selection =>
                    selection.NodeIds.Contains(mesh.Node.Id) ||
                    selection.NodePaths.Count > 0 && selection.ResolveNodes(doc).Any(n => n.Id == mesh.Node.Id),
                Model.Sets.NwcSearchSet search => search.Matches(mesh.Node),
                _ => false,
            };

            if (!member)
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
    public double Tolerance { get; set; } = 0.001; // document length units
    public bool Enabled { get; set; } = true;
    public NwcClashTest? ExecutedTest { get; set; }

    public int ClashCount => ExecutedTest?.TotalClashCount ?? 0;
    public IReadOnlyList<NwcClashResult> Clashes =>
        ExecutedTest?.AllClashes.ToList() ?? (IReadOnlyList<NwcClashResult>)Array.Empty<NwcClashResult>();

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
        foreach (var discipline in Disciplines)
        {
            if (!string.IsNullOrEmpty(discipline.SetName) && !assembly.Models.Any(d => d.FindSet(discipline.SetName) is not null))
            {
                throw new ArgumentException(
                    $"Discipline '{discipline.Name}' selects the set '{discipline.SetName}', which no model contains.", nameof(assembly));
            }
        }

        var modelKeys = NwcElementGuid.ModelKeys(assembly.Models);
        var lengthUnit = assembly.CommonLengthUnit;
        var keyByDocument = new Dictionary<NwcDocument, string>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < assembly.Models.Count; i++)
        {
            keyByDocument[assembly.Models[i]] = modelKeys[i];
        }

        var geometry = new ClashGeometry();
        var candidates = new List<int>();
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

            var selectionB = meshesB.Select(item => item.mesh).ToArray();
            bool clearance = cell.TestType == NwcClashTestType.Clearance;
            bool duplicates = cell.TestType == NwcClashTestType.Duplicates;
            double tolerance = Math.Max(0.0, cell.Tolerance);
            // BoxGap squares the axis differences. Round outward so underflow or rounding
            // at the clearance threshold cannot exclude a pair before that test.
            double searchRadius = clearance
                ? Math.BitIncrement(Math.Sqrt(Math.BitIncrement(tolerance * tolerance)))
                : duplicates ? tolerance : 0;
            bool unboundedClearance = clearance && (!double.IsFinite(searchRadius) ||
                selectionB.Any(mesh => HasNonFiniteBounds(geometry.GetBounds(mesh))));
            var index = unboundedClearance ? null : new NwcSpatialIndex(selectionB, geometry.GetBounds, default);
            int clashId = 1;
            for (int i = 0; i < meshesA.Count; i++)
            {
                var (mA, docA) = meshesA[i];
                var boxA = geometry.GetBounds(mA);

                int startJ = (cell.DisciplineA == cell.DisciplineB) ? i + 1 : 0;
                if (unboundedClearance || clearance && HasNonFiniteBounds(boxA))
                {
                    candidates.Clear();
                    for (int j = startJ; j < meshesB.Count; j++) candidates.Add(j);
                }
                else
                {
                    index!.QueryBoxIndices(ClashGeometry.Expand(boxA, searchRadius), candidates);
                }

                foreach (int j in candidates)
                {
                    if (j < startJ) continue;
                    var (mB, docB) = meshesB[j];
                    if ((mA.Id == mB.Id && docA == docB) || NwcClashDetector.IsSameElement(mA, mB)) continue;

                    var boxB = geometry.GetBounds(mB);
                    var interBox = boxA.Intersect(boxB);

                    double penetration;
                    Vector3d clashPoint;
                    if (cell.TestType == NwcClashTestType.Clearance)
                    {
                        double gap = interBox.IsEmpty ? BoxGap(boxA, boxB) : 0.0;
                        if (gap > cell.Tolerance)
                        {
                            continue;
                        }

                        penetration = -gap;
                        clashPoint = interBox.IsEmpty ? (boxA.Center + boxB.Center) * 0.5 : interBox.Center;
                    }
                    else if (cell.TestType == NwcClashTestType.Duplicates)
                    {
                        // Same broad phase as NwcClashDetector: empty meshes are never duplicates.
                        if (boxA.IsEmpty || boxB.IsEmpty)
                        {
                            continue;
                        }

                        if (double.IsNaN(tolerance) || !NwcClashDetector.IsDuplicate(mA, mB, tolerance, geometry))
                        {
                            continue;
                        }

                        penetration = 0;
                        clashPoint = (boxA.Center + boxB.Center) * 0.5;
                    }
                    else if (cell.TestType == NwcClashTestType.Conservative)
                    {
                        if (interBox.IsEmpty)
                        {
                            continue;
                        }

                        penetration = Math.Min(interBox.Size.X, Math.Min(interBox.Size.Y, interBox.Size.Z));
                        clashPoint = interBox.Center;
                    }
                    else
                    {
                        if (interBox.IsEmpty)
                        {
                            continue;
                        }

                        penetration = Math.Min(interBox.Size.X, Math.Min(interBox.Size.Y, interBox.Size.Z));
                        if (penetration < cell.Tolerance)
                        {
                            continue;
                        }

                        clashPoint = interBox.Center;
                    }

                    {
                        var c1 = new NwcClashObject(mA.Name ?? $"Mesh_{mA.Id}", mA.Id)
                        {
                            ModelName = Path.GetFileNameWithoutExtension(docA.FileInfo.SourcePath ?? "ModelA"),
                            Mesh = mA,
                            Node = mA.Node,
                            Layer = mA.Node?.Parent?.Name,
                            IfcGuid = NwcElementGuid.ForMesh(mA, keyByDocument[docA]),
                        };
                        var c2 = new NwcClashObject(mB.Name ?? $"Mesh_{mB.Id}", mB.Id)
                        {
                            ModelName = Path.GetFileNameWithoutExtension(docB.FileInfo.SourcePath ?? "ModelB"),
                            Mesh = mB,
                            Node = mB.Node,
                            Layer = mB.Node?.Parent?.Name,
                            IfcGuid = NwcElementGuid.ForMesh(mB, keyByDocument[docB]),
                        };

                        var clash = new NwcClashResult($"Clash_{clashId++}", c1, c2, clashPoint, -penetration)
                        {
                            LengthUnit = lengthUnit,
                        };

                        if (!test.Rules.Any(rule => rule.ShouldIgnore(clash)))
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

            if (c.ExecutedTest is not null && c.ExecutedTest.TotalClashCount > 0)
            {
                writer.WriteStartArray("clashes");
                foreach (var clash in c.ExecutedTest.AllClashes)
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

    private static bool HasNonFiniteBounds(BoundingBox3d box) => !box.IsEmpty &&
        (!double.IsFinite(box.Min.X) || !double.IsFinite(box.Min.Y) || !double.IsFinite(box.Min.Z) ||
         !double.IsFinite(box.Max.X) || !double.IsFinite(box.Max.Y) || !double.IsFinite(box.Max.Z));

    /// <summary>Shortest distance between two axis-aligned boxes (zero when they intersect).</summary>
    private static double BoxGap(BoundingBox3d a, BoundingBox3d b)
    {
        double dx = Math.Max(0.0, Math.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        double dy = Math.Max(0.0, Math.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        double dz = Math.Max(0.0, Math.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Exports all clashes detected across the entire matrix into a unified buildingSMART BCF 2.1 zip archive.
    /// </summary>
    public void SaveBcf(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var tests = Cells
            .Where(c => c.ExecutedTest is not null)
            .Select(c => c.ExecutedTest!)
            .ToList();

        NwcClashBcfExporter.SaveTests(filePath, tests, Name);
    }
}
