namespace FileOps.Core.Tests.Support;

/// <summary>A scratch directory that is removed when the test finishes.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = Directory.CreateTempSubdirectory("fileops-test-").FullName;
    }

    public string Path { get; }

    /// <summary>Returns an absolute path inside the directory without creating anything.</summary>
    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Creates a file with the given UTF-8 content and returns its path.</summary>
    public string WriteFile(string name, string content)
    {
        var path = File(name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Creates a file with the given bytes and returns its path.</summary>
    public string WriteFile(string name, byte[] content)
    {
        var path = File(name);
        System.IO.File.WriteAllBytes(path, content);
        return path;
    }

    /// <summary>Names of leftover temporary files from <c>AtomicFile</c>, which should always be empty.</summary>
    public IEnumerable<string> LeftoverTempFiles() =>
        Directory.EnumerateFiles(Path, ".*.tmp").Select(System.IO.Path.GetFileName)!;

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp cleaner will get it.
        }
        catch (UnauthorizedAccessException)
        {
            // A test may have removed permissions; same as above.
        }
    }
}
