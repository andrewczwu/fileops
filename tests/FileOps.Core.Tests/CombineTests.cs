using FileOps.Core.Telemetry;
using FileOps.Core.Tests.Support;

namespace FileOps.Core.Tests;

public sealed class CombineTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ObservedOperations _ops = new();

    public void Dispose()
    {
        _ops.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public void Concatenates_first_then_second_without_separator()
    {
        var first = _dir.WriteFile("a", "hello ");
        var second = _dir.WriteFile("b", "world");
        var output = _dir.File("out");

        var result = _ops.Sut.Combine(first, second, output);

        Assert.Equal("hello world", File.ReadAllText(output));
        Assert.Equal(11, result.Bytes);
    }

    [Fact]
    public void Handles_binary_content()
    {
        byte[] a = [0x00, 0xFF, 0x10];
        byte[] b = [0x0A, 0x0D];
        var output = _dir.File("out.bin");

        _ops.Sut.Combine(_dir.WriteFile("a.bin", a), _dir.WriteFile("b.bin", b), output);

        Assert.Equal(new byte[] { 0x00, 0xFF, 0x10, 0x0A, 0x0D }, File.ReadAllBytes(output));
    }

    [Fact]
    public void Can_append_in_place_when_output_is_an_input_and_forced()
    {
        var a = _dir.WriteFile("a", "one");
        var b = _dir.WriteFile("b", "two");

        _ops.Sut.Combine(a, b, a, overwrite: true);

        Assert.Equal("onetwo", File.ReadAllText(a));
        Assert.Equal("two", File.ReadAllText(b));
    }

    [Fact]
    public void Can_double_a_file_onto_itself()
    {
        var a = _dir.WriteFile("a", "ab");

        _ops.Sut.Combine(a, a, a, overwrite: true);

        Assert.Equal("abab", File.ReadAllText(a));
    }

    [Fact]
    public void Fails_with_NotFound_when_either_input_missing_and_writes_nothing()
    {
        var first = _dir.WriteFile("a", "x");
        var output = _dir.File("out");

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Combine(first, _dir.File("missing"), output));

        Assert.Equal(ExitCode.NotFound, ex.ExitCode);
        Assert.False(File.Exists(output));
        Assert.Empty(_dir.LeftoverTempFiles());
    }

    [Fact]
    public void Refuses_to_overwrite_existing_output_by_default()
    {
        var first = _dir.WriteFile("a", "x");
        var second = _dir.WriteFile("b", "y");
        var output = _dir.WriteFile("out", "keep");

        var ex = Assert.Throws<FileOpsException>(() => _ops.Sut.Combine(first, second, output));

        Assert.Equal(ExitCode.AlreadyExists, ex.ExitCode);
        Assert.Equal("keep", File.ReadAllText(output));
    }

    [Fact]
    public void Logs_all_three_paths_and_byte_count()
    {
        var first = _dir.WriteFile("a", "12");
        var second = _dir.WriteFile("b", "345");
        var output = _dir.File("out");

        _ops.Sut.Combine(first, second, output);

        var record = _ops.LogRecord(LogEvents.FilesCombined);
        Assert.Equal(first, record.GetStructuredStateValue("First"));
        Assert.Equal(second, record.GetStructuredStateValue("Second"));
        Assert.Equal(output, record.GetStructuredStateValue("Output"));
        Assert.Equal("5", record.GetStructuredStateValue("Bytes"));

        var span = _ops.Span(FileOperation.Combine);
        Assert.Equal(second, span.GetTagItem(FileOpsActivitySource.Tags.SecondSource));
        Assert.Equal("combine", _ops.OperationMeasurement().Tags["operation"]);
    }
}
