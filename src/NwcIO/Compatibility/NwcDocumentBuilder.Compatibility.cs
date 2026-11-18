namespace NwcIO;

public sealed partial class NwcDocumentBuilder
{
    private readonly List<Model.Clash.NwcClashTest> _clashTests = [];
    private Model.Appearance.NwcAppearanceProfiler? _appearanceProfiler;

    /// <summary>Adds a Clash Detective test to the document.</summary>
    public NwcDocumentBuilder AddClashTest(Model.Clash.NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        _clashTests.Add(test);
        return this;
    }

    /// <summary>
    /// Sets the Navisworks Appearance Profiler for colorizing the model based on rules.
    /// </summary>
    public NwcDocumentBuilder SetAppearanceProfiler(Model.Appearance.NwcAppearanceProfiler profiler)
    {
        ArgumentNullException.ThrowIfNull(profiler);
        _appearanceProfiler = profiler;
        return this;
    }

    /// <summary>
    /// Adds an appearance profiler rule to colorize matching scene graph elements.
    /// </summary>
    public NwcDocumentBuilder AddAppearanceRule(Model.Appearance.NwcAppearanceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _appearanceProfiler ??= new Model.Appearance.NwcAppearanceProfiler();
        _appearanceProfiler.Rules.Add(rule);
        return this;
    }

    partial void ApplyCompatibilityOptions(NwcDocument document)
    {
        if (_appearanceProfiler is not null)
        {
            document.ApplyAppearanceProfiler(_appearanceProfiler);
        }
        document.ClashTests.AddRange(_clashTests);
    }
}
