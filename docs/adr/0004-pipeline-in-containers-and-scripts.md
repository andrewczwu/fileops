# ADR 0004: CI runs in a pinned container and every step is a script

**Status:** accepted · **Date:** 2026-10-10

## Context

Pipelines commonly accumulate logic in YAML `run:` blocks and depend on whatever the hosted runner
image happens to contain. Both make "works in CI but not locally" (and vice versa) a routine
failure, and make the pipeline impossible to exercise without pushing.

## Decision

- All build, test and packaging steps run inside `ghcr.io/andrewczwu/fileops-build`, built from
  `docker/Dockerfile.build` by its own workflow and rebuilt weekly for base-image patches.
- Each multi-step task is a shell script in `scripts/` with a `Makefile` target; workflows call
  `make <target>`. The YAML holds orchestration (job graph, caching, artefacts, summaries) only.
- The workflows are runnable offline with `act` (`.actrc`); steps that need GitHub services are
  guarded and skipped locally.
- Third-party actions are pinned to commit SHAs; Dependabot keeps them current. Jobs declare
  least-privilege `permissions`, `timeout-minutes` and `concurrency`.

## Reasons

- Same image locally and in CI removes toolchain drift as a variable.
- `make test` reproducing the CI job is the fastest path from red build to fix.
- Scripts are testable, diff-able and reviewable; inline YAML shell is not.
- SHA pinning and least privilege are the current baseline for GitHub Actions supply-chain
  hygiene (`zizmor` enforces them).

## Consequences

- The toolchain image is a dependency that must itself be versioned. The release workflow pins it
  by digest (printed in the build-image job summary) so a release is reproducible bit for bit. CI
  deliberately uses the `:main` tag instead: the tag is only ever written by this repository's own
  workflow from a pinned Dockerfile, and a tag reference is what lets `act` run the jobs against a
  locally built image without network access.
- Running jobs as the image's unprivileged user (`--user 1001`) is needed for permission tests,
  which costs a little setup (`DOTNET_CLI_HOME`, explicit `NUGET_PACKAGES`).
- `act` cannot run everything (OIDC, artifact API, code scanning), so some jobs are only proven on
  GitHub.
