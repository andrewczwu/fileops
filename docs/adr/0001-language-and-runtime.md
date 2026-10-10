# ADR 0001: C# on .NET 10, published as a NativeAOT binary

**Status:** accepted · **Date:** 2026-10-10

## Context

The deliverable is a small file tool that must ship as a single portable executable installable on
Debian, with the evaluation focused on CI/CD practice, version control and implementation quality
rather than language. Candidates considered: C++/CMake, Go, Rust, C#.

## Decision

C# 14 on .NET 10, compiled with NativeAOT to a self-contained `linux-x64` ELF binary.

## Reasons

- `System.IO` and `System.Diagnostics` cover file operations, tracing and metrics with no third
  party code; the only runtime packages are
  Microsoft.Extensions.{DependencyInjection,Diagnostics,Logging,Logging.Console}, System.CommandLine
  and OpenTelemetry (SDK and OTLP exporter).
- NativeAOT yields a ~10 MB binary that depends only on glibc, starts in milliseconds and needs no
  runtime on the target, satisfying "single portable executable" without static-linking debates.
- First-class testing (`xunit`, `FakeLogger`, `MetricCollector`) makes the telemetry-as-evidence
  approach cheap.
- Trim/AOT analysis with warnings-as-errors turns "does this dependency still work AOT?" into a
  build-time check.

## Consequences

- Building the Linux binary requires a Linux toolchain (`clang`, `zlib1g-dev`); this is provided by
  the pinned Docker image and is the reason local builds on macOS go through `make docker-*`.
- The binary links against the build machine's glibc. Microsoft publishes no Debian-based SDK image
  for .NET 10, so the toolchain image is the Ubuntu 24.04 `-aot` variant (glibc 2.39) and the install
  smoke test on Debian 12/13 and Ubuntu 22.04/24.04 is what proves the binary runs on older glibc.
- The binary bundles the .NET runtime but links glibc dynamically, so the `.deb` declares
  `Depends: libc6 (>= <floor>)` with the floor read from the binary's versioned symbols at package
  time (`scripts/package.sh`), and lintian runs at warning level with no overrides.
