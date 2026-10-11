# Contributing

## Setup

```sh
git clone https://github.com/andrewczwu/fileops && cd fileops
make setup          # points git at .githooks and restores dotnet tools (reportgenerator)
make test           # build + unit + integration tests
```

Requirements: the .NET 10 SDK (`global.json` pins the feature band), GNU Make 3.82 or newer (macOS
ships 3.81; `brew install make` and use `gmake`, or put its `gnubin` directory first on `PATH`), and
Docker if you want to build the Linux native binary or run the pipeline locally.
`brew install make gitleaks actionlint zizmor shellcheck shfmt hadolint yamllint editorconfig-checker nfpm act`
and `npm i -g markdownlint-cli2` give you every tool the hooks and `make lint` use. Without them
`make lint` falls back to Docker images, but the pre-commit hook silently skips the missing tools.

## Formatting and style

Conventions are enforced, not suggested: every check below fails the build in CI and most run in the
pre-commit hook on the files you staged.

| What | Tool | Rules |
| --- | --- | --- |
| C# formatting, style, naming, analyzers | `dotnet format --verify-no-changes`, `EnforceCodeStyleInBuild`, warnings as errors | `.editorconfig` (style rules promoted to warnings), `Directory.Build.props` |
| Shell scripts and hooks | shellcheck, shfmt | `.shellcheckrc`, `[*.sh]` in `.editorconfig` |
| Dockerfiles | hadolint | `.hadolint.yaml` |
| YAML | yamllint | `.yamllint.yml` |
| Markdown | markdownlint-cli2 | `.markdownlint-cli2.yaml` |
| Whitespace, newlines, indentation (all files) | editorconfig-checker | `.editorconfig`, `.editorconfig-checker.json` |
| GitHub Actions workflows | actionlint, zizmor | — |
| Commit messages | `scripts/ci/check-commits.sh` (Conventional Commits) | `commit-msg` hook, CI on pull requests |
| Secrets | gitleaks | `.gitleaks.toml` |

`make lint` runs all of it. `scripts/lint.sh` uses a locally installed tool when one is on `PATH` and
the pinned Docker image otherwise, so laptops and CI check identical versions. `dotnet format` fixes
C# issues and `shfmt -w` fixes shell formatting; the other tools report only.

## Workflow

1. Branch from `main`: `git switch -c feat/short-description`.
2. Make small commits using [Conventional Commits](https://www.conventionalcommits.org):
   `feat:`, `fix:`, `ci:`, `build:`, `docs:`, `test:`, `refactor:`, `chore:`, `perf:`, `revert:`,
   `style:`. The header is at most 72 characters with no trailing period; body lines at most 100.
3. Add or update tests. Behavioural changes must also assert on the telemetry they emit — see
   [docs/OBSERVABILITY.md](docs/OBSERVABILITY.md) for the event-id contract.
4. `make lint test` locally, or `make docker-test` to run inside the exact CI image.
5. Open a pull request; the template has the checklist. CI must be green and history is kept linear
   (squash or rebase).

## Running the pipeline locally

```sh
docker pull ghcr.io/andrewczwu/fileops-build:main   # once (or build it: make build-image BUILD_IMAGE=ghcr.io/andrewczwu/fileops-build:main)
make act-build                                       # the CI "Build, test & package" job (needs network)
make act-smoke ACT_ARGS='--matrix image:debian:bookworm-slim'
```

`make act-<job>` scopes `act` to `ci.yml` (every workflow has a job called `build`, so a bare
`act -j build` would run three of them). The container job restores NuGet packages from the network,
so this needs connectivity; `make test` and `make docker-test` work offline from your package cache.
`.actrc` holds the remaining defaults. Steps that depend on GitHub-hosted services (caching, test reports,
OIDC, code scanning) are skipped under `act` and marked as such in the workflow.

## Project layout

```text
src/FileOps.Core        the operations, telemetry and atomic-write logic (no CLI concerns)
src/FileOps.Cli         System.CommandLine front end, host wiring, exit-code mapping
tests/FileOps.Core.Tests          unit tests: file-system outcomes + logs/spans/metrics
tests/FileOps.Integration.Tests   runs the real executable; parses its JSON log stream
packaging/              nfpm spec, man page, Debian copyright
scripts/                every multi-step task CI runs, callable locally through the Makefile
docker/                 the pinned toolchain image
docs/                   product requirements, architecture, observability contract, decision records, reviews
```

## Decision records

Non-obvious choices live in [docs/adr](docs/adr). Add a new numbered file when you make one.
