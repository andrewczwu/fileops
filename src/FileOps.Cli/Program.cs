using FileOps.Core;

namespace FileOps.Cli;

/// <summary>Process entry point. All behaviour lives in <see cref="CliApp"/> so tests can drive it in-process.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var parseResult = CliApp.Build().Parse(args);

        // Usage errors follow the same contract as every other failure: one `fileops: <message>` line
        // per problem on stderr, exit 1, nothing on stdout. (System.CommandLine's default would print
        // the full help text to stdout as well.)
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
            {
                // Some parser messages span lines ("must be one of:" lists); keep one message per line.
                var message = string.Join(' ', error.Message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                Console.Error.WriteLine($"fileops: {message}");
            }

            Console.Error.WriteLine("fileops: run 'fileops --help' for usage");
            return (int)ExitCode.UsageError;
        }

        return parseResult.Invoke();
    }
}
