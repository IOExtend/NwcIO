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

    /// <summary>Most comments per viewpoint the reader accepts.</summary>
    public const int MaxComments = 1024;

    /// <summary>Longest comment, in UTF-8 bytes, the reader accepts.</summary>
    public const int MaxCommentBytes = 65536;

    /// <summary>Most section planes per viewpoint the reader accepts.</summary>
    public const int MaxSectionPlanes = 64;
}
