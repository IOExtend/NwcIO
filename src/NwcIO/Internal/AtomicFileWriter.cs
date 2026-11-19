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

    private static async ValueTask WriteCore(
        string path, Func<Stream, ValueTask> write, bool asynchronous, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = new FileInfo(Path.GetFullPath(path));
        if (target.LinkTarget is not null)
        {
            target = new FileInfo(target.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
        }

        bool exists = target.Exists;
        if (exists)
        {
            // A rename can bypass the destination's file write permissions on Unix.
            using var accessCheck = target.Open(FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }

        string temp = Path.Combine(target.DirectoryName!, $"nwcio-{Guid.NewGuid():N}.tmp");
        var output = CreateTemporaryFile(temp, target, exists, asynchronous);
        bool committed = false;
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

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, target.FullName, overwrite: exists);
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                File.Delete(temp);
            }
        }
    }

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
