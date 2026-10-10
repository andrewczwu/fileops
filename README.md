# fileops

[![CI](https://github.com/andrewczwu/fileops/actions/workflows/ci.yml/badge.svg)](https://github.com/andrewczwu/fileops/actions/workflows/ci.yml)
[![Release](https://github.com/andrewczwu/fileops/actions/workflows/release.yml/badge.svg)](https://github.com/andrewczwu/fileops/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A command-line tool that creates, copies, combines and deletes files — with atomic writes,
safe-by-default overwrite handling, stable exit codes, and built-in structured logging, tracing
and metrics that the test suite and CI treat as evidence.

Ships as a single native Linux executable (no runtime to install), packaged as a `.deb`.

```text
$ fileops create notes.txt --text "hello"
$ fileops copy notes.txt backup.txt
$ fileops combine notes.txt backup.txt both.txt
$ fileops delete backup.txt
$ fileops copy notes.txt both.txt
fileops: file already exists: both.txt (use --force to overwrite)
$ echo $?
3
```

## Install

Download the `.deb` from the [latest release](https://github.com/andrewczwu/fileops/releases/latest):

```sh
VERSION=1.0.0   # see the releases page for the current version
curl -LO "https://github.com/andrewczwu/fileops/releases/download/v${VERSION}/fileops_${VERSION}_amd64.deb"
sudo apt install "./fileops_${VERSION}_amd64.deb"
fileops --version
man fileops
```

Works on Debian 12 and 13 and Ubuntu 22.04 and 24.04 (x86-64). The package's only dependency is
`libc6`; the minimum version is read from the binary at build time (currently `>= 2.34`).

Every release also ships `fileops_<version>_linux-x64.tar.gz` (the binary, `LICENSE` and the man
page under `fileops-<version>/`), a `SHA256SUMS` file, and a
[SLSA build provenance attestation](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations)
covering all three. Verify that an asset was built by this repository's release workflow, not
merely signed by someone with access to the repository:

```sh
gh attestation verify "fileops_${VERSION}_amd64.deb" --owner andrewczwu \
  --signer-workflow andrewczwu/fileops/.github/workflows/release.yml
```

Remove with `sudo apt remove fileops`.

## Usage

```text
fileops [--log-level LEVEL] [--log-format text|json] [-v] <command> [args]

  create  <path> [--text TEXT] [--force]      Create a file, empty or with text (UTF-8, as given)
  copy    <source> <destination> [--force]    Copy a file, preserving permissions
  combine <first> <second> <output> [--force] Concatenate two files into a third
  delete  <path> [--force]                    Delete a file (--force: ok if missing, like rm -f)
```

Rules that hold for every command:

- An existing destination is **never overwritten** unless `--force` is given; when it is, the
  replacement keeps the existing file's permission bits.
- Writes are **atomic**: content goes to a temporary file beside the destination and is renamed
  into place, so the destination is never left partially written. A consequence:
  `combine a b a --force` safely appends `b` to `a`. (If the process is killed by a signal mid-write,
  a `.name.<id>.tmp` sibling can remain; the destination itself is untouched.)
- `copy` preserves the source's permission bits, applied before any content is written.
- **stdout is reserved for results** (only `--help` and `--version` use it); diagnostics go to stderr.
- Directories are refused; copying a file onto itself is refused.

| Exit code | Meaning |
| ---: | --- |
| 0 | success |
| 1 | usage error (bad arguments, same source and destination, path is a directory) |
| 2 | an input file or directory does not exist |
| 3 | destination exists and `--force` was not given |
| 4 | the file system refused (permissions, disk full) |
| 70 | internal error |

## Telemetry

Logging is off by default. Turn it on per run or via environment variables:

```sh
$ fileops -v copy a b                       # human-readable, Information level
16:48:15.060 info: FileOps.Core.FileOperations[3001] Copied a to b (1 bytes)
16:48:15.067 info: FileOps.Core.FileOperations[1001] Copy completed on b: 1 bytes in 22.54 ms

$ FILEOPS_LOG_FORMAT=json FILEOPS_LOG_LEVEL=Debug fileops copy a b 2>run.ndjson
$ jq -c '{EventId, LogLevel, State}' run.ndjson
{"EventId":1000,"LogLevel":"Debug","State":{"Operation":"Copy","Path":"b",...}}
{"EventId":3001,"LogLevel":"Information","State":{"Source":"a","Destination":"b","Bytes":1,...}}
{"EventId":1001,"LogLevel":"Information","State":{"Operation":"Copy","Path":"b","Bytes":1,"DurationMs":11.07,...}}
```

Every operation also produces one OpenTelemetry span and three metrics. Set
`OTEL_EXPORTER_OTLP_ENDPOINT` and they are exported alongside the logs. Event ids, span tags and
instruments are a documented contract: [docs/OBSERVABILITY.md](docs/OBSERVABILITY.md).

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Docker is needed for the Linux
native binary and the package (the AOT toolchain is Linux-only), and for the install smoke test.

```sh
make test                 # restore, build, unit + integration tests (any OS)
make docker-test          # the same, inside the pinned CI toolchain image
make docker-package       # NativeAOT publish + .deb  → artifacts/deb/
make smoke                # install the .deb in clean Debian/Ubuntu containers and exercise it
make lint-deb             # lintian
make help                 # everything else
```

`make build-image` builds the toolchain image locally if you do not want to pull it from GHCR.

## Testing

Three layers, all run by `make test` / CI:

| Layer | What runs | What it proves |
| --- | --- | --- |
| Unit (`tests/FileOps.Core.Tests`) | the library in-process with a `FakeLogger`, `ActivityListener` and `MetricCollector` attached | each operation's file-system behaviour, atomicity, error→exit-code mapping, **and** that the right log events, span status/tags and metric measurements were emitted |
| Integration (`tests/FileOps.Integration.Tests`) | the real executable as a child process, with JSON logging on | argument parsing, exit codes, stdout/stderr discipline, log schema, and that logged facts (bytes, exit code, reason) match what actually happened |
| Package smoke (`scripts/smoke-test.sh`) | the installed `.deb` in pristine `debian:bookworm-slim` (12), `debian:trixie-slim` (13), `ubuntu:22.04`, `ubuntu:24.04` containers | it installs, runs, honours exit codes, logs, ships a man page, and removes cleanly |

CI runs the integration suite twice more: against the NativeAOT binary (`make test-native`) and,
via the smoke script, against the installed package — so the artefact that ships is the artefact
that was tested. Every CLI invocation's log stream is captured to `cli-runs.ndjson`, summarised
into the job summary, and uploaded; a red build can be diagnosed from that file without a re-run.
The `diagnose` job in `ci.yml` hands that file to an AI assistant on failing same-repository pull
requests; it is dormant until an `ANTHROPIC_API_KEY` repository secret exists.

## Pipeline

```text
PR / push to main ──► ci.yml
   format        dotnet format + analyzers (warnings are errors), host SDK   ─┐
   secrets       gitleaks over full history                                   ├─ parallel
   lint          shellcheck, shfmt, hadolint, yamllint, markdownlint,        ─┘
                 editorconfig-checker, actionlint, zizmor, Conventional Commits
   build         one toolchain-container job: unit + integration tests (coverage + telemetry
                 → job summary) → NativeAOT publish → integration tests vs native binary
                 → .deb → lintian
   smoke ×4      apt install in clean containers, exercise, remove
   diagnose      (on failure, same-repo PRs, dormant without a secret) AI summary from the telemetry

tag vX.Y.Z ──► release.yml
   build → verify ×4 → provenance attestation → GitHub release (.deb, .tar.gz, SHA256SUMS)
   (a pre-release suffix such as v1.2.0-rc.1 publishes a GitHub pre-release)

docker/Dockerfile.build ──► build-image.yml (on change + weekly) ──► ghcr.io/andrewczwu/fileops-build
```

Design choices worth knowing (details in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and
[docs/adr](docs/adr)):

- **One toolchain for everything that builds.** Test, publish, package and lintian run in a pinned
  container image as an unprivileged user; `make docker-test` runs the identical thing locally. The
  formatter and the linters run on the host because they need no toolchain.
- **Scripts, not YAML.** Every multi-step job calls a script under `scripts/` through the
  Makefile, so pipeline logic is reviewable and runnable without pushing.
- **Runnable offline.** `act -j build` executes the real workflow locally (`.actrc` has the defaults).
- **Supply-chain hygiene.** Actions pinned to commit SHAs with Dependabot updates, least-privilege
  `permissions`, `timeout-minutes` and `concurrency` on every job, locked NuGet restores with
  `NuGetAudit`, secret scanning in a pre-commit hook and in CI, SLSA provenance on releases.
- **Version from the tag.** `release.yml` derives the version from `vX.Y.Z` and refuses to run if
  `CHANGELOG.md` has no matching section.

## Releasing

```sh
# 1. add a "## [1.2.3] - YYYY-MM-DD" section to CHANGELOG.md and merge it
# 2.
git tag -a v1.2.3 -m "fileops 1.2.3" && git push origin v1.2.3
```

The release workflow does the rest. A tag with a pre-release suffix (`v1.2.3-rc.1`) publishes a
GitHub pre-release and may reuse the `[1.2.3]` changelog section, which is how the release path is
exercised before a final version. CI builds use `0.0.0-ci.N`; local builds use `0.0.0-dev`.

## Non-goals

Choices made on purpose for a single-maintainer tool with one requested target; the reasoning is in
[docs/reviews/2026-10-ci-cd-review.md](docs/reviews/2026-10-ci-cd-review.md), section 5:

- no apt repository, RPM, Homebrew tap, container image or `curl | sh` installer — `apt install ./file.deb`
  from a verified release is the supported path;
- no arm64 or musl builds — x86-64 Debian/Ubuntu is the brief;
- no merge queue, reusable workflows, SLSA Build L3 builder or SBOM — each adds machinery without a
  consumer here; provenance attestations give Build L2 today;
- no coverage gate, mutation testing or fuzzing — 84 deterministic tests on a tool that parses only
  its own arguments.

## Repository layout

```text
src/FileOps.Core/        operations, atomic writer, telemetry contract (no CLI code)
src/FileOps.Cli/         System.CommandLine front end, host wiring, exit codes
tests/                   unit and integration tests
packaging/               nfpm spec, man page, Debian copyright
scripts/                 build, publish, package, smoke-test, CI helpers
docker/                  toolchain image
docs/                    architecture, observability contract, decision records
.github/                 workflows, Dependabot, CODEOWNERS, PR template
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow and
[SECURITY.md](SECURITY.md) for the security posture.
