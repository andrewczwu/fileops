using System.Reflection;

using FileOps.Core;
using FileOps.Core.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FileOps.Cli.Hosting;

/// <summary>
/// Owns the service container and telemetry pipelines for one command invocation.
/// Disposing it flushes every pending log record and span before the process exits.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private const string ServiceName = "fileops";

    private static readonly string ServiceVersion =
        typeof(AppHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private readonly ServiceProvider _services;
    private readonly TracerProvider? _tracerProvider;
    private readonly MeterProvider? _meterProvider;

    private AppHost(ServiceProvider services, TracerProvider? tracerProvider, MeterProvider? meterProvider)
    {
        _services = services;
        _tracerProvider = tracerProvider;
        _meterProvider = meterProvider;
        Operations = services.GetRequiredService<FileOperations>();
    }

    /// <summary>The operations service, fully instrumented.</summary>
    public FileOperations Operations { get; }

    /// <summary>Builds logging, metrics and (optionally) OTLP export according to <paramref name="settings"/>.</summary>
    public static AppHost Create(TelemetrySettings settings)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            // --log-level governs every sink, including an explicit None. When nothing was chosen and an
            // OTLP endpoint is set, the collector still receives Information and above; the console stays silent.
            var exporting = settings.OtlpEndpoint is not null;
            logging.SetMinimumLevel(settings.PipelineLevel);
            // Stamp every record with the current span so logs and traces can be joined.
            logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

            if (settings.ConsoleLevel != LogLevel.None)
            {
                // All log output goes to stderr so stdout stays reserved for command results.
                logging.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
                if (settings.JsonLogs)
                {
                    logging.AddJsonConsole(o =>
                    {
                        o.IncludeScopes = true;
                        o.UseUtcTimestamp = true;
                        o.TimestampFormat = "O";
                    });
                }
                else
                {
                    logging.AddSimpleConsole(o =>
                    {
                        o.SingleLine = true;
                        o.IncludeScopes = true;
                        o.TimestampFormat = "HH:mm:ss.fff ";
                    });
                }
            }

            if (exporting)
            {
                logging.AddOpenTelemetry(o =>
                {
                    o.SetResourceBuilder(CreateResource());
                    o.IncludeScopes = true;
                    o.AddOtlpExporter();
                });
            }
        });

        services.AddMetrics();
        services.AddSingleton<FileOpsMetrics>();
        services.AddSingleton<FileOperations>();

        TracerProvider? tracerProvider = null;
        MeterProvider? meterProvider = null;
        if (settings.OtlpEndpoint is not null)
        {
            tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(CreateResource())
                .AddSource(FileOpsActivitySource.Name)
                .AddOtlpExporter()
                .Build();

            meterProvider = Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(CreateResource())
                .AddMeter(FileOpsMetrics.MeterName)
                .AddOtlpExporter()
                .Build();
        }

        return new AppHost(services.BuildServiceProvider(), tracerProvider, meterProvider);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Order matters: exporters first (they may log), then the logger provider drains its queue.
        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();
        _services.Dispose();
    }

    private static ResourceBuilder CreateResource() =>
        ResourceBuilder.CreateDefault().AddService(ServiceName, serviceVersion: ServiceVersion);
}
