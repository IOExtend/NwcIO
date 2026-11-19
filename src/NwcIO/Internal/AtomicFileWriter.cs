using System.Security.AccessControl;

namespace NwcIO.Internal;

internal static class AtomicFileWriter
{
    public static void Write(string path, Action<Stream> write, CancellationToken cancellationToken)
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
        var output = CreateTemporaryFile(temp, target, exists);
        bool committed = false;
        try
        {
            using (output)
            {
                write(output);
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

    private static FileStream CreateTemporaryFile(string path, FileInfo target, bool exists)
    {
        if (OperatingSystem.IsWindows() && exists)
        {
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096,
                FileOptions.None, target.GetAccessControl(AccessControlSections.Access));
        }

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows() && exists)
        {
            options.UnixCreateMode = File.GetUnixFileMode(target.FullName)
                & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        }

        return new FileStream(path, options);
    }
}
