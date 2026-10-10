using System.CommandLine;

using FileOps.Cli.Commands;

namespace FileOps.Cli;

/// <summary>Assembles the command tree. Kept separate from <c>Main</c> so tests can parse and invoke without a process.</summary>
internal static class CliApp
{
    /// <summary>Builds the root command with all subcommands and global options attached.</summary>
    public static RootCommand Build()
    {
        var global = new GlobalOptions();

        var root = new RootCommand("Create, copy, combine and delete files. Writes are atomic; every run emits structured logs, traces and metrics.");
        foreach (var option in global.All)
        {
            root.Options.Add(option);
        }

        root.Subcommands.Add(CreateCommand.Build(global));
        root.Subcommands.Add(CopyCommand.Build(global));
        root.Subcommands.Add(CombineCommand.Build(global));
        root.Subcommands.Add(DeleteCommand.Build(global));

        return root;
    }
}
