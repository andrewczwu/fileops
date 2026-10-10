using Microsoft.Extensions.Logging;

namespace FileOps.Core.Telemetry;

/// <summary>
/// Source-generated, allocation-free log messages. Each carries a stable <see cref="LogEvents"/> id
/// and named placeholders so the JSON output is queryable by field.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = LogEvents.OperationStarted, Level = LogLevel.Debug,
        Message = "Starting {Operation} on {Path}")]
    public static partial void OperationStarted(ILogger logger, FileOperation operation, string path);

    [LoggerMessage(EventId = LogEvents.OperationCompleted, Level = LogLevel.Information,
        Message = "{Operation} completed on {Path}: {Bytes} bytes in {DurationMs:F2} ms")]
    public static partial void OperationCompleted(ILogger logger, FileOperation operation, string path, long bytes, double durationMs);

    [LoggerMessage(EventId = LogEvents.OperationFailed, Level = LogLevel.Error,
        Message = "{Operation} failed on {Path} with exit code {ExitCode}: {Reason}")]
    public static partial void OperationFailed(ILogger logger, Exception? exception, FileOperation operation, string path, ExitCode exitCode, string reason);

    [LoggerMessage(EventId = LogEvents.FileCreated, Level = LogLevel.Information,
        Message = "Created {Path} ({Bytes} bytes)")]
    public static partial void FileCreated(ILogger logger, string path, long bytes);

    [LoggerMessage(EventId = LogEvents.FileCopied, Level = LogLevel.Information,
        Message = "Copied {Source} to {Destination} ({Bytes} bytes)")]
    public static partial void FileCopied(ILogger logger, string source, string destination, long bytes);

    [LoggerMessage(EventId = LogEvents.FilesCombined, Level = LogLevel.Information,
        Message = "Combined {First} and {Second} into {Output} ({Bytes} bytes)")]
    public static partial void FilesCombined(ILogger logger, string first, string second, string output, long bytes);

    [LoggerMessage(EventId = LogEvents.FileDeleted, Level = LogLevel.Information,
        Message = "Deleted {Path} ({Bytes} bytes)")]
    public static partial void FileDeleted(ILogger logger, string path, long bytes);

    [LoggerMessage(EventId = LogEvents.DeleteSkippedMissing, Level = LogLevel.Warning,
        Message = "Nothing to delete: {Path} does not exist")]
    public static partial void DeleteSkippedMissing(ILogger logger, string path);
}
