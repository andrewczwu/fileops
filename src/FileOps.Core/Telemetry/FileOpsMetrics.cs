using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FileOps.Core.Telemetry;

/// <summary>
/// Instruments for the <c>FileOps</c> meter. Created through <see cref="IMeterFactory"/> so tests
/// can own an isolated meter per test and assert on exact measurements.
/// </summary>
public sealed class FileOpsMetrics
{
    /// <summary>Meter name to pass to <c>AddMeter</c> / <c>MetricCollector</c>.</summary>
    public const string MeterName = "FileOps";

    /// <summary>Counter of operations, tagged by <c>operation</c> and <c>outcome</c>.</summary>
    public const string OperationsInstrument = "fileops.operations";

    /// <summary>Histogram of operation wall-clock duration in seconds.</summary>
    public const string DurationInstrument = "fileops.operation.duration";

    /// <summary>Counter of bytes written to or removed from disk.</summary>
    public const string BytesInstrument = "fileops.bytes";

    private readonly Counter<long> _operations;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _bytes;

    /// <summary>Creates the instruments on a meter obtained from <paramref name="meterFactory"/>.</summary>
    public FileOpsMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _operations = meter.CreateCounter<long>(OperationsInstrument, unit: "{operation}", description: "Number of file operations attempted.");
        _duration = meter.CreateHistogram<double>(DurationInstrument, unit: "s", description: "Wall-clock duration of each file operation.");
        _bytes = meter.CreateCounter<long>(BytesInstrument, unit: "By", description: "Bytes written or removed by file operations.");
    }

    /// <summary>Records one finished operation.</summary>
    /// <param name="operation">Which operation ran.</param>
    /// <param name="outcome"><c>success</c> or the lower-case <see cref="ExitCode"/> name.</param>
    /// <param name="duration">How long it took.</param>
    /// <param name="bytes">Bytes written or removed; zero on failure.</param>
    public void Record(FileOperation operation, string outcome, TimeSpan duration, long bytes)
    {
        var tags = new TagList
        {
            { "operation", operation.ToString().ToLowerInvariant() },
            { "outcome", outcome },
        };
        _operations.Add(1, tags);
        _duration.Record(duration.TotalSeconds, tags);
        if (bytes > 0)
        {
            _bytes.Add(bytes, tags);
        }
    }
}
