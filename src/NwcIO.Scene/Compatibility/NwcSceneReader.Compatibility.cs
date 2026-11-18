using IOScene;

namespace NwcIO.Scene;

public static partial class NwcSceneReader
{
    static partial void ReportLegacyClashTests(NwcDocument document, List<SceneDiagnostic> diagnostics)
    {
        if (document.ClashTests.Count > 0)
        {
            diagnostics.Add(new SceneDiagnostic(SceneSeverity.Warning, "clash-tests-skipped", "Clash tests are not converted."));
        }
    }
}
