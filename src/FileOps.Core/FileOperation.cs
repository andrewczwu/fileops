namespace FileOps.Core;

/// <summary>The commands the tool supports. Used as a structured field in logs, metrics and spans.</summary>
public enum FileOperation
{
    /// <summary>Create a new file, optionally with initial text.</summary>
    Create,

    /// <summary>Copy one file to a new path.</summary>
    Copy,

    /// <summary>Concatenate two files into a third.</summary>
    Combine,

    /// <summary>Delete a file.</summary>
    Delete,
}
