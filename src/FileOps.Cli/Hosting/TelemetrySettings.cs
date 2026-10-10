using System.CommandLine;

using Microsoft.Extensions.Logging;

namespace FileOps.Cli.Hosting;

/// <summary>Resolved telemetry configuration for one invocation.</summary>
/// <param name="MinimumLevel">Lowest log level to emit; <see cref="LogLevel.None"/> disables the console logger.</param>
/// <param name="JsonLogs">Emit one JSON object per line instead of text.</param>
/// <param name="OtlpEndpoint">OTLP collector endpoint, or <see langword="null"/> to skip exporting.</param>
internal sealed record TelemetrySettings(LogLevel MinimumLevel, bool JsonLogs, string? OtlpEndpoint)
{
    /// <summary>Standard OpenTelemetry variable; when set, traces, metrics and logs are exported over OTLP.</summary>
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Combines parsed global options with environment variables.</summary>
    public static TelemetrySettings From(ParseResult parseResult, GlobalOptions options)
    {
        var level = parseResult.GetValue(options.LogLevel);
        if (parseResult.GetValue(options.Verbose) && level > LogLevel.Information)
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
