namespace NwcIO.Internal.Container;

/// <summary>
/// Markers for the writer's transform streams. Real Navisworks transform streams start with a record count and
/// a record id of 100; the writer prepends a magic so it can preserve real transform ids instead.
/// </summary>
internal static class TransformStreamFormat
{
    /// <summary>"NWTR" in little-endian byte order: transform records follow the count.</summary>
    public const int TransformsMagic = 0x5254574E;

    /// <summary>"NWTL" in little-endian byte order: one transform id per mesh follows the count.</summary>
    public const int LinksMagic = 0x4C54574E;
}
