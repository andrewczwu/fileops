using System.Diagnostics;

using FileOps.Core.Telemetry;

using Microsoft.Extensions.Logging;

namespace FileOps.Core;

/// <summary>
/// The four file operations the tool offers. Every call is wrapped in a trace span, timed into a
/// histogram, counted, and logged with a stable event id, so callers and tests can observe exactly
/// what happened without inspecting the file system.
/// </summary>
public sealed class FileOperations
{
    private const int CopyBufferSize = 1024 * 1024;

    private readonly ILogger<FileOperations> _logger;
    private readonly FileOpsMetrics _metrics;

    /// <summary>Creates the service.</summary>
    public FileOperations(ILogger<FileOperations> logger, FileOpsMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);
        _logger = logger;
        _metrics = metrics;
    }

    /// <summary>Creates a file, empty or with the given text (UTF-8, written exactly as given).</summary>
    /// <param name="path">File to create.</param>
    /// <param name="text">Initial content, or <see langword="null"/> for an empty file.</param>
    /// <param name="overwrite">Replace an existing file instead of failing.</param>
    public OperationResult Create(string path, string? text = null, bool overwrite = false) =>
        Execute(FileOperation.Create, path, activity =>
        {
            activity?.SetTag(FileOpsActivitySource.Tags.Overwrite, overwrite);
            RequirePath(path, "path");
            RejectDirectory(path);

            var content = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            var bytes = AtomicFile.Write(path, overwrite, stream => stream.Write(content));

            Log.FileCreated(_logger, path, bytes);
            return new OperationResult(FileOperation.Create, path, bytes);
        });

    /// <summary>Copies <paramref name="source"/> to <paramref name="destination"/>, preserving Unix permissions.</summary>
    /// <param name="source">Existing file to copy.</param>
    /// <param name="destination">Path of the new copy.</param>
    /// <param name="overwrite">Replace an existing destination instead of failing.</param>
    public OperationResult Copy(string source, string destination, bool overwrite = false) =>
        Execute(FileOperation.Copy, destination, activity =>
        {
            activity?.SetTag(FileOpsActivitySource.Tags.Source, source);
            activity?.SetTag(FileOpsActivitySource.Tags.Destination, destination);
            activity?.SetTag(FileOpsActivitySource.Tags.Overwrite, overwrite);

            RequirePath(source, "source");
            RequirePath(destination, "destination");
            RequireFile(source);
            RejectDirectory(destination);
            if (SamePath(source, destination))
            {
                throw new FileOpsException(ExitCode.UsageError, $"source and destination are the same file: {source}", source);
            }

            // The source's permission bits go onto the temp file before any content does, so the
            // copy is never readable more widely than the original, not even between write and rename.
            UnixFileMode? mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(source);
            var bytes = AtomicFile.Write(destination, overwrite, stream =>
            {
                if (!OperatingSystem.IsWindows() && mode is { } m)
                {
                    File.SetUnixFileMode(stream.SafeFileHandle, m);
                }

                using var input = OpenForRead(source);
                input.CopyTo(stream, CopyBufferSize);
            });

            Log.FileCopied(_logger, source, destination, bytes);
            return new OperationResult(FileOperation.Copy, destination, bytes);
        });

    /// <summary>
    /// Concatenates <paramref name="first"/> then <paramref name="second"/> into <paramref name="output"/>.
    /// Both inputs are read before the output is renamed into place, so an input may also be the
    /// output (<c>combine a b a --force</c> appends <c>b</c> to <c>a</c>).
    /// </summary>
    /// <param name="first">First input file.</param>
    /// <param name="second">Second input file.</param>
    /// <param name="output">File to write.</param>
    /// <param name="overwrite">Replace an existing output instead of failing.</param>
    public OperationResult Combine(string first, string second, string output, bool overwrite = false) =>
        Execute(FileOperation.Combine, output, activity =>
        {
            activity?.SetTag(FileOpsActivitySource.Tags.Source, first);
            activity?.SetTag(FileOpsActivitySource.Tags.SecondSource, second);
            activity?.SetTag(FileOpsActivitySource.Tags.Destination, output);
            activity?.SetTag(FileOpsActivitySource.Tags.Overwrite, overwrite);

            RequirePath(first, "first");
            RequirePath(second, "second");
            RequirePath(output, "output");
            RequireFile(first);
            RequireFile(second);
            RejectDirectory(output);

            var bytes = AtomicFile.Write(output, overwrite, stream =>
            {
                using (var a = OpenForRead(first))
                {
                    a.CopyTo(stream, CopyBufferSize);
                }

                using var b = OpenForRead(second);
                b.CopyTo(stream, CopyBufferSize);
            });

            Log.FilesCombined(_logger, first, second, output, bytes);
            return new OperationResult(FileOperation.Combine, output, bytes);
        });

    /// <summary>Deletes a file. Directories are refused.</summary>
    /// <param name="path">File to delete.</param>
    /// <param name="ignoreMissing">Succeed (with a warning) when the file does not exist.</param>
    public OperationResult Delete(string path, bool ignoreMissing = false) =>
        Execute(FileOperation.Delete, path, _ =>
        {
            RequirePath(path, "path");
            RejectDirectory(path);

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                if (!ignoreMissing)
                {
                    throw new FileOpsException(ExitCode.NotFound, $"file not found: {path}", path);
                }

                Log.DeleteSkippedMissing(_logger, path);
                return new OperationResult(FileOperation.Delete, path, 0);
            }

            var bytes = info.Length;
            info.Delete();

            Log.FileDeleted(_logger, path, bytes);
            return new OperationResult(FileOperation.Delete, path, bytes);
        });

    /// <summary>
    /// Runs <paramref name="body"/> inside a span with timing, metrics and lifecycle logs, and maps
    /// raw file-system exceptions to <see cref="FileOpsException"/> with the right exit code.
    /// </summary>
    private OperationResult Execute(FileOperation operation, string path, Func<Activity?, OperationResult> body)
    {
        using var activity = FileOpsActivitySource.Source.StartActivity(FileOpsActivitySource.SpanName(operation));
        activity?.SetTag(FileOpsActivitySource.Tags.Operation, operation.ToString().ToLowerInvariant());
        activity?.SetTag(FileOpsActivitySource.Tags.Path, path);

        Log.OperationStarted(_logger, operation, path);
        var start = Stopwatch.GetTimestamp();

        try
        {
            var result = body(activity);
            var elapsed = Stopwatch.GetElapsedTime(start);

            activity?.SetTag(FileOpsActivitySource.Tags.Size, result.Bytes);
            activity?.SetTag(FileOpsActivitySource.Tags.ExitCode, (int)ExitCode.Success);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _metrics.Record(operation, "success", elapsed, result.Bytes);
            Log.OperationCompleted(_logger, operation, path, result.Bytes, elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            var elapsed = Stopwatch.GetElapsedTime(start);
            var failure = ex as FileOpsException ?? Translate(ex, path);

            activity?.SetTag(FileOpsActivitySource.Tags.ExitCode, (int)failure.ExitCode);
            activity?.SetTag(FileOpsActivitySource.Tags.ErrorType, ex.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, failure.Message);
            _metrics.Record(operation, failure.ExitCode.ToString().ToLowerInvariant(), elapsed, 0);
            Log.OperationFailed(_logger, failure.InnerException ?? ex, operation, path, failure.ExitCode, failure.Message);

            if (ReferenceEquals(failure, ex))
            {
                throw;
            }

            throw failure;
        }
    }

    private static FileOpsException Translate(Exception ex, string path) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException =>
            new FileOpsException(ExitCode.NotFound, $"not found: {path}", path, ex),
        UnauthorizedAccessException =>
            new FileOpsException(ExitCode.IoError, $"permission denied: {path}", path, ex),
        IOException =>
            new FileOpsException(ExitCode.IoError, $"i/o error on {path}: {ex.Message}", path, ex),
        _ =>
            new FileOpsException(ExitCode.InternalError, $"unexpected error on {path}: {ex.Message}", path, ex),
    };

    /// <summary>An empty argument is a usage error (exit 1), reported through the normal failure path.</summary>
    private static void RequirePath(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FileOpsException(ExitCode.UsageError, $"{name} must not be empty", value);
        }
    }

    private static void RequireFile(string path)
    {
        if (Directory.Exists(path))
        {
            throw new FileOpsException(ExitCode.UsageError, $"is a directory: {path}", path);
        }

        if (!File.Exists(path))
        {
            throw new FileOpsException(ExitCode.NotFound, $"file not found: {path}", path);
        }
    }

    private static void RejectDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            throw new FileOpsException(ExitCode.UsageError, $"is a directory: {path}", path);
        }
    }

    private static FileStream OpenForRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan);

    /// <summary>True when both paths refer to the same file, following symbolic links.</summary>
    private static bool SamePath(string a, string b)
    {
        static string Resolve(string p)
        {
            var full = Path.GetFullPath(p);
            try
            {
                return new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
            }
            catch (IOException)
            {
                return full;
            }
        }

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(Resolve(a), Resolve(b), comparison);
    }
}
