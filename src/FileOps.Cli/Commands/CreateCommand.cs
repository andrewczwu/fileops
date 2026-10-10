using System.CommandLine;

namespace FileOps.Cli.Commands;

/// <summary><c>fileops create &lt;path&gt; [--text ...] [--force]</c></summary>
internal static class CreateCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(GlobalOptions global)
    {
        var path = new Argument<string>("path") { Description = "File to create." };
        var text = new Option<string?>("--text", "-t") { Description = "Initial content, written as UTF-8 exactly as given (no trailing newline is added)." };
        var force = new Option<bool>("--force", "-f") { Description = "Overwrite the file if it already exists." };

        var command = new Command("create", "Create a file, empty or with the given text.") { path, text, force };
        command.SetAction(parseResult => CommandRunner.Run(parseResult, global, ops =>
            ops.Create(parseResult.GetValue(path)!, parseResult.GetValue(text), parseResult.GetValue(force))));
        return command;
    }
}
