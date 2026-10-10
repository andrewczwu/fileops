using FileOps.Core.Tests.Support;

namespace FileOps.Core.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Leaves_no_temp_file_and_no_destination_when_writer_throws()
    {
        var destination = _dir.File("out");

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFile.Write(destination, overwrite: false, _ => throw new InvalidOperationException("boom")));

        Assert.False(File.Exists(destination));
        Assert.Empty(_dir.LeftoverTempFiles());
    }

    [Fact]
    public void Keeps_original_content_when_writer_throws_during_overwrite()
    {
        var destination = _dir.WriteFile("out", "original");

        Assert.Throws<InvalidOperationException>(() =>
            AtomicFile.Write(destination, overwrite: true, stream =>
            {
                stream.Write("partial"u8);
                throw new InvalidOperationException("boom");
            }));

        Assert.Equal("original", File.ReadAllText(destination));
        Assert.Empty(_dir.LeftoverTempFiles());
    }

    [Fact]
    public void Returns_bytes_written()
    {
        var bytes = AtomicFile.Write(_dir.File("out"), overwrite: false, stream => stream.Write(new byte[1234]));

        Assert.Equal(1234, bytes);
    }

    [Fact]
    public void Temp_file_is_created_in_destination_directory()
    {
        // Rename is only atomic within one file system, so the temp file must live beside the destination.
        string? observedTemp = null;

        AtomicFile.Write(_dir.File("out"), overwrite: false, _ => observedTemp = Directory.EnumerateFiles(_dir.Path, ".*.tmp").SingleOrDefault());

        Assert.NotNull(observedTemp);
        Assert.Equal(_dir.Path, Path.GetDirectoryName(observedTemp));
    }
}
