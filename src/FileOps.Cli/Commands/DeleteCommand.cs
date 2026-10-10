using System.CommandLine;

namespace FileOps.Cli.Commands;

/// <summary><c>fileops delete &lt;path&gt; [--force]</c></summary>
internal static class DeleteCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(GlobalOptions global)
    {
        var path = new Argument<string>("path") { Description = "File to delete. Directories are refused." };
        var force = new Option<bool>("--force", "-f") { Description = "Succeed even if the file does not exist (like rm -f)." };

        var command = new Command("delete", "Delete a file.") { path, force };
        command.SetAction(parseResult => CommandRunner.Run(parseResult, global, ops =>
            ops.Delete(parseResult.GetValue(path)!, parseResult.GetValue(force))));
        return command;
    }
}
