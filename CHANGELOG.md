# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org). The release workflow refuses to publish a tag that
has no matching section below.

## [Unreleased]

## [1.1.0] - 2026-10-10

### Fixed

- The `.deb` now declares its real dependency, `libc6 (>= <glibc floor read from the binary>)`,
  and lintian runs at warning level; the stale `statically-linked-binary` override is gone.
- Release tarballs contain the binary, `LICENSE` and the man page under `fileops-<version>/`;
  `SHA256SUMS` is itself covered by the provenance attestation; packaging timestamps come from
  the tagged commit (`SOURCE_DATE_EPOCH`).
- Pre-release tags (`v1.2.0-rc.1`) publish as GitHub pre-releases and may reuse the final
  version's changelog section. The `.deb` file is named with the SemVer version
  (`fileops_1.2.0-rc.1_amd64.deb`) because GitHub rewrites `~` in asset names, which broke the
  `SHA256SUMS` match in `v1.1.0-rc.1`; the package's own version field keeps the Debian `~` form.
- CI could not fail on a found secret (`make lint`), two smoke-test checks could never fail
  (`!` pipelines are exempt from `set -e`), a pull-request image build could cancel the `main`
  publish, and runs on `main` could be cancelled by the next push.
- An empty or whitespace-only path now exits 1 with a `1002` event (it exited 70).
- File names of 240 characters or more no longer fail with exit 4; the temporary name is truncated.
- `copy` creates its temporary file with the source's permission bits, so the copy is never briefly
  more readable than the original.
- Usage errors are reported on stderr as `fileops: <message>` lines and nothing is written to stdout
  (the help text used to be printed on stdout).
- With `OTEL_EXPORTER_OTLP_ENDPOINT` set and no `--log-level`, log records are exported at
  Information and above while the console stays silent; `--log-level` governs every sink.
- The CI secret scan now covers the full git history, not just the pushed commit.
- `make docker-*` runs as the unprivileged user, as CI does.

### Added

- Enforced style and formatting conventions: C# style rules promoted to build warnings, plus
  shellcheck, shfmt, hadolint, yamllint, markdownlint, editorconfig-checker and a Conventional
  Commits check, all mandatory in CI (`Lint` job) and run by the git hooks on staged files.
- Integration test proving `combine` is byte-transparent for binary input.
- A seven-day Dependabot cooldown on GitHub Actions, NuGet and Docker updates.
- Repository settings: a tag ruleset on `v*`, immutable releases and CodeQL (default setup).

### Changed

- CI critical path shortened: test, NativeAOT publish, native-binary tests, `.deb` build and
  lintian now run in one toolchain-container job (one image pull instead of three), the formatter
  runs on the runner's preinstalled SDK, and lintian ships in the toolchain image.
- GNU Make 3.82 or newer is required (macOS: `brew install make`).
- Documented that a run killed by a signal can leave a `.name.<id>.tmp` sibling; the destination
  itself is never partial.

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

[Unreleased]: https://github.com/andrewczwu/fileops/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/andrewczwu/fileops/releases/tag/v1.1.0
[1.0.0]: https://github.com/andrewczwu/fileops/releases/tag/v1.0.0
