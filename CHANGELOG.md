# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org). The release workflow refuses to publish a tag that
has no matching section below.

## [Unreleased]

## [1.0.0] - 2026-10-10

### Added

- `create`, `copy`, `combine` and `delete` commands with safe-by-default overwrite handling (`--force`).
- Atomic writes: output goes to a temporary sibling file and is renamed into place, so an interrupted
  run never leaves a partial file. `combine` can therefore append in place (`combine a b a --force`).
- Stable exit codes: 0 success, 1 usage, 2 not found, 3 already exists, 4 I/O error, 70 internal.
- Structured logging (`--log-level`, `--log-format text|json`, `FILEOPS_LOG_*` env) to stderr with
  stable event ids; stdout is reserved for results.
- OpenTelemetry traces, metrics and logs exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- Single native executable (NativeAOT, linux-x64) packaged as a `.deb` with a man page.
- CI: format/analyzers, secret scanning, workflow linting, unit + integration tests with coverage and
  telemetry evidence in the job summary, native-binary verification, lintian, and install smoke tests
  on Debian 12/13 and Ubuntu 22.04/24.04.
- Release workflow: tag-driven, with SLSA build provenance attestations and checksums.

[Unreleased]: https://github.com/andrewczwu/fileops/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/andrewczwu/fileops/releases/tag/v1.0.0
