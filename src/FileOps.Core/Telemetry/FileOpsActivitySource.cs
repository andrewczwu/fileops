using System.Diagnostics;
using System.Reflection;

namespace FileOps.Core.Telemetry;

/// <summary>
/// The single <see cref="ActivitySource"/> for the library and the tag names its spans use.
/// Tag names follow OpenTelemetry semantic conventions where one exists (<c>file.*</c>).
/// </summary>
public static class FileOpsActivitySource
{
    /// <summary>Name to pass to <c>AddSource</c> / <c>ActivityListener.ShouldListenTo</c>.</summary>
    public const string Name = "FileOps";

    /// <summary>The shared activity source. Spans are named <c>fileops.&lt;operation&gt;</c>.</summary>
    public static ActivitySource Source { get; } = new(
        Name,
        typeof(FileOpsActivitySource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");

    /// <summary>Tag keys attached to spans.</summary>
    public static class Tags
    {
        /// <summary>The <see cref="FileOperation"/> name in lower case.</summary>
        public const string Operation = "fileops.operation";

        /// <summary>The primary file the operation acted on.</summary>
        public const string Path = "file.path";

        /// <summary>Source file for copy/combine.</summary>
        public const string Source = "file.source";

        /// <summary>Second source file for combine.</summary>
        public const string SecondSource = "file.source2";

        /// <summary>Destination file for copy/combine.</summary>
        public const string Destination = "file.destination";

        /// <summary>Bytes written or removed.</summary>
        public const string Size = "file.size";

        /// <summary>Whether overwriting an existing destination was permitted.</summary>
        public const string Overwrite = "fileops.overwrite";

        /// <summary>The <see cref="ExitCode"/> the operation produced.</summary>
        public const string ExitCode = "fileops.exit_code";

        /// <summary>CLR type name of the exception on failure.</summary>
        public const string ErrorType = "error.type";
    }

    /// <summary>Builds the span name for an operation.</summary>
    public static string SpanName(FileOperation operation) =>
        $"fileops.{operation.ToString().ToLowerInvariant()}";
}
