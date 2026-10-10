using System.Diagnostics;
using System.Security.Cryptography;

using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;

namespace FileOps.Core.Tests;

public sealed class CopyTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ObservedOperations _ops = new();

    public void Dispose()
    {
        _ops.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public void Copies_content_byte_for_byte()
    {
        var content = RandomNumberGenerator.GetBytes((3 * 1024 * 1024) + 17); // crosses several 1 MiB buffer boundaries
        var source = _dir.WriteFile("src.bin", content);
        var destination = _dir.File("dst.bin");

        var result = _ops.Sut.Copy(source, destination);

        Assert.Equal(content.Length, result.Bytes);
        Assert.Equal(SHA256.HashData(content), SHA256.HashData(File.ReadAllBytes(destination)));
    }

    [Fact]
    public void Copies_empty_file()
    {
        var source = _dir.WriteFile("empty", string.Empty);
        var destination = _dir.File("copy");

        var result = _ops.Sut.Copy(source, destination);

        Assert.Equal(0, result.Bytes);
        Assert.True(File.Exists(destination));
    }

    [NonRootFact]
    public void Preserves_unix_permissions()
    {
        var source = _dir.WriteFile("script.sh", "#!/bin/sh\n");
        File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);
        var destination = _dir.File("copy.sh");

        _ops.Sut.Copy(source, destination);

        Assert.Equal(File.GetUnixFileMode(source), File.GetUnixFileMode(destination));
    }

    [NonRootFact]
    public void Temp_file_carries_the_source_mode_before_any_content_is_written()
    {
        // A private source must not be world-readable even for the instant between write and rename.
        var source = _dir.WriteFile("private", new string('x', 64 * 1024));
        File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var destination = _dir.File("copy");
        UnixFileMode? observed = null;
        using var watcher = new FileSystemWatcher(_dir.Path, ".*.tmp") { EnableRaisingEvents = true };
        watcher.Created += (_, e) => observed ??= TryGetMode(e.FullPath);

        _ops.Sut.Copy(source, destination);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(destination));
        if (observed is { } mode)
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }

        static UnixFileMode? TryGetMode(string path)
        {
            try
            {
                return File.GetUnixFileMode(path);
            }
            catch (IOException)
            {
                return null; // already renamed
            }
        }
    }

    [Fact]
    public void Empty_source_or_destination_is_a_usage_error()
    {
        var source = _dir.WriteFile("src", "x");

        Assert.Equal(ExitCode.UsageError, Assert.Throws<FileOpsException>(() => _ops.Sut.Copy(source, "")).ExitCode);
        Assert.Equal(ExitCode.UsageError, Assert.Throws<FileOpsException>(() => _ops.Sut.Copy("", _dir.File("dst"))).ExitCode);
    }

    [Fact]
    public void Fails_with_NotFound_when_source_missing()
    {
        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Copy(_dir.File("nope"), _dir.File("dst")));

        Assert.Equal(ExitCode.NotFound, ex.ExitCode);
        Assert.False(File.Exists(_dir.File("dst")));
    }

    [Fact]
    public void Refuses_to_overwrite_by_default_and_leaves_destination_untouched()
    {
        var source = _dir.WriteFile("src", "new");
        var destination = _dir.WriteFile("dst", "old");

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Copy(source, destination));

        Assert.Equal(ExitCode.AlreadyExists, ex.ExitCode);
        Assert.Equal("old", File.ReadAllText(destination));
    }

    [Fact]
    public void Overwrites_when_forced()
    {
        var source = _dir.WriteFile("src", "new");
        var destination = _dir.WriteFile("dst", "old");

        _ops.Sut.Copy(source, destination, overwrite: true);

        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public void Rejects_copying_a_file_onto_itself()
    {
        var source = _dir.WriteFile("same", "data");
        var alias = Path.Combine(_dir.Path, ".", "same");

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Copy(source, alias, overwrite: true));

        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
        Assert.Equal("data", File.ReadAllText(source));
    }

    [Fact]
    public void Rejects_directory_as_source()
    {
        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Copy(_dir.Path, _dir.File("dst")));

        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [NonRootFact]
    public void Maps_permission_denied_to_IoError()
    {
        var source = _dir.WriteFile("secret", "data");
        File.SetUnixFileMode(source, UnixFileMode.None);

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Copy(source, _dir.File("dst")));

        Assert.Equal(ExitCode.IoError, ex.ExitCode);
        Assert.IsType<UnauthorizedAccessException>(ex.InnerException);
        Assert.Empty(_dir.LeftoverTempFiles());
        File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void Span_records_source_and_destination()
    {
        var source = _dir.WriteFile("src", "12345");
        var destination = _dir.File("dst");

        _ops.Sut.Copy(source, destination);

        var span = _ops.Span(FileOperation.Copy);
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
        Assert.Equal(source, span.GetTagItem(FileOpsActivitySource.Tags.Source));
        Assert.Equal(destination, span.GetTagItem(FileOpsActivitySource.Tags.Destination));
        Assert.Equal(5L, span.GetTagItem(FileOpsActivitySource.Tags.Size));

        var copied = _ops.LogRecord(LogEvents.FileCopied);
        Assert.Equal(source, copied.GetStructuredStateValue("Source"));
        Assert.Equal(destination, copied.GetStructuredStateValue("Destination"));
    }
}
