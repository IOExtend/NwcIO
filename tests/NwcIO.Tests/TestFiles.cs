namespace NwcIO.Tests;

/// <summary>
/// Helper to locate external test fixtures that are not part of the repository (clean-room policy).
/// Private models can be supplied via the NWCIO_TEST_FILES environment variable or placed
/// in an uncommitted 'test_files' directory in the repository root or its ancestors.
/// </summary>
internal static class TestFiles
{
    public const string EnvironmentVariable = "NWCIO_TEST_FILES";

    /// <summary>
    /// Searches for candidate fixture directories.
    /// </summary>
    public static IEnumerable<string> SearchRoots()
    {
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(env))
        {
            if (File.Exists(env))
            {
                yield return Path.GetDirectoryName(env)!;
            }
            else if (Directory.Exists(env))
            {
                yield return env;
            }
        }

        for (var dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "test_files");
            if (Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Returns the path to the primary test model, or null if none is available.
    /// </summary>
    public static string? TryResolvePrimary()
    {
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
        {
            return env;
        }

        foreach (var root in SearchRoots())
        {
            var candidate = Directory.EnumerateFiles(root, "*.nwc", SearchOption.AllDirectories).FirstOrDefault();
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Every local fixture file in a stable order: only the file named by <see cref="EnvironmentVariable"/>
    /// when it exists, otherwise all *.nwc and *.nwd files of the first search root that holds any,
    /// ordered by (length, full path). Reports refer to files by position in this list.
    /// </summary>
    public static IReadOnlyList<string> EnumerateAll()
    {
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
        {
            return [env];
        }

        foreach (var root in SearchRoots())
        {
            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".nwc", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => new FileInfo(f).Length)
                .ThenBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count > 0)
            {
                return files;
            }
        }

        return [];
    }

    /// <summary>
    /// Returns a human-readable reason why no fixture could be resolved, or null when
    /// one is available. A set-but-unresolvable <see cref="EnvironmentVariable"/> value
    /// is called out so a broken fixture setup is visible in the test output.
    /// </summary>
    public static string? DescribeSkipReason()
    {
        if (TryResolvePrimary() is not null)
        {
            return null;
        }

        var env = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(env))
        {
            return $"No .nwc fixture found: {EnvironmentVariable} is set to '{env}', which is neither an existing .nwc file nor a directory containing one.";
        }

        return $"No .nwc fixture found (set {EnvironmentVariable} or add a test_files directory).";
    }

    /// <summary>
    /// Returns the path to a secondary test model for multi-model federated testing, or null if none is available.
    /// </summary>
    public static string? TryResolveSecondary(string? primaryPath)
    {
        if (primaryPath is null) return null;
        var dir = Path.GetDirectoryName(primaryPath);
        if (dir is null || !Directory.Exists(dir)) return null;

        return Directory.EnumerateFiles(dir, "*.nwc", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => !string.Equals(f, primaryPath, StringComparison.OrdinalIgnoreCase));
    }
}
