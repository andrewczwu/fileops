using System.Diagnostics;
using System.Diagnostics.Metrics;

using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileOps.Core.Tests;

/// <summary>
/// Cross-cutting guarantees every operation must honour, so downstream consumers (dashboards,
/// alerting, the CI failure summariser) can rely on one shape regardless of command.
/// </summary>
public sealed class TelemetryContractTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ObservedOperations _ops = new();

    public void Dispose()
    {
        _ops.Dispose();
        _dir.Dispose();
    }

    /// <summary>Every <see cref="FileOperation"/>, so a new member is covered automatically.</summary>
    public static TheoryData<FileOperation> AllOperations { get; } = BuildAllOperations();

    private static TheoryData<FileOperation> BuildAllOperations()
    {
        var data = new TheoryData<FileOperation>();
        foreach (var operation in Enum.GetValues<FileOperation>())
        {
            data.Add(operation);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void Successful_operation_emits_started_then_completed_with_one_ok_span_and_one_metric(FileOperation operation)
    {
        Run(operation);

        var ids = _ops.Logs.Select(r => r.Id.Id).ToList();
        Assert.Equal(LogEvents.OperationStarted, ids.First());
        Assert.Equal(LogEvents.OperationCompleted, ids.Last());
        Assert.DoesNotContain(_ops.Logs, r => r.Level >= LogLevel.Warning);

        var span = Assert.Single(_ops.Spans);
        Assert.Equal(FileOpsActivitySource.SpanName(operation), span.DisplayName);
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
        Assert.Equal(operation.ToString().ToLowerInvariant(), span.GetTagItem(FileOpsActivitySource.Tags.Operation));
        Assert.NotNull(span.GetTagItem(FileOpsActivitySource.Tags.Path));
        Assert.True(span.Duration > TimeSpan.Zero);

        var count = Assert.Single(_ops.Operations.GetMeasurementSnapshot());
        Assert.Equal(operation.ToString().ToLowerInvariant(), count.Tags["operation"]);
        Assert.Equal("success", count.Tags["outcome"]);
        var duration = Assert.Single(_ops.Durations.GetMeasurementSnapshot());
        Assert.True(duration.Value > 0);
        Assert.Equal(count.Tags, duration.Tags);
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public void Failed_operation_emits_error_log_with_exit_code_and_error_span(FileOperation operation)
    {
        var ex = Assert.Throws<FileOpsException>(() => RunFailing(operation));

        var failed = _ops.LogRecord(LogEvents.OperationFailed);
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal(ex.ExitCode.ToString(), failed.GetStructuredStateValue("ExitCode"));
        Assert.Equal(ex.Message, failed.GetStructuredStateValue("Reason"));
        Assert.DoesNotContain(_ops.Logs, r => r.Id.Id == LogEvents.OperationCompleted);

        var span = Assert.Single(_ops.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(ex.Message, span.StatusDescription);
        Assert.Equal((int)ex.ExitCode, span.GetTagItem(FileOpsActivitySource.Tags.ExitCode));
        Assert.Equal(typeof(FileOpsException).FullName, span.GetTagItem(FileOpsActivitySource.Tags.ErrorType));

        Assert.Equal(ex.ExitCode.ToString().ToLowerInvariant(), _ops.OperationMeasurement().Tags["outcome"]);
    }

    [Fact]
    public void Operations_still_work_when_nobody_is_listening()
    {
        // Spans are optional: with no listener StartActivity returns null and nothing may throw.
        _ops.Dispose();
        using var quiet = new TempDirectory();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var sut = new FileOperations(
            NullLogger<FileOperations>.Instance,
            new FileOpsMetrics(services.GetRequiredService<IMeterFactory>()));

        var result = sut.Create(quiet.File("x"), "ok");

        Assert.Equal(2, result.Bytes);
    }

    private void Run(FileOperation operation)
    {
        var a = _dir.WriteFile("a", "aa");
        var b = _dir.WriteFile("b", "bb");
        switch (operation)
        {
            case FileOperation.Create: _ops.Sut.Create(_dir.File("new"), "x"); break;
            case FileOperation.Copy: _ops.Sut.Copy(a, _dir.File("copy")); break;
            case FileOperation.Combine: _ops.Sut.Combine(a, b, _dir.File("out")); break;
            case FileOperation.Delete: _ops.Sut.Delete(a); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private void RunFailing(FileOperation operation)
    {
        var missing = _dir.File("missing");
        switch (operation)
        {
            case FileOperation.Create: _ops.Sut.Create(Path.Combine(missing, "x")); break;
            case FileOperation.Copy: _ops.Sut.Copy(missing, _dir.File("copy")); break;
            case FileOperation.Combine: _ops.Sut.Combine(missing, missing, _dir.File("out")); break;
            case FileOperation.Delete: _ops.Sut.Delete(missing); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}
