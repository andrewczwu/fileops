using System.CommandLine;

using Microsoft.Extensions.Logging;

namespace FileOps.Cli;

/// <summary>
/// Options accepted by every command. Defaults come from environment variables so CI and
/// scripts can turn on telemetry without touching the command line.
/// </summary>
internal sealed class GlobalOptions
{
    /// <summary>Environment variable that sets the default log level.</summary>
    public const string LogLevelVariable = "FILEOPS_LOG_LEVEL";

    /// <summary>Environment variable that sets the default log format.</summary>
    public const string LogFormatVariable = "FILEOPS_LOG_FORMAT";

    /// <summary>Minimum level of log records written to stderr. <c>None</c> disables logging.</summary>
    public Option<LogLevel> LogLevel { get; } = new("--log-level")
    {
        Description = $"Minimum log level written to stderr (Trace, Debug, Information, Warning, Error, Critical, None). Env: {LogLevelVariable}.",
        Recursive = true,
        DefaultValueFactory = _ => ParseLevel(Environment.GetEnvironmentVariable(LogLevelVariable)),
    };

    /// <summary>Log record format: human-readable text or one JSON object per line.</summary>
    public Option<string> LogFormat { get; } = CreateFormatOption();

    /// <summary>Shorthand for <c>--log-level Information</c>.</summary>
    public Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Shorthand for --log-level Information.",
        Recursive = true,
    };

    /// <summary>Every global option, in help order.</summary>
    public IEnumerable<Option> All => [LogLevel, LogFormat, Verbose];

    private static Option<string> CreateFormatOption()
    {
        var option = new Option<string>("--log-format")
        {
            Description = $"Log format: text or json. Env: {LogFormatVariable}.",
            Recursive = true,
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable(LogFormatVariable)?.ToLowerInvariant() is "json" ? "json" : "text",
        };
        option.AcceptOnlyFromAmong("text", "json");
        return option;
    }

    private static LogLevel ParseLevel(string? value) =>
        Enum.TryParse<LogLevel>(value, ignoreCase: true, out var level) ? level : Microsoft.Extensions.Logging.LogLevel.None;
}
