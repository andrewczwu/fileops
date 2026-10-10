namespace FileOps.Core;

/// <summary>
/// A failure the user can act on. Carries the <see cref="ExitCode"/> the process should return
/// and a message that is safe to print verbatim.
/// </summary>
public sealed class FileOpsException : Exception
{
    /// <summary>Creates an exception for the given exit code.</summary>
    /// <param name="exitCode">The process exit code that describes the failure.</param>
    /// <param name="message">A user-facing description of the failure.</param>
    /// <param name="path">The path the failure relates to, if any.</param>
    /// <param name="innerException">The underlying file-system exception, if any.</param>
    public FileOpsException(ExitCode exitCode, string message, string? path = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        Path = path;
    }

    /// <summary>The exit code the process should return for this failure.</summary>
    public ExitCode ExitCode { get; }

    /// <summary>The path the failure relates to, if any.</summary>
    public string? Path { get; }
}
