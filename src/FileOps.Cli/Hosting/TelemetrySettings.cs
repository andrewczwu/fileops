using System.CommandLine;

using Microsoft.Extensions.Logging;

namespace FileOps.Cli.Hosting;

/// <summary>Resolved telemetry configuration for one invocation.</summary>
/// <param name="ChosenLevel">
/// Lowest log level the user asked for, by option or environment; <see langword="null"/> when they chose none.
/// <see cref="LogLevel.None"/> is an explicit choice and silences every sink.
/// </param>
/// <param name="JsonLogs">Emit one JSON object per line instead of text.</param>
/// <param name="OtlpEndpoint">OTLP collector endpoint, or <see langword="null"/> to skip exporting.</param>
internal sealed record TelemetrySettings(LogLevel? ChosenLevel, bool JsonLogs, string? OtlpEndpoint)
{
    /// <summary>Level for the console logger: what the user chose, otherwise silent.</summary>
    public LogLevel ConsoleLevel => ChosenLevel ?? LogLevel.None;

    /// <summary>
    /// Level for the logging pipeline as a whole. When the user chose nothing and an OTLP endpoint is
    /// set, the collector still receives Information and above while the console stays silent.
    /// </summary>
    public LogLevel PipelineLevel => ChosenLevel ?? (OtlpEndpoint is null ? LogLevel.None : LogLevel.Information);

    /// <summary>Standard OpenTelemetry variable; when set, traces, metrics and logs are exported over OTLP.</summary>
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Combines parsed global options with environment variables.</summary>
    public static TelemetrySettings From(ParseResult parseResult, GlobalOptions options)
    {
        var level = parseResult.GetValue(options.LogLevel);
        if (parseResult.GetValue(options.Verbose) && (level is null || level > LogLevel.Information))
        {
            level = LogLevel.Information;
        }

        var endpoint = Environment.GetEnvironmentVariable(OtlpEndpointVariable);
        return new TelemetrySettings(
            level,
            parseResult.GetValue(options.LogFormat) == "json",
            string.IsNullOrWhiteSpace(endpoint) ? null : endpoint);
    }
}
