using System.Security.Cryptography;

using FileOps.Core;
using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;
using FileOps.Integration.Tests.Support;

namespace FileOps.Integration.Tests;

/// <summary>End-to-end behaviour of each command through the real executable.</summary>
public sealed class CommandTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Create_writes_text_and_logs_the_byte_count()
    {
        var result = Tool.Run(_dir.Path, "create", "hello.txt", "--text", "hello");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", File.ReadAllText(_dir.File("hello.txt")));
        Assert.Equal(5, result.Log(LogEvents.FileCreated).GetProperty("State").GetProperty("Bytes").GetInt64());
    }

    [Fact]
    public void Create_without_text_makes_empty_file()
    {
        var result = Tool.Run(_dir.Path, "create", "empty");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, new FileInfo(_dir.File("empty")).Length);
    }

    [Fact]
    public void Create_refuses_existing_file_unless_forced()
    {
        _dir.WriteFile("x", "keep");

        var refused = Tool.Run(_dir.Path, "create", "x", "--text", "new");
        var forced = Tool.Run(_dir.Path, "create", "x", "--text", "new", "--force");

        Assert.Equal((int)ExitCode.AlreadyExists, refused.ExitCode);
        Assert.Contains("use --force", Assert.Single(refused.Messages), StringComparison.Ordinal);
        Assert.Equal(0, forced.ExitCode);
        Assert.Equal("new", File.ReadAllText(_dir.File("x")));
    }

    [Fact]
    public void Copy_preserves_content_of_multi_megabyte_file()
    {
        var content = RandomNumberGenerator.GetBytes(4 * 1024 * 1024 + 3);
        _dir.WriteFile("src.bin", content);

        var result = Tool.Run(_dir.Path, "copy", "src.bin", "dst.bin");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(SHA256.HashData(content), SHA256.HashData(File.ReadAllBytes(_dir.File("dst.bin"))));
        Assert.Equal(content.Length, result.Log(LogEvents.FileCopied).GetProperty("State").GetProperty("Bytes").GetInt64());
    }

    [Fact]
    public void Copy_missing_source_exits_NotFound()
    {
        var result = Tool.Run(_dir.Path, "copy", "missing", "dst");

        Assert.Equal((int)ExitCode.NotFound, result.ExitCode);
        Assert.Equal("fileops: file not found: missing", Assert.Single(result.Messages));
        Assert.False(File.Exists(_dir.File("dst")));
    }

    [Fact]
    public void Copy_onto_itself_is_a_usage_error()
    {
        _dir.WriteFile("same", "data");

        var result = Tool.Run(_dir.Path, "copy", "same", "./same", "--force");

        Assert.Equal((int)ExitCode.UsageError, result.ExitCode);
        Assert.Equal("data", File.ReadAllText(_dir.File("same")));
    }

    [Fact]
    public void Combine_concatenates_in_order()
    {
        _dir.WriteFile("a", "foo");
        _dir.WriteFile("b", "bar");

        var result = Tool.Run(_dir.Path, "combine", "a", "b", "out");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("foobar", File.ReadAllText(_dir.File("out")));
        Assert.Equal(6, result.Log(LogEvents.FilesCombined).GetProperty("State").GetProperty("Bytes").GetInt64());
    }

    [Fact]
    public void Combine_can_append_in_place_with_force()
    {
        _dir.WriteFile("log", "line1\n");
        _dir.WriteFile("more", "line2\n");

        var result = Tool.Run(_dir.Path, "combine", "log", "more", "log", "--force");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("line1\nline2\n", File.ReadAllText(_dir.File("log")));
    }

    [Fact]
    public void Combine_refuses_to_overwrite_without_force()
    {
        _dir.WriteFile("a", "1");
        _dir.WriteFile("b", "2");
        _dir.WriteFile("out", "keep");

        var result = Tool.Run(_dir.Path, "combine", "a", "b", "out");

        Assert.Equal((int)ExitCode.AlreadyExists, result.ExitCode);
        Assert.Equal("keep", File.ReadAllText(_dir.File("out")));
    }

    [Fact]
    public void Delete_removes_file()
    {
        _dir.WriteFile("gone", "xyz");

        var result = Tool.Run(_dir.Path, "delete", "gone");

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(_dir.File("gone")));
        Assert.Equal(3, result.Log(LogEvents.FileDeleted).GetProperty("State").GetProperty("Bytes").GetInt64());
    }

    [Fact]
    public void Delete_missing_fails_unless_forced()
    {
        var strict = Tool.Run(_dir.Path, "delete", "missing");
        var forced = Tool.Run(_dir.Path, "delete", "missing", "-f");

        Assert.Equal((int)ExitCode.NotFound, strict.ExitCode);
        Assert.Equal(0, forced.ExitCode);
        Assert.Equal("Warning", forced.Log(LogEvents.DeleteSkippedMissing).GetProperty("LogLevel").GetString());
    }

    [Fact]
    public void Delete_refuses_directory()
    {
        Directory.CreateDirectory(_dir.File("sub"));

        var result = Tool.Run(_dir.Path, "delete", "sub");

        Assert.Equal((int)ExitCode.UsageError, result.ExitCode);
        Assert.True(Directory.Exists(_dir.File("sub")));
    }

    [Fact]
    public void No_temporary_files_remain_after_any_sequence()
    {
        _dir.WriteFile("a", "a");
        Tool.Run(_dir.Path, "create", "b", "-t", "b");
        Tool.Run(_dir.Path, "copy", "a", "c");
        Tool.Run(_dir.Path, "copy", "a", "c");
        Tool.Run(_dir.Path, "combine", "a", "b", "d");
        Tool.Run(_dir.Path, "combine", "a", "missing", "e");
        Tool.Run(_dir.Path, "delete", "d");

        Assert.Empty(_dir.LeftoverTempFiles());
        Assert.False(File.Exists(_dir.File("e")));
    }
}
