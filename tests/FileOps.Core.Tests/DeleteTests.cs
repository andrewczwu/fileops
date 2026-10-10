using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;

using Microsoft.Extensions.Logging;

namespace FileOps.Core.Tests;

public sealed class DeleteTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ObservedOperations _ops = new();

    public void Dispose()
    {
        _ops.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public void Deletes_file_and_reports_its_size()
    {
        var path = _dir.WriteFile("gone", "1234");

        var result = _ops.Sut.Delete(path);

        Assert.False(File.Exists(path));
        Assert.Equal(4, result.Bytes);
        Assert.Equal("4", _ops.LogRecord(LogEvents.FileDeleted).GetStructuredStateValue("Bytes"));
    }

    [Fact]
    public void Fails_with_NotFound_when_file_missing()
    {
        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Delete(_dir.File("missing")));

        Assert.Equal(ExitCode.NotFound, ex.ExitCode);
        Assert.Equal("notfound", _ops.OperationMeasurement().Tags["outcome"]);
    }

    [Fact]
    public void Succeeds_with_warning_when_missing_and_ignoreMissing()
    {
        var path = _dir.File("missing");

        var result = _ops.Sut.Delete(path, ignoreMissing: true);

        Assert.Equal(0, result.Bytes);
        var warning = _ops.LogRecord(LogEvents.DeleteSkippedMissing);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(path, warning.GetStructuredStateValue("Path"));
        Assert.Equal("success", _ops.OperationMeasurement().Tags["outcome"]);
    }

    [Fact]
    public void Refuses_to_delete_a_directory()
    {
        var sub = Directory.CreateDirectory(_dir.File("sub")).FullName;

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Delete(sub));

        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
        Assert.True(Directory.Exists(sub));
    }

    [NonRootFact]
    public void Maps_permission_denied_to_IoError()
    {
        var sub = Directory.CreateDirectory(_dir.File("locked")).FullName;
        var path = Path.Combine(sub, "file");
        File.WriteAllText(path, "x");
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Delete(path));
            Assert.Equal(ExitCode.IoError, ex.ExitCode);
        }
        finally
        {
            File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
