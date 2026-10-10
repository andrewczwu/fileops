namespace FileOps.Cli;

/// <summary>Process entry point. All behaviour lives in <see cref="CliApp"/> so tests can drive it in-process.</summary>
internal static class Program
{
    private static int Main(string[] args) => CliApp.Build().Parse(args).Invoke();
}
