using System.CommandLine;

using FileOps.Cli.Hosting;
using FileOps.Core;

namespace FileOps.Cli.Commands;

/// <summary>
/// Shared execution path for every command: build the host, run the operation, translate the
/// outcome into a user-facing message on stderr and a process exit code.
/// </summary>
internal static class CommandRunner
{
    /// <summary>Runs <paramref name="operation"/> and returns the exit code the process should use.</summary>
    public static int Run(ParseResult parseResult, GlobalOptions options, Func<FileOperations, OperationResult> operation)
    {
        var settings = TelemetrySettings.From(parseResult, options);
        var stderr = parseResult.InvocationConfiguration.Error;

        ExitCode exitCode;
        string? message = null;

        using (var host = AppHost.Create(settings))
        {
            try
            {
                operation(host.Operations);
                exitCode = ExitCode.Success;
            }
            catch (FileOpsException ex)
            {
                exitCode = ex.ExitCode;
                message = ex.Message;
            }
#pragma warning disable CA1031 // Last line of defence: anything else is an internal error, reported and mapped to a distinct exit code.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                exitCode = ExitCode.InternalError;
                message = $"unexpected error: {ex.Message}";
            }
        }

        // Written after the host is disposed so log records always precede the summary line.
        if (message is not null)
        {
            stderr.WriteLine($"fileops: {message}");
        }

        return (int)exitCode;
    }
}
