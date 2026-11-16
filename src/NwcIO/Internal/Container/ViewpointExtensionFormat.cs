namespace NwcIO.Internal.Container;

/// <summary>
/// Layout shared by the writer and the reader of the optional saved-viewpoint trailer after the tag-517
/// record. Real Navisworks streams do not contain it; the reader falls back to its heuristics when it is absent.
/// </summary>
internal static class ViewpointExtensionFormat
{
    /// <summary>"NWVP" in little-endian byte order.</summary>
    public const int Magic = 0x5056574E;

    /// <summary>Version stored as the first payload int.</summary>
    public const int Version = 1;
}
