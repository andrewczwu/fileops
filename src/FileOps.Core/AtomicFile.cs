using System.Text;

namespace FileOps.Core;

/// <summary>
/// Writes files so that a crash or error never leaves a partially written destination behind.
/// Content goes to a temporary file in the destination directory and is then renamed into place,
/// which is a single atomic step on POSIX file systems.
/// </summary>
internal static class AtomicFile
{
    // Most file systems cap a name at 255 bytes. The temp name adds ".", ".", 32 hex digits and ".tmp"
    // around the destination name, so the name component is truncated to leave room for them.
    private const int MaxTempNameBytes = 255;
    private const int TempDecorationBytes = 1 + 1 + 32 + 4;

    /// <summary>
    /// Writes <paramref name="destination"/> via a temporary sibling file.
    /// </summary>
    /// <param name="destination">Final path of the file.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="write">Callback that writes the content to the open temp file.</param>
    /// <param name="mode">
    /// Unix permission bits the file is created with. When <see langword="null"/>, an overwritten
    /// destination's bits are kept; a new file gets the process default.
    /// </param>
    /// <returns>The number of bytes written.</returns>
    /// <remarks>
    /// The permission bits are passed to the <c>open(2)</c> call that creates the temp file, so there is
    /// no instant at which it exists with wider permissions than intended.
    /// </remarks>
    /// <exception cref="FileOpsException">
    /// <see cref="ExitCode.NotFound"/> if the destination directory does not exist;
    /// <see cref="ExitCode.AlreadyExists"/> if the destination exists and <paramref name="overwrite"/> is false.
    /// </exception>
    public static long Write(string destination, bool overwrite, Action<FileStream> write, UnixFileMode? mode = null)
    {
        var fullPath = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(fullPath) ?? Path.GetPathRoot(fullPath) ?? fullPath;

        if (!Directory.Exists(directory))
        {
            throw new FileOpsException(ExitCode.NotFound, $"directory does not exist: {directory}", directory);
        }

        var exists = File.Exists(fullPath);
        if (!overwrite && exists)
        {
            throw new FileOpsException(ExitCode.AlreadyExists, $"file already exists: {destination} (use --force to overwrite)", destination);
        }

        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            // Create the temp file with its final bits so no observer ever sees it wider than intended.
            var createMode = mode ?? (exists ? File.GetUnixFileMode(fullPath) : null);
            if (createMode is { } m)
            {
                options.UnixCreateMode = m;
            }
        }

        var tempPath = Path.Combine(directory, $".{TruncateName(Path.GetFileName(fullPath))}.{Guid.NewGuid():N}.tmp");
        try
        {
            long bytes;
            using (var stream = new FileStream(tempPath, options))
            {
                if (!OperatingSystem.IsWindows() && options.UnixCreateMode is { } m)
                {
                    // open(2) applies the umask to the requested bits; restore any it masked off.
                    File.SetUnixFileMode(stream.SafeFileHandle, m);
                }

                write(stream);
                stream.Flush(flushToDisk: true);
                bytes = stream.Length;
            }

            try
            {
                File.Move(tempPath, fullPath, overwrite);
            }
            catch (IOException ex) when (!overwrite && File.Exists(fullPath))
            {
                // Lost a race: the destination appeared between our check and the rename.
                throw new FileOpsException(ExitCode.AlreadyExists, $"file already exists: {destination} (use --force to overwrite)", destination, ex);
            }

            return bytes;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>Shortens a file name so the decorated temp name stays within the file-system limit.</summary>
    private static string TruncateName(string name)
    {
        var budget = MaxTempNameBytes - TempDecorationBytes;
        if (Encoding.UTF8.GetByteCount(name) <= budget)
        {
            return name;
        }

        // Cut on a character boundary so a multi-byte sequence is never split.
        var length = 0;
        var bytes = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (bytes + size > budget)
            {
                break;
            }

            bytes += size;
            length += rune.Utf16SequenceLength;
        }

        return name[..length];
    }
}
