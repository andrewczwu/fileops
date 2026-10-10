using System.Diagnostics;
using System.Diagnostics.Metrics;

using FileOps.Core.Telemetry;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;

namespace FileOps.Core.Tests.Support;

/// <summary>
/// A <see cref="FileOperations"/> instance whose logs, spans and metrics are captured in memory.
/// Each test gets its own meter factory, logger and listener so assertions are exact and tests can run in parallel.
/// </summary>
internal sealed class ObservedOperations : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly ActivityListener _listener;
    private readonly Activity _root;
    private readonly List<Activity> _spans = [];

    public ObservedOperations()
    {
        _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var meterFactory = _services.GetRequiredService<IMeterFactory>();

        Logger = new FakeLogger<FileOperations>();
        Operations = new MetricCollector<long>(meterFactory, FileOpsMetrics.MeterName, FileOpsMetrics.OperationsInstrument);
        Durations = new MetricCollector<double>(meterFactory, FileOpsMetrics.MeterName, FileOpsMetrics.DurationInstrument);
        Bytes = new MetricCollector<long>(meterFactory, FileOpsMetrics.MeterName, FileOpsMetrics.BytesInstrument);

        // Listeners are process-wide and tests run in parallel, so only keep spans that descend
        // from this harness's own root activity.
        _root = new Activity("test-root").Start();
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FileOpsActivitySource.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Parent != _root)
                {
                    return;
                }

                lock (_spans)
                {
                    _spans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);

        Sut = new FileOperations(Logger, new FileOpsMetrics(meterFactory));
    }

    public FileOperations Sut { get; }

    public FakeLogger<FileOperations> Logger { get; }

    public MetricCollector<long> Operations { get; }

    public MetricCollector<double> Durations { get; }

    public MetricCollector<long> Bytes { get; }

    /// <summary>All log records emitted so far.</summary>
    public IReadOnlyList<FakeLogRecord> Logs => Logger.Collector.GetSnapshot();

    /// <summary>All completed spans so far.</summary>
    public IReadOnlyList<Activity> Spans
    {
        get
        {
            lock (_spans)
            {
                return [.. _spans];
            }
        }
    }

    /// <summary>The single log record with the given event id; fails if there is not exactly one.</summary>
    public FakeLogRecord LogRecord(int eventId) => Assert.Single(Logs, r => r.Id.Id == eventId);

    /// <summary>The single span for the given operation; fails if there is not exactly one.</summary>
    public Activity Span(FileOperation operation) =>
        Assert.Single(Spans, s => s.DisplayName == FileOpsActivitySource.SpanName(operation));

    /// <summary>The single operations-counter measurement; fails if there is not exactly one.</summary>
    public CollectedMeasurement<long> OperationMeasurement() => Assert.Single(Operations.GetMeasurementSnapshot());

    public void Dispose()
    {
        _listener.Dispose();
        _root.Dispose();
        Operations.Dispose();
        Durations.Dispose();
        Bytes.Dispose();
        _services.Dispose();
    }
}
