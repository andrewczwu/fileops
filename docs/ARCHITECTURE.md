# Architecture

A small tool, structured so that it could grow: the file logic, the command-line surface, the
telemetry and the delivery pipeline are separate concerns with explicit contracts between them.

```text
                      ┌──────────────────────────────┐
 argv ──► FileOps.Cli │ System.CommandLine parse     │
                      │ GlobalOptions (log level/fmt)│
                      │ AppHost: DI + logging + OTLP │
                      │ CommandRunner: exceptions →  │──► exit code + "fileops: …" on stderr
                      │   exit codes                 │
                      └──────────────┬───────────────┘
                                     │ FileOperations.Create/Copy/Combine/Delete
                      ┌──────────────▼───────────────┐
                      │ FileOps.Core                 │
                      │  FileOperations  (Execute(): │──► span + metrics + log events
                      │    span, timing, error map)  │
                      │  AtomicFile      (tmp+rename)│──► files on disk
                      │  Telemetry/*     (contract)  │
                      └──────────────────────────────┘
```

## FileOps.Core

- **`FileOperations`** is the only public entry point. Each operation validates inputs, then does
  its work inside `Execute()`, which owns the cross-cutting behaviour: start a span, time the call,
  log start/complete/fail with stable event ids, record metrics, and translate raw
  `IOException`/`UnauthorizedAccessException` into `FileOpsException` with the right `ExitCode`.
  Adding a fifth operation means adding one method and one `FileOperation` enum member.
- **`AtomicFile`** writes to `.<name>.<guid>.tmp` in the destination directory and renames it into
  place. Rename is atomic on POSIX within one file system, which is why the temp file must live
  beside the destination. The "already exists" check is done twice: up front for a clean message,
  and again by `File.Move(overwrite: false)` to close the race.
- **`FileOpsException`** carries an `ExitCode` and a message safe to print verbatim. The CLI never
  inspects exception types beyond this one.
- **`Telemetry/`** holds the contract: `LogEvents` ids, source-generated `Log` methods,
  `FileOpsActivitySource` tag names and `FileOpsMetrics` instruments. See
  [OBSERVABILITY.md](OBSERVABILITY.md).

The library depends only on `Microsoft.Extensions.Logging.Abstractions`; tracing and metrics use
the in-box `System.Diagnostics` APIs, so any host (this CLI, a service, a test) can attach
whatever exporters it wants.

## FileOps.Cli

- **`CliApp`** builds the `RootCommand`; each command lives in its own file under `Commands/` and
  is a thin mapping from parsed arguments to one `FileOperations` call.
- **`CommandRunner`** is the single execution path: build an `AppHost`, run, map the outcome.
  The host is disposed before the summary line is written so log records always come first.
- **`AppHost`** wires `ServiceCollection` with console logging (stderr only, text or JSON),
  `IMeterFactory`, and, when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, OpenTelemetry tracer, meter
  and log providers. System.CommandLine 2.0 has no hosting layer, so this is deliberately small.
- Published as a **NativeAOT** single binary: startup is milliseconds, there is no runtime to
  install, and `TreatWarningsAsErrors` on trim/AOT analysis means any package that stops being
  AOT-safe fails the build instead of the user.

## Tests

| Project | Runs against | Proves |
| --- | --- | --- |
| `FileOps.Core.Tests` | the library in-process | behaviour of each operation, atomicity, error mapping, and the telemetry emitted for every outcome |
| `FileOps.Integration.Tests` | the real executable (managed build by default, native binary via `FILEOPS_BIN`) | argument parsing, exit codes, stdout/stderr discipline, JSON log schema, and that logged facts match reality |

The same integration suite runs three times in CI: against the managed build, against the
NativeAOT binary, and (as a shell script) against the installed `.deb` in clean containers.

## Delivery

```text
docker/Dockerfile.build ─► ghcr.io/…/fileops-build   (weekly + on change)
                                      │
   PR / push ─► ci.yml:  format · secrets · lint · build (test → publish → native tests → .deb → lintian) ─► smoke×4
                                      │
   tag vX.Y.Z ─► release.yml: build ─► verify×4 ─► attest + GitHub release (.deb, .tar.gz, SHA256SUMS)
```

Every multi-step CI task is a script under `scripts/` invoked through the `Makefile`, so
`make test`, `make package`, `make smoke` locally are the same commands the pipeline runs, and
`act` can execute the workflows offline against the same toolchain image.

### Pipeline timing

The job graph is shaped by one measurement: pulling the 1 GB toolchain image costs ~25 s per
container job on a hosted runner, and the work inside each job is smaller than that. The first
layout had three container jobs in a chain (`test → package → lintian`), so two image pulls, an
artifact round-trip and an `apt-get install lintian` sat on the critical path.

| Layout | Critical path | Wall-clock |
| --- | --- | --- |
| `test → package → lintian` (three container jobs) | 53 s + 60 s + 39 s | 2 m 40 s |
| `build` (one container job) `→ smoke` | 82 s + 15 s | 1 m 42 s |

Inside the consolidated `build` job: image pull 19 s, build + tests 21 s, NativeAOT publish 22 s,
native-binary tests 3 s, `.deb` 2 s, lintian 3 s (it took 19 s on the host runner plus a 15 s
`apt-get install`; running it from the image is both cheaper and version-pinned).

Rules that follow from it: anything needing the toolchain image runs in the single `build` job;
`format` uses the SDK preinstalled on the runner (the same 10.0.401 the image pins) rather than the
image; `lintian` ships in the image instead of being installed per run. What remains on the
critical path is real work — NativeAOT compilation and the test suites — plus one image pull.
Further gains would need a smaller base image or caching the AOT object files, both of which cost
more in maintenance than the seconds they save at this size.

## Reproducible restores

`packages.lock.json` files are committed and CI restores with `--locked-mode`, so the dependency
graph that was tested is the one that ships. Two SDK behaviours would otherwise make the lock files
machine-specific, and both are neutralised in the project files:

- `PublishAot` adds implicit `Microsoft.DotNet.ILCompiler` / `Microsoft.NET.ILLink.Tasks` references
  at whatever version the installed SDK bundles. `Directory.Build.targets` pins them by updating the
  SDK's `KnownILCompilerPack`/`KnownILLinkPack` items to `NativeAotToolchainVersion`
  (`Directory.Build.props`); bump it together with the SDK image.
- With `PublishAot` and no `RuntimeIdentifier`, the SDK sets `PublishRuntimeIdentifier` to the
  *host* RID and adds it to the restore graph. `FileOps.Cli.csproj` sets it to `linux-x64`, the
  only RID we ship.
- `dotnet publish -r linux-x64` flows that RID into every referenced project's restore, so
  `FileOps.Core.csproj` declares `RuntimeIdentifiers=linux-x64` and its lock file carries the RID
  section up front.

The `act` run of the CI job is what caught both; see ADR 0004 for why the pipeline is runnable locally.

## Versioning

The git tag is the single source of truth. `release.yml` derives `VERSION` from `vX.Y.Z`, refuses
tags without a matching `CHANGELOG.md` section, and passes the version to MSBuild and nfpm.
Local and CI builds use `0.0.0-dev` / `0.0.0-ci.N` so they always sort below a real release.
