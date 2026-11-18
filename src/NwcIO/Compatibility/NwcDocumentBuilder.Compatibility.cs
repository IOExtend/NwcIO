namespace NwcIO;

public sealed partial class NwcDocumentBuilder
{
    private readonly List<Model.Clash.NwcClashTest> _clashTests = [];

    /// <summary>Adds a Clash Detective test to the document.</summary>
    public NwcDocumentBuilder AddClashTest(Model.Clash.NwcClashTest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        _clashTests.Add(test);
        return this;
    }

    partial void AddConfiguredClashTests(NwcDocument document) => document.ClashTests.AddRange(_clashTests);
}
