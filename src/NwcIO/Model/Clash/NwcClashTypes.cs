using System.Globalization;
using NwcIO.Internal.Export;
using NwcIO.Model.Sets;

namespace NwcIO.Model.Clash;

/// <summary>
/// Status lifecycle of a detected clash in Navisworks Clash Detective.
/// </summary>
public enum NwcClashStatus
{
    New,
    Active,
    Reviewed,
    Approved,
    Resolved,
}

/// <summary>
/// Type of geometric intersection or clearance test to perform.
/// </summary>
public enum NwcClashTestType
{
    Hard,
    Clearance,
    Duplicates,
    Conservative,
}

/// <summary>
/// Standard rule types to filter out acceptable or intentional clashes.
/// </summary>
public enum NwcClashRuleType
{
    IgnoreSameLayer,
    IgnoreSameGroup,
    IgnoreSameFile,
    IgnoreSpecifiedSets,
    Custom,
}

/// <summary>
/// A rule that excludes specific pairs of elements from clash reporting.
/// </summary>
public sealed class NwcClashRule
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public NwcClashRuleType RuleType { get; set; } = NwcClashRuleType.IgnoreSameLayer;
    public string? SetA { get; set; }
    public string? SetB { get; set; }

    public NwcClashRule() { }

    public NwcClashRule(string name, NwcClashRuleType ruleType, bool enabled = true)
    {
        Name = name;
        RuleType = ruleType;
        Enabled = enabled;
    }

    /// <summary>Creates a rule that ignores clashes between elements on the same layer.</summary>
    public static NwcClashRule SameLayer(bool enabled = true) =>
        new("Items in same layer", NwcClashRuleType.IgnoreSameLayer, enabled);

    /// <summary>Creates a rule that ignores clashes between elements in the same file/model.</summary>
    public static NwcClashRule SameFile(bool enabled = true) =>
        new("Items in same file", NwcClashRuleType.IgnoreSameFile, enabled);

    /// <summary>Creates a rule that ignores clashes between elements in the same group or parent composite node.</summary>
    public static NwcClashRule SameGroup(bool enabled = true) =>
        new("Items in same group", NwcClashRuleType.IgnoreSameGroup, enabled);
}

/// <summary>
/// Defines which elements participate on one side of a clash test.
/// </summary>
public sealed class NwcClashSelection
{
    public string Name { get; set; } = "All";
    public List<string> SetNames { get; } = [];
    public List<int> ExplicitNodeIds { get; } = [];
    public NwcSearchCondition? FilterCondition { get; set; }

    public NwcClashSelection() { }

    public NwcClashSelection(string name)
    {
        Name = name;
    }

    public static NwcClashSelection All() => new("All");

    public static NwcClashSelection FromSet(string setName)
    {
        var sel = new NwcClashSelection(setName);
        sel.SetNames.Add(setName);
        return sel;
    }

    public static NwcClashSelection FromCondition(string name, NwcSearchCondition condition) =>
        new(name) { FilterCondition = condition };
}

/// <summary>
/// Information about an individual model element involved in a clash.
/// </summary>
public sealed class NwcClashObject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int? ElementId { get; set; }
    public string? IfcGuid { get; set; }
    public string? Layer { get; set; }
    public string? ModelName { get; set; }
    public NwcNode? Node { get; set; }
    public NwcMesh? Mesh { get; set; }

    public NwcClashObject() { }

    public NwcClashObject(string name, int? elementId = null, string? path = null)
    {
        Name = name;
        ElementId = elementId;
        Path = path ?? name;
        if (elementId.HasValue)
        {
            IfcGuid = Internal.Export.IfcGuid.FromElementId(elementId.Value, "NwcClash");
        }
    }
}

