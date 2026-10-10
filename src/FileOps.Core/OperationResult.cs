namespace FileOps.Core;

/// <summary>What an operation produced. Returned to the CLI for reporting and recorded in telemetry.</summary>
/// <param name="Operation">The operation that ran.</param>
/// <param name="Path">The file the operation produced or removed.</param>
/// <param name="Bytes">Bytes written (create/copy/combine) or removed (delete).</param>
public sealed record OperationResult(FileOperation Operation, string Path, long Bytes);
