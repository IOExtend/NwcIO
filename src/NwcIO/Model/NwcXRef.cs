namespace NwcIO.Model;

/// <summary>
/// Represents an external cross-reference (XRef) to an external file
/// (e.g., texture, CAD file, link model) referenced by a Navisworks document.
/// </summary>
public sealed record NwcXRef(string OriginalPath, string RemappedPath)
{
    /// <summary>
    /// Gets the most specific available path (remapped path if present, otherwise original path).
    /// </summary>
    public string EffectivePath => !string.IsNullOrEmpty(RemappedPath) ? RemappedPath : OriginalPath;

    /// <summary>
    /// Checks whether the referenced file exists on disk, optionally relative to a base directory.
    /// </summary>
    public bool Exists(string? baseDirectory = null) => TryResolve(baseDirectory, out _);

    /// <summary>
    /// <summary>
    /// Resolves the reference to an existing file. A relative stored path is resolved against
    /// <paramref name="baseDirectory"/> FIRST (never against the process working directory,
    /// so a same-named file in the CWD cannot silently substitute a different model); the
    /// stored path as-is is only its last fallback. An absolute stored path is tried first,
    /// and when it is missing the trailing path segments are matched under the base
    /// directory, longest suffix first, so a reference to <c>&lt;old-root&gt;/Arch/A.nwc</c>
    /// resolves to <c>&lt;base&gt;/Arch/A.nwc</c> before <c>&lt;base&gt;/A.nwc</c>.
    /// </summary>
    /// <param name="baseDirectory">Directory to resolve against, typically the directory of the referencing file.</param>
    /// <param name="resolvedPath">The first existing candidate, in the order described above.</param>
    /// <returns>True when an existing file was found.</returns>
    public bool TryResolve(string? baseDirectory, out string resolvedPath)
    {
        var path = EffectivePath;

        if (!string.IsNullOrEmpty(baseDirectory) && !Path.IsPathRooted(path))
        {
            // Relative reference: the base directory (the .nwf folder) wins over the CWD.
            if (TryResolveSuffixUnder(path, baseDirectory, out resolvedPath))
            {
                return true;
            }

            if (File.Exists(path))
            {
                resolvedPath = path;
                return true;
            }

            return false;
        }

        if (File.Exists(path))
        {
            resolvedPath = path;
            return true;
        }

        if (!string.IsNullOrEmpty(baseDirectory) && TryResolveSuffixUnder(path, baseDirectory, out resolvedPath))
        {
            return true;
        }

        resolvedPath = "";
        return false;
    }

    private static bool TryResolveSuffixUnder(string path, string baseDirectory, out string resolvedPath)
    {
        // Skip drive roots ("C:") so they are never joined onto the base directory.
        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        int first = segments.Length > 1 && segments[0].EndsWith(':') ? 1 : 0;

        // Longest suffix first (start ascends); the last iteration is the bare file name.
        for (int start = first; start < segments.Length; start++)
        {
            string suffix = string.Join(Path.DirectorySeparatorChar, segments[start..]);
            string combined = Path.Combine(baseDirectory, suffix);
            if (File.Exists(combined))
            {
                resolvedPath = combined;
                return true;
            }
        }

        resolvedPath = "";
        return false;
    }
}

/// <summary>
/// Table of external references embedded in the Navisworks file container.
/// </summary>
public sealed record NwcXRefTable(
    string Type,
    int Version,
    string SavedFilename,
    IReadOnlyList<NwcXRef> References);
