using System.Diagnostics;

using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;

using Microsoft.Extensions.Logging;

namespace FileOps.Core.Tests;

public sealed class CreateTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ObservedOperations _ops = new();

    public void Dispose()
    {
        _ops.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public void Creates_empty_file_when_no_text_given()
    {
        var path = _dir.File("empty.txt");

        var result = _ops.Sut.Create(path);

        Assert.Equal(new OperationResult(FileOperation.Create, path, 0), result);
        Assert.Equal([], File.ReadAllBytes(path));
    }

    [Fact]
    public void Writes_text_exactly_as_given_without_trailing_newline()
    {
        var path = _dir.File("hello.txt");

        var result = _ops.Sut.Create(path, "héllo");

        Assert.Equal("héllo", File.ReadAllText(path));
        Assert.Equal(6, result.Bytes); // UTF-8: é is two bytes, no BOM, no newline.
    }

    [Fact]
    public void Refuses_to_overwrite_existing_file_by_default()
    {
        var path = _dir.WriteFile("exists.txt", "original");

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Create(path, "new"));

        Assert.Equal(ExitCode.AlreadyExists, ex.ExitCode);
        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void Overwrites_when_forced()
    {
        var path = _dir.WriteFile("exists.txt", "original");

        _ops.Sut.Create(path, "new", overwrite: true);

        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public void Fails_with_NotFound_when_directory_is_missing()
    {
        var path = Path.Combine(_dir.Path, "missing", "file.txt");

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Create(path));

        Assert.Equal(ExitCode.NotFound, ex.ExitCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_path_is_a_usage_error_with_failure_telemetry(string path)
    {
        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Create(path));

        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
        Assert.Equal("UsageError", _ops.LogRecord(LogEvents.OperationFailed).GetStructuredStateValue("ExitCode"));
        Assert.Equal(ActivityStatusCode.Error, _ops.Span(FileOperation.Create).Status);
    }

    [Fact]
    public void Accepts_names_close_to_the_file_system_limit()
    {
        // 240 bytes is a legal name; the temp file's decoration must not push it past 255.
        var path = _dir.File(new string('n', 240));

        var result = _ops.Sut.Create(path, "ok");

        Assert.Equal(2, result.Bytes);
        Assert.Equal("ok", File.ReadAllText(path));
        Assert.Empty(_dir.LeftoverTempFiles());
    }

    [NonRootFact]
    public void Overwrite_keeps_the_existing_files_mode()
    {
        var path = _dir.WriteFile("secret", "old");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        _ops.Sut.Create(path, "new", overwrite: true);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Refuses_path_that_is_a_directory()
    {
        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Create(_dir.Path));

        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [Fact]
    public void Emits_log_span_and_metric_evidence_on_success()
    {
        var path = _dir.File("observed.txt");

        _ops.Sut.Create(path, "abc");

        var created = _ops.LogRecord(LogEvents.FileCreated);
        Assert.Equal(LogLevel.Information, created.Level);
        Assert.Equal(path, created.GetStructuredStateValue("Path"));
        Assert.Equal("3", created.GetStructuredStateValue("Bytes"));

        var completed = _ops.LogRecord(LogEvents.OperationCompleted);
        Assert.Equal("Create", completed.GetStructuredStateValue("Operation"));

        var span = _ops.Span(FileOperation.Create);
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
        Assert.Equal("create", span.GetTagItem(FileOpsActivitySource.Tags.Operation));
        Assert.Equal(3L, span.GetTagItem(FileOpsActivitySource.Tags.Size));
        Assert.Equal(0, span.GetTagItem(FileOpsActivitySource.Tags.ExitCode));

        var measurement = _ops.OperationMeasurement();
        Assert.Equal(1, measurement.Value);
        Assert.Equal("create", measurement.Tags["operation"]);
        Assert.Equal("success", measurement.Tags["outcome"]);
        Assert.Equal(3, Assert.Single(_ops.Bytes.GetMeasurementSnapshot()).Value);
    }

    [Fact]
    public void Emits_error_evidence_on_failure()
    {
        var path = _dir.WriteFile("exists.txt", "x");

        Assert.Throws<FileOpsException>(() => _ops.Sut.Create(path));

        var failed = _ops.LogRecord(LogEvents.OperationFailed);
        Assert.Equal(LogLevel.Error, failed.Level);
        Assert.Equal("AlreadyExists", failed.GetStructuredStateValue("ExitCode"));
        Assert.DoesNotContain(_ops.Logs, r => r.Id.Id == LogEvents.FileCreated);

        var span = _ops.Span(FileOperation.Create);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal((int)ExitCode.AlreadyExists, span.GetTagItem(FileOpsActivitySource.Tags.ExitCode));

        Assert.Equal("alreadyexists", _ops.OperationMeasurement().Tags["outcome"]);
        Assert.Empty(_ops.Bytes.GetMeasurementSnapshot());
    }
}
