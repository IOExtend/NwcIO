namespace NwcIO.Model;

/// <summary>Progress reported by long-running reader, clash and export operations.</summary>
/// <param name="Phase">Operation phase, for example "clash".</param>
/// <param name="Completed">Completed units, such as mesh pairs or model files.</param>
/// <param name="Total">Total units for the phase, or 0 when unknown.</param>
public sealed record NwcProgress(string Phase, int Completed, int Total);
