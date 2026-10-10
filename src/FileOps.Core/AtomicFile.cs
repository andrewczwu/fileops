namespace FileOps.Core;

/// <summary>
/// Writes files so that a crash or error never leaves a partially written destination behind.
/// Content goes to a temporary file in the destination directory and is then renamed into place,
/// which is a single atomic step on POSIX file systems.
/// </summary>
internal static class AtomicFile
{
    /// <summary>
    /// Writes <paramref name="destination"/> via a temporary sibling file.
    /// </summary>
    /// <param name="destination">Final path of the file.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="write">Callback that writes the content to the open stream.</param>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="FileOpsException">
    /// <see cref="ExitCode.NotFound"/> if the destination directory does not exist;
    /// <see cref="ExitCode.AlreadyExists"/> if the destination exists and <paramref name="overwrite"/> is false.
    /// </exception>
    public static long Write(string destination, bool overwrite, Action<Stream> write)
    {
        var fullPath = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(fullPath) ?? Path.GetPathRoot(fullPath)!;

        if (!Directory.Exists(directory))
        {
            throw new FileOpsException(ExitCode.NotFound, $"directory does not exist: {directory}", directory);
        }

        if (!overwrite && File.Exists(fullPath))
        {
            throw new FileOpsException(ExitCode.AlreadyExists, $"file already exists: {destination} (use --force to overwrite)", destination);
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            long bytes;
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
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
}