/// <summary>
/// A comment recorded against a clash test result.
/// </summary>
public sealed class NwcClashComment
{
    public int Id { get; set; } = 1;
    public string Author { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset Date { get; set; } = DateTimeOffset.UtcNow;
    public NwcClashStatus Status { get; set; } = NwcClashStatus.Active;

    public NwcClashComment() { }

    public NwcClashComment(string body, string author = "Clash Detective", NwcClashStatus status = NwcClashStatus.Active)
    {
        Body = body;
        Author = author;
        Status = status;
        Date = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Represents a single collision, penetration, or clearance violation between two BIM elements.
/// </summary>
public sealed class NwcClashResult
{
    public string Name { get; set; } = "Clash1";
    public Guid ResultGuid { get; set; } = Guid.NewGuid();
    public NwcClashStatus Status { get; set; } = NwcClashStatus.New;
    public NwcClashObject Item1 { get; set; } = new();
    public NwcClashObject Item2 { get; set; } = new();
    public Vector3d ClashPoint { get; set; }
    public double Distance { get; set; } // Negative for hard clash penetration, positive for clearance
    public string? GridLocation { get; set; }
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public string? AssignedTo { get; set; }
    public string? ApprovedBy { get; set; }
    public string? Description { get; set; }
    public List<NwcClashComment> Comments { get; } = [];
    public NwcSavedViewpoint? Viewpoint { get; set; }

    public NwcClashResult() { }

    public NwcClashResult(
        string name,
        NwcClashObject item1,
        NwcClashObject item2,
        Vector3d clashPoint,
        double distance = 0.0,
        NwcClashStatus status = NwcClashStatus.New)
    {
        Name = name;
        Item1 = item1;
        Item2 = item2;
        ClashPoint = clashPoint;
        Distance = distance;
        Status = status;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public void AddComment(string body, string author = "Coordinator", NwcClashStatus status = NwcClashStatus.Active)
    {
        Comments.Add(new NwcClashComment
        {
            Id = Comments.Count + 1,
            Author = author,
            Body = body,
            Date = DateTimeOffset.UtcNow,
            Status = status,
        });
    }
}

/// <summary>
/// A logical group of related clashes in Navisworks Clash Detective (e.g. grouped by element, proximity, or level).
/// Conforms to &lt;clashgroup&gt; in the Navisworks Clash Report XML schema.
/// </summary>
public sealed class NwcClashGroup
{
    public string Name { get; set; } = "Group 1";
    public Guid GroupGuid { get; set; } = Guid.NewGuid();
    public NwcClashStatus Status { get; set; } = NwcClashStatus.New;
    public string? Description { get; set; }
    public string? AssignedTo { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public string? GridLocation { get; set; }
    public List<NwcClashResult> Clashes { get; } = [];
    public List<NwcClashComment> Comments { get; } = [];
    public NwcSavedViewpoint? Viewpoint { get; set; }

    public Vector3d ClashPoint => Clashes.Count == 0 ? Vector3d.Zero :
        new Vector3d(
            Clashes.Average(c => c.ClashPoint.X),
            Clashes.Average(c => c.ClashPoint.Y),
            Clashes.Average(c => c.ClashPoint.Z));

    public double Distance => Clashes.Count == 0 ? 0.0 : Clashes.Min(c => c.Distance);

    public NwcClashGroup() { }

    public NwcClashGroup(string name, NwcClashStatus status = NwcClashStatus.New)
    {
        Name = name;
        Status = status;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public void AddClash(NwcClashResult clash)
    {
        ArgumentNullException.ThrowIfNull(clash);
        Clashes.Add(clash);
    }
}

/// <summary>
/// Represents a configured Navisworks Clash Detective test containing rules, selection sets, and results.
/// </summary>
public sealed class NwcClashTest
{
    public string Name { get; set; } = "Clash Test";
    public Guid TestGuid { get; set; } = Guid.NewGuid();
    public NwcClashTestType TestType { get; set; } = NwcClashTestType.Hard;
    public string Status { get; set; } = "active";
    public double Tolerance { get; set; } = 0.001; // in meters (default: 1 mm)
    public NwcClashSelection SelectionA { get; set; } = NwcClashSelection.All();
    public NwcClashSelection SelectionB { get; set; } = NwcClashSelection.All();
    public List<NwcClashRule> Rules { get; } = [];
    public List<NwcClashResult> Results { get; } = [];
    public List<NwcClashGroup> Groups { get; } = [];

    public IEnumerable<NwcClashResult> AllClashes =>
        Results.Concat(Groups.SelectMany(g => g.Clashes));

    public int TotalClashCount => Results.Count + Groups.Sum(g => g.Clashes.Count);
    public int TotalCount => Results.Count + Groups.Count;
    public int NewCount => Results.Count(r => r.Status == NwcClashStatus.New) + Groups.Count(g => g.Status == NwcClashStatus.New);
    public int ActiveCount => Results.Count(r => r.Status == NwcClashStatus.Active) + Groups.Count(g => g.Status == NwcClashStatus.Active);
    public int ReviewedCount => Results.Count(r => r.Status == NwcClashStatus.Reviewed) + Groups.Count(g => g.Status == NwcClashStatus.Reviewed);
    public int ApprovedCount => Results.Count(r => r.Status == NwcClashStatus.Approved) + Groups.Count(g => g.Status == NwcClashStatus.Approved);
    public int ResolvedCount => Results.Count(r => r.Status == NwcClashStatus.Resolved) + Groups.Count(g => g.Status == NwcClashStatus.Resolved);

    public NwcClashTest() { }

    public NwcClashTest(
        string name,
        NwcClashTestType testType = NwcClashTestType.Hard,
        double tolerance = 0.001)
    {
        Name = name;
        TestType = testType;
        Tolerance = tolerance;
    }

    public void AddResult(NwcClashResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Results.Add(result);
    }
}

/// <summary>
/// Container holding multiple clash tests (equivalent to Navisworks &lt;batchtest&gt;).
/// </summary>
public sealed class NwcClashBatch
{
    public string Name { get; set; } = "BatchTest";
    public List<NwcClashTest> Tests { get; } = [];

    public NwcClashBatch() { }

    public NwcClashBatch(string name)
    {
        Name = name;
    }
}
