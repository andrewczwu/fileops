namespace FileOps.Core;

/// <summary>
/// Process exit codes returned by the CLI. Scripts can branch on these without parsing output.
/// </summary>
public enum ExitCode
{
    /// <summary>The operation completed.</summary>
    Success = 0,

    /// <summary>The request itself was invalid (bad arguments, same source and destination, path is a directory).</summary>
    UsageError = 1,

    /// <summary>A required input file or directory does not exist.</summary>
    NotFound = 2,

    /// <summary>The destination already exists and overwriting was not requested.</summary>
    AlreadyExists = 3,

    /// <summary>The file system refused the operation (permissions, disk full, locked file).</summary>
    IoError = 4,

    /// <summary>An unexpected failure inside the tool. Mirrors <c>EX_SOFTWARE</c> from sysexits.h.</summary>
    InternalError = 70,
}
