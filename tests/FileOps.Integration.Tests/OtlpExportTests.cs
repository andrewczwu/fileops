using FileOps.Core;
using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;
using FileOps.Integration.Tests.Support;

namespace FileOps.Integration.Tests;

/// <summary>
/// End-to-end OTLP export: the real executable, a real HTTP endpoint and the three signals arriving as
/// protobuf payloads. The unit tests prove each operation emits the right telemetry; these prove the CLI
/// host ships it: subscriptions by name, resource attributes, the export log level, and the flush on exit.
/// Payloads are checked by string presence because protobuf stores strings verbatim.
/// </summary>
public sealed class OtlpExportTests : IDisposable
{
    private const string Traces = "/v1/traces";
    private const string Metrics = "/v1/metrics";
    private const string Logs = "/v1/logs";

    private readonly TempDirectory _dir = new();
    private readonly OtlpReceiver _receiver = new();

    public void Dispose()
    {
        _receiver.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public void Exports_a_span_metrics_and_log_records_for_one_run()
    {
        _dir.WriteFile("a", "x");

        var result = Tool.Run(_dir.Path, _receiver.Environment(), "copy", "a", "b");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stderr); // the console stays silent without --log-level

        var traces = _receiver.For(Traces);
        var metrics = _receiver.For(Metrics);
        var logs = _receiver.For(Logs);
        Assert.NotEmpty(traces);
        Assert.NotEmpty(metrics);
        Assert.NotEmpty(logs);
        Assert.All(_receiver.Received, r =>
        {
            Assert.Equal("application/x-protobuf", r.ContentType);
            Assert.NotEmpty(r.Body);
        });

        Assert.Contains(traces, r => r.Contains(FileOpsActivitySource.SpanName(FileOperation.Copy)));
        Assert.Contains(traces, r => r.Contains(FileOpsActivitySource.Tags.Size));
        foreach (var signal in new[] { traces, metrics, logs })
        {
            Assert.Contains(signal, r => r.Contains("service.name") && r.Contains("fileops") && r.Contains("service.version"));
        }

        Assert.Contains(metrics, r => r.Contains(FileOpsMetrics.OperationsInstrument));
        Assert.Contains(metrics, r => r.Contains(FileOpsMetrics.DurationInstrument));
        Assert.Contains(metrics, r => r.Contains(FileOpsMetrics.BytesInstrument));
        Assert.Contains(metrics, r => r.Contains("success"));

        // Information and above by default: the copied record goes out, the Debug "Starting" one does not.
        Assert.Contains(logs, r => r.Contains("Copied {Source} to {Destination}"));
        Assert.DoesNotContain(logs, r => r.Contains("Starting {Operation}"));
    }

    [Fact]
    public void Log_level_governs_the_export_too()
    {
        _dir.WriteFile("a", "x");
        var environment = _receiver.Environment();
        environment["FILEOPS_LOG_LEVEL"] = "None";

        var result = Tool.Run(_dir.Path, environment, "copy", "a", "b");

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(_receiver.For(Traces)); // spans are not logs
        Assert.NotEmpty(_receiver.For(Metrics));
        Assert.Empty(_receiver.For(Logs)); // --log-level None silences every sink
    }

    [Fact]
    public void Failure_is_exported_as_an_error_span_and_the_summary_line_comes_last()
    {
        var environment = _receiver.Environment();
        environment["FILEOPS_LOG_LEVEL"] = "Debug";
        environment["FILEOPS_LOG_FORMAT"] = "json";

        var result = Tool.Run(_dir.Path, environment, "delete", "missing");

        Assert.Equal((int)ExitCode.NotFound, result.ExitCode);
        var traces = _receiver.For(Traces);
        Assert.Contains(traces, r => r.Contains(FileOpsActivitySource.SpanName(FileOperation.Delete)));
        Assert.Contains(traces, r => r.Contains("file not found: missing"));
        Assert.Contains(traces, r => r.Contains(typeof(FileOpsException).FullName!));
        Assert.Contains(_receiver.For(Metrics), r => r.Contains("notfound"));
        Assert.StartsWith("fileops: file not found", result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1], StringComparison.Ordinal);
    }
}
