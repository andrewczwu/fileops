using System.CommandLine;

namespace FileOps.Cli.Commands;

/// <summary><c>fileops combine &lt;first&gt; &lt;second&gt; &lt;output&gt; [--force]</c></summary>
internal static class CombineCommand
{
    /// <summary>Builds the command.</summary>
    public static Command Build(GlobalOptions global)
    {
        var first = new Argument<string>("first") { Description = "First input file." };
        var second = new Argument<string>("second") { Description = "Second input file, appended after the first." };
        var output = new Argument<string>("output") { Description = "File to write. May be one of the inputs when --force is given." };
        var force = new Option<bool>("--force", "-f") { Description = "Overwrite the output if it already exists." };

        var command = new Command("combine", "Concatenate two files into a third.") { first, second, output, force };
        command.SetAction(parseResult => CommandRunner.Run(parseResult, global, ops =>
            ops.Combine(parseResult.GetValue(first)!, parseResult.GetValue(second)!, parseResult.GetValue(output)!, parseResult.GetValue(force))));
        return command;
    }
}
