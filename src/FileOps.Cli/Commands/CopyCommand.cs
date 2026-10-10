using System.CommandLine;

namespace FileOps.Cli.Commands;

/// <summary><c>fileops copy &lt;source&gt; &lt;destination&gt; [--force]</c></summary>
internal static class CopyCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(GlobalOptions global)
    {
        var source = new Argument<string>("source") { Description = "Existing file to copy." };
        var destination = new Argument<string>("destination") { Description = "Path of the new copy." };
        var force = new Option<bool>("--force", "-f") { Description = "Overwrite the destination if it already exists." };

        var command = new Command("copy", "Copy a file to a new path, preserving permissions.") { source, destination, force };
        command.SetAction(parseResult => CommandRunner.Run(parseResult, global, ops =>
            ops.Copy(parseResult.GetValue(source)!, parseResult.GetValue(destination)!, parseResult.GetValue(force))));
        return command;
    }
}
