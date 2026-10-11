# Observability contract

`fileops` treats its own telemetry as part of the interface: tests assert on it, CI publishes it,
and a failure can be diagnosed from the log stream alone. This document is the contract.

## Channels

| Channel | Where | Enabled by |
| --- | --- | --- |
| Logs | stderr, text or NDJSON | `--log-level` / `FILEOPS_LOG_LEVEL` (default `None`), `--log-format` / `FILEOPS_LOG_FORMAT` |
| Logs, traces, metrics | OTLP | `OTEL_EXPORTER_OTLP_ENDPOINT` (standard `OTEL_*` variables honoured); logs at Information and above by default while the console stays silent, `--log-level` governs every sink |
| User-facing result | stderr, last line, `fileops: <message>` | always, on failure only |

**stdout is never used** except by `--help` and `--version`, so the tool is safe in pipelines. A usage
error prints one `fileops: <message>` line per error plus `fileops: run 'fileops --help' for usage` on
stderr and nothing on stdout.
Log records are flushed before the summary line and before the process exits.

## Log events

Every record carries a stable `EventId`. Ranges: 1xxx lifecycle, 2xxx create, 3xxx copy,
4xxx combine, 5xxx delete. Defined in `src/FileOps.Core/Telemetry/LogEvents.cs`.

| Id | Level | Message template | Structured fields |
| ---: | --- | --- | --- |
| 1000 | Debug | `Starting {Operation} on {Path}` | Operation, Path |
| 1001 | Information | `{Operation} completed on {Path}: {Bytes} bytes in {DurationMs} ms` | Operation, Path, Bytes, DurationMs |
| 1002 | Error | `{Operation} failed on {Path} with exit code {ExitCode}: {Reason}` | Operation, Path, ExitCode, Reason (+ exception) |
| 2001 | Information | `Created {Path} ({Bytes} bytes)` | Path, Bytes |
| 3001 | Information | `Copied {Source} to {Destination} ({Bytes} bytes)` | Source, Destination, Bytes |
| 4001 | Information | `Combined {First} and {Second} into {Output} ({Bytes} bytes)` | First, Second, Output, Bytes |
| 5001 | Information | `Deleted {Path} ({Bytes} bytes)` | Path, Bytes |
| 5002 | Warning | `Nothing to delete: {Path} does not exist` | Path |

Invariants (enforced by `TelemetryContractTests`):

- a successful operation emits `1000` first and `1001` last, with nothing at Warning or above, except
  `5002` for `delete --force` on a missing path;
- a failed operation emits `1002` with `ExitCode` (the `ExitCode` enum name, for example
  `NotFound` for exit code 2) and `Reason` (the summary line without its `fileops:` prefix), and never
  emits `1001`;
- the `--log-format json` record shape is `{Timestamp, EventId, LogLevel, Category, Message, State{...}, Scopes[]}`,
  one object per line.

## Traces

One span per operation from `ActivitySource` `FileOps`, named `fileops.<operation>`.

| Tag | Meaning |
| --- | --- |
| `fileops.operation` | `create` / `copy` / `combine` / `delete` |
| `file.path` | the file produced or removed |
| `file.source`, `file.source2`, `file.destination` | inputs and output for copy/combine |
| `file.size` | bytes written or removed |
| `fileops.overwrite` | whether `--force` was given (create, copy, combine) |
| `fileops.exit_code` | numeric exit code (0 on success) |
| `error.type` | exception type on failure |

Status is `Ok` on success, `Error` with the user-facing message as description on failure.
When a span is active, log records include `TraceId` and `SpanId` scopes for correlation.

## Metrics

Meter `FileOps`:

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `fileops.operations` | counter | `{operation}` | `operation`, `outcome` (`success` or lower-case exit code name) |
| `fileops.operation.duration` | histogram | `s` | same |
| `fileops.bytes` | counter | `By` | same; only recorded when bytes > 0 |

## How tests use this

- **Unit tests** (`tests/FileOps.Core.Tests`) run each operation with a `FakeLogger`, an
  `ActivityListener` and `MetricCollector<T>` attached and assert on records, spans and
  measurements alongside the file-system outcome. `ObservedOperations` is the harness.
- **Integration tests** (`tests/FileOps.Integration.Tests`) launch the real executable with
  `FILEOPS_LOG_FORMAT=json`, parse stderr line by line, and check that the logged exit code,
  byte counts and event sequence match what the process actually returned and wrote.
  With `FILEOPS_TEST_LOG_DIR` set, every invocation is appended to `cli-runs.ndjson`.
- **OTLP export tests** (`OtlpExportTests`) point the real executable at an in-process OTLP/HTTP
  receiver and check that a span, the three metrics and Information-level log records arrive with
  `service.name` and `service.version`, that `--log-level None` stops log export but not spans or
  metrics, and that a failure is exported as an error span. Payloads are matched by string
  presence, not decoded, so no protobuf dependency is needed.
- **CI** renders `cli-runs.ndjson` into the job summary (`scripts/telemetry-summary.sh`) and
  uploads it as an artefact. On failure, the optional `diagnose` job hands the same file to an
  AI assistant with the event-id table above, so the first read of a red build already names
  the failing event and the likely cause.

## Shipping telemetry somewhere

```sh
docker run -d -p 4318:4318 otel/opentelemetry-collector     # or any OTLP endpoint
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318 OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf \
  fileops -v copy a b
```

Resource attributes: `service.name=fileops`, `service.version=<version>`.
