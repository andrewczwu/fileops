namespace FileOps.Core.Telemetry;

/// <summary>
/// Stable event identifiers for every structured log record the library emits.
/// Tests and log consumers match on these rather than on message text.
/// Ranges: 1xxx lifecycle, 2xxx create, 3xxx copy, 4xxx combine, 5xxx delete.
/// </summary>
public static class LogEvents
{
    /// <summary>An operation is about to run.</summary>
    public const int OperationStarted = 1000;

    /// <summary>An operation finished successfully.</summary>
    public const int OperationCompleted = 1001;

    /// <summary>An operation failed with a known exit code.</summary>
    public const int OperationFailed = 1002;

    /// <summary>A file was created.</summary>
    public const int FileCreated = 2001;

    /// <summary>A file was copied.</summary>
    public const int FileCopied = 3001;

    /// <summary>Two files were concatenated into a third.</summary>
    public const int FilesCombined = 4001;

    /// <summary>A file was deleted.</summary>
    public const int FileDeleted = 5001;

    /// <summary>Delete was asked to ignore a missing file.</summary>
    public const int DeleteSkippedMissing = 5002;
}
