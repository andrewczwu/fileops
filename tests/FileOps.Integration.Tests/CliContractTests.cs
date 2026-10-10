using System.Text.RegularExpressions;

using FileOps.Core;
using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;
using FileOps.Integration.Tests.Support;

namespace FileOps.Integration.Tests;

/// <summary>
/// The contract scripts and CI rely on: stdout is reserved for results, stderr carries logs and the
/// summary line, exit codes are stable, and the log stream is self-describing enough to diagnose
/// a failure without re-running the command.
/// </summary>
public sealed partial class CliContractTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Stdout_is_empty_on_success_and_on_failure()
    {
        _dir.WriteFile("a", "x");

        var success = Tool.Run(_dir.Path, "copy", "a", "b");
        var failure = Tool.Run(_dir.Path, "copy", "missing", "c");

        Assert.Equal(0, success.ExitCode);
        Assert.Equal(string.Empty, success.Stdout);
        Assert.NotEqual(0, failure.ExitCode);
        Assert.Equal(string.Empty, failure.Stdout);
    }

    [Fact]
    public void Logs_are_one_json_object_per_stderr_line_with_stable_schema()
    {
        _dir.WriteFile("a", "x");

        var result = Tool.Run(_dir.Path, "copy", "a", "b");

        Assert.NotEmpty(result.LogRecords);
        Assert.All(result.LogRecords, record =>
        {
            Assert.True(record.TryGetProperty("Timestamp", out _));
            Assert.True(record.TryGetProperty("EventId", out _));
            Assert.True(record.TryGetProperty("LogLevel", out _));
            Assert.True(record.TryGetProperty("Category", out _));
            Assert.True(record.TryGetProperty("Message", out _));
            Assert.True(record.TryGetProperty("State", out _));
        });
        Assert.Equal([LogEvents.OperationStarted, LogEvents.FileCopied, LogEvents.OperationCompleted], result.EventIds);
    }

    [Fact]
    public void Nothing_is_logged_by_default()
    {
        _dir.WriteFile("a", "x");

        var result = Tool.Run(_dir.Path, new Dictionary<string, string?>(), "copy", "a", "b");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void Verbose_flag_enables_human_readable_logs()
    {
        _dir.WriteFile("a", "x");

        var result = Tool.Run(_dir.Path, new Dictionary<string, string?>(), "--verbose", "copy", "a", "b");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.LogRecords);
        Assert.Contains($"[{LogEvents.FileCopied}]", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("Copied a to b (1 bytes)", result.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_line_options_override_environment()
    {
        _dir.WriteFile("a", "x");

        var result = Tool.Run(_dir.Path, "--log-level", "None", "copy", "a", "b");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Theory]
    [InlineData("copy", "missing", "dst")]
    [InlineData("combine", "missing", "missing", "dst")]
    [InlineData("delete", "missing")]
    [InlineData("delete", ".")]
    public void Process_exit_code_matches_the_logged_exit_code(params string[] args)
    {
        var result = Tool.Run(_dir.Path, args);

        Assert.NotEqual(0, result.ExitCode);
        var failed = result.Log(LogEvents.OperationFailed);
        Assert.Equal("Error", failed.GetProperty("LogLevel").GetString());
        var loggedCode = Enum.Parse<ExitCode>(failed.GetProperty("State").GetProperty("ExitCode").GetString()!);
        Assert.Equal((int)loggedCode, result.ExitCode);
        Assert.Equal($"fileops: {failed.GetProperty("State").GetProperty("Reason").GetString()}", Assert.Single(result.Messages));
        Assert.False(result.HasLog(LogEvents.OperationCompleted));
    }

    [Fact]
    public void Summary_line_comes_after_all_log_records()
    {
        var result = Tool.Run(_dir.Path, "delete", "missing");

        var lines = result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("fileops:", lines[^1], StringComparison.Ordinal);
        Assert.All(lines[..^1], line => Assert.StartsWith("{", line, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData]
    [InlineData("create")]
    [InlineData("copy", "only-one")]
    [InlineData("bogus")]
    [InlineData("create", "x", "--nope")]
    public void Invalid_usage_exits_1_and_prints_help_on_stderr(params string[] args)
    {
        var result = Tool.Run(_dir.Path, args);

        Assert.Equal((int)ExitCode.UsageError, result.ExitCode);
        Assert.NotEqual(string.Empty, result.Stderr);
        Assert.Empty(result.LogRecords); // Nothing ran, so no telemetry was produced.
    }

    [Fact]
    public void Help_lists_every_command()
    {
        var result = Tool.Run(_dir.Path, "--help");

        Assert.Equal(0, result.ExitCode);
        foreach (var command in new[] { "create", "copy", "combine", "delete" })
        {
            Assert.Contains(command, result.Stdout, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Version_is_semver()
    {
        var result = Tool.Run(_dir.Path, "--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(SemVer(), result.Stdout.Trim());
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemVer();
}
