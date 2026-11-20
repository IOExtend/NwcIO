using System.Security.AccessControl;

namespace NwcIO.Internal;

internal static class AtomicFileWriter
{
    public static void Write(string path, Action<Stream> write, CancellationToken cancellationToken)
        => WriteCore(path, output =>
        {
            write(output);
            return ValueTask.CompletedTask;
        }, asynchronous: false, cancellationToken).GetSynchronousResult();

    public static ValueTask WriteAsync(string path, Func<Stream, ValueTask> write, CancellationToken cancellationToken)
        => WriteCore(path, write, asynchronous: true, cancellationToken);

    internal static async ValueTask WriteCore(
        string path, Func<Stream, ValueTask> write, bool asynchronous, CancellationToken cancellationToken,
        (string Path, Func<Stream, ValueTask> Write)? companion = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var primary = await StageFile(path, write, asynchronous, companion.HasValue, isCompanion: false, cancellationToken)
            .ConfigureAwait(false);
        StagedFile? secondary = null;
        bool committed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (companion is { } pending)
            {
                var staged = await StageFile(pending.Path, pending.Write, asynchronous, paired: true, isCompanion: true, cancellationToken)
                    .ConfigureAwait(false);
                secondary = staged;
                if (string.Equals(primary.Target.FullName, staged.Target.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("The output and its companion must be different files.", nameof(path));
                }
                WithPairLock(() => CommitPair(primary, staged), cancellationToken);
            }
            else
            {
                File.Move(primary.Temp, primary.Target.FullName, overwrite: primary.Target.Exists);
            }

            committed = true;
        }
        finally
        {
            if (!committed)
            {
                File.Delete(primary.Temp);
                if (secondary is { } staged) File.Delete(staged.Temp);
            }
        }
    }

    private static FileInfo GetTarget(string path, bool isCompanion)
    {
        var target = new FileInfo(Path.GetFullPath(path));
        if (target.LinkTarget is not null)
        {
            if (isCompanion) throw new IOException($"The companion file '{path}' is a symbolic link.");
            target = new FileInfo(target.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
        }

        if (target.Exists)
        {
            // A rename can bypass the destination's file write permissions on Unix.
            using var accessCheck = target.Open(FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }

        return target;
    }

    private static async ValueTask<StagedFile> StageFile(string path, Func<Stream, ValueTask> write,
        bool asynchronous, bool paired, bool isCompanion, CancellationToken cancellationToken)
    {
        StagedFile staged = default;
        FileStream? stream = null;
        if (paired) WithPairLock(Prepare, cancellationToken);
        else Prepare();
        var output = stream!;
        try
        {
            try
            {
                await write(output).ConfigureAwait(false);
            }
            finally
            {
                if (asynchronous)
                {
                    await output.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    output.Dispose();
                }
            }

            return staged;
        }
        catch
        {
            File.Delete(staged.Temp);
            throw;
        }

        void Prepare()
        {
            var target = GetTarget(path, isCompanion);
            staged = new StagedFile(TemporarySibling(target.DirectoryName!), target);
            stream = CreateTemporaryFile(staged.Temp, target, target.Exists, asynchronous);
        }
    }

    private static void WithPairLock(Action action, CancellationToken cancellationToken)
    {
        // Serialize permission checks and renames across CLI processes. File contents are written outside the lock.
        using var commitLock = new Mutex(initiallyOwned: false, "NwcIO.AtomicFileWriter.Pair");
        bool acquired = false;
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    acquired = commitLock.WaitOne(50);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true;
                }
            }
            while (!acquired);

            cancellationToken.ThrowIfCancellationRequested();
            action();
        }
        finally
        {
            if (acquired) commitLock.ReleaseMutex();
        }
    }

    private static void CommitPair(StagedFile primary, StagedFile companion)
    {
        // Once publication starts it must finish or restore the original companion, even if cancelled.
        string path = primary.Target.FullName;
        string companionPath = companion.Target.FullName;
        if (new FileInfo(companionPath).LinkTarget is not null)
        {
            throw new IOException($"The companion file '{companionPath}' is a symbolic link.");
        }
        string? backup = null;
        if (File.Exists(companionPath))
        {
            backup = TemporarySibling(companion.Target.DirectoryName!);
            File.Replace(companion.Temp, companionPath, backup);
        }
        else
        {
            File.Move(companion.Temp, companionPath);
        }

        try
        {
            File.Move(primary.Temp, path, overwrite: File.Exists(path));
        }
        catch
        {
            if (backup is null) File.Delete(companionPath);
            else File.Move(backup, companionPath, overwrite: true);
            throw;
        }

        if (backup is not null) File.Delete(backup);
    }

    private readonly record struct StagedFile(string Temp, FileInfo Target);

    private static string TemporarySibling(string directory) => Path.Combine(directory, $"nwcio-{Guid.NewGuid():N}.tmp");

    private static FileStream CreateTemporaryFile(string path, FileInfo target, bool exists, bool asynchronous)
    {
        var fileOptions = asynchronous ? FileOptions.Asynchronous : FileOptions.None;
        int bufferSize = asynchronous ? 1 : 4096;
        if (OperatingSystem.IsWindows() && exists)
        {
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, bufferSize,
                fileOptions, target.GetAccessControl(AccessControlSections.Access));
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = fileOptions,
            BufferSize = bufferSize,
        };
        if (!OperatingSystem.IsWindows() && exists)
        {
            options.UnixCreateMode = File.GetUnixFileMode(target.FullName)
                & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        }

        return new FileStream(path, options);
    }
}
