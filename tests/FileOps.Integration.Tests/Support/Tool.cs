using System.Diagnostics;
using System.Text.Json;

namespace FileOps.Integration.Tests.Support;

/// <summary>
/// Launches the real <c>fileops</c> executable as a child process and captures everything it produces.
/// By default runs <c>fileops.dll</c> from the test output directory; set <c>FILEOPS_BIN</c> to point
/// at a published native binary to run the same suite against the release artefact.
/// </summary>
internal static class Tool
{
    /// <summary>Environment variable that overrides which binary the tests exercise.</summary>
    public const string BinaryVariable = "FILEOPS_BIN";

    /// <summary>
    /// When set, every invocation's stderr is appended to <c>&lt;dir&gt;/cli-runs.ndjson</c>, preceded by a
    /// header record naming the arguments and exit code. CI publishes this file so a failure can be
    /// diagnosed from the telemetry alone, without re-running the suite.
    /// </summary>
    public const string LogDirectoryVariable = "FILEOPS_TEST_LOG_DIR";

    private static readonly Lock LogFileLock = new();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>Path of the executable under test, for diagnostics.</summary>
    public static string Description =>
        Environment.GetEnvironmentVariable(BinaryVariable) is { Length: > 0 } bin
            ? bin
            : $"dotnet {Path.Combine(AppContext.BaseDirectory, "fileops.dll")}";

    /// <summary>Runs the tool with JSON logging at Debug level so every test has log evidence to inspect.</summary>
    public static CliResult Run(string workingDirectory, params string[] args) =>
        Run(workingDirectory, new Dictionary<string, string?>
        {
            ["FILEOPS_LOG_FORMAT"] = "json",
            ["FILEOPS_LOG_LEVEL"] = "Debug",
        }, args);

    /// <summary>Runs the tool with exactly the given environment overrides (null removes a variable).</summary>
    public static CliResult Run(string workingDirectory, IReadOnlyDictionary<string, string?> environment, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (Environment.GetEnvironmentVariable(BinaryVariable) is { Length: > 0 } binary)
        {
            startInfo.FileName = binary;
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "fileops.dll"));
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Start from a clean slate so the host's settings cannot leak into assertions.
        startInfo.Environment.Remove("FILEOPS_LOG_FORMAT");
        startInfo.Environment.Remove("FILEOPS_LOG_LEVEL");
        startInfo.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
        foreach (var (key, value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"could not start {startInfo.FileName}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"fileops did not exit within {Timeout}: {string.Join(' ', args)}");
        }

        var result = new CliResult(process.ExitCode, stdout.Result, stderr.Result);
        RecordRun(args, result);
        return result;
    }

    private static void RecordRun(string[] args, CliResult result)
    {
        if (Environment.GetEnvironmentVariable(LogDirectoryVariable) is not { Length: > 0 } directory)
        {
            return;
        }

        var header = JsonSerializer.Serialize(new { run = new { args, exitCode = result.ExitCode, binary = Description } });
        var records = result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('{'));

        lock (LogFileLock)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllLines(Path.Combine(directory, "cli-runs.ndjson"), records.Prepend(header));
        }
    }
}

/// <summary>Captured outcome of one CLI invocation.</summary>
internal sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Lines on stderr that are JSON log records, parsed.</summary>
    public IReadOnlyList<JsonElement> LogRecords { get; } = [.. Stderr
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.StartsWith('{'))
        .Select(line => JsonDocument.Parse(line).RootElement)];

    /// <summary>Lines on stderr that are user-facing messages (the <c>fileops: ...</c> summary).</summary>
    public IReadOnlyList<string> Messages { get; } = [.. Stderr
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(line => line.StartsWith("fileops:", StringComparison.Ordinal))];

    /// <summary>The single log record with the given event id; fails if there is not exactly one.</summary>
    public JsonElement Log(int eventId) =>
        Assert.Single(LogRecords, r => r.GetProperty("EventId").GetInt32() == eventId);

    /// <summary>Whether any log record with the given event id exists.</summary>
    public bool HasLog(int eventId) => LogRecords.Any(r => r.GetProperty("EventId").GetInt32() == eventId);

    /// <summary>The event ids in emission order.</summary>
    public IEnumerable<int> EventIds => LogRecords.Select(r => r.GetProperty("EventId").GetInt32());
}
