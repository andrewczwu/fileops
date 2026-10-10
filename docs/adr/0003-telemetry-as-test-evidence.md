# ADR 0003: Telemetry is part of the contract and tests assert on it

**Status:** accepted · **Date:** 2026-10-10

## Context

Logs are usually added for operators and ignored by tests. That leaves two gaps: the logs drift
from reality without anyone noticing, and when CI fails the first step is always "re-run with more
logging". Both are expensive in a pipeline whose job is developer velocity.

## Decision

- Every operation emits a fixed set of structured log events with stable ids, one span with a
  fixed tag set, and three metrics. The set is documented in `docs/OBSERVABILITY.md`.
- Unit tests capture logs (`FakeLogger`), spans (`ActivityListener`) and metrics
  (`MetricCollector<T>`) and assert on them next to the file-system assertions.
- Integration tests run the real binary with JSON logging and verify that the logged exit code,
  byte counts and event order agree with what the process returned and what is on disk.
- CI captures every invocation's log stream to `cli-runs.ndjson`, renders a summary into the job
  summary, uploads it, and (optionally) feeds it to an AI assistant for failure diagnosis.

## Reasons

- A log line that a test depends on cannot silently rot.
- A red build already contains the evidence needed to explain it; no re-run is required.
- Machine-readable, schema-stable output is what makes automated or AI-assisted triage reliable:
  the assistant is given event ids and field names, not free text to guess at.

## Consequences

- Changing a message template or event id is a breaking change for tests and must be deliberate.
- The harness (`ObservedOperations`) has to isolate process-global `ActivityListener`s per test;
  spans are filtered by a per-test root activity.
- Logging must never touch stdout, so `LogToStandardErrorThreshold` is pinned to `Trace`.
