# Contributing

## Setup

```sh
git clone https://github.com/andrewczwu/fileops && cd fileops
make setup          # installs git hooks (gitleaks, actionlint) and local tools
make test           # build + unit + integration tests
```

Requirements: the .NET 10 SDK (`global.json` pins the feature band), and Docker if you want to build
the Linux native binary or run the pipeline locally. `brew install gitleaks actionlint zizmor nfpm act`
gives you every tool the hooks and `make lint` use.

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
   `feat:`, `fix:`, `ci:`, `build:`, `docs:`, `test:`, `refactor:`, `chore:`.
3. Add or update tests. Behavioural changes must also assert on the telemetry they emit — see
   [docs/OBSERVABILITY.md](docs/OBSERVABILITY.md) for the event-id contract.
4. `make lint test` locally, or `make docker-test` to run inside the exact CI image.
5. Open a pull request; the template has the checklist. CI must be green and history is kept linear
   (squash or rebase).

## Running the pipeline locally

```sh
make build-image BUILD_IMAGE=ghcr.io/andrewczwu/fileops-build:main   # once
act -j build                # the CI "Build, test & package" job, offline
act -j smoke --matrix image:debian:bookworm-slim
```

`.actrc` holds the defaults. Steps that depend on GitHub-hosted services (artifact upload, OIDC,
code scanning) are skipped under `act` and marked as such in the workflow.

## Project layout

```text
src/FileOps.Core        the operations, telemetry and atomic-write logic (no CLI concerns)
src/FileOps.Cli         System.CommandLine front end, host wiring, exit-code mapping
tests/FileOps.Core.Tests          unit tests: file-system outcomes + logs/spans/metrics
tests/FileOps.Integration.Tests   runs the real executable; parses its JSON log stream
packaging/              nfpm spec, man page, Debian copyright
scripts/                every multi-step task CI runs, callable locally through the Makefile
docker/                 the pinned toolchain image
docs/                   architecture, observability contract, decision records
```

## Decision records

Non-obvious choices live in [docs/adr](docs/adr). Add a new numbered file when you make one.
