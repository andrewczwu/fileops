# Security

## Reporting a vulnerability

Please report security issues privately through
[GitHub's private vulnerability reporting](https://github.com/andrewczwu/fileops/security/advisories/new)
rather than a public issue. You will get an acknowledgement within a few days.

## What the project does to stay secure

- **Secrets never reach history.** A `gitleaks` pre-commit hook scans staged changes, CI scans the
  full history on every push and pull request, and GitHub push protection is enabled.
- **Supply chain.** Every third-party GitHub Action is pinned to a commit SHA and updated by
  Dependabot. NuGet restores run in locked mode against committed `packages.lock.json` files, and
  `NuGetAudit` fails the build on high-severity advisories. Workflows run with
  `permissions: contents: read` and escalate per job only where needed.
- **Provenance.** Release artefacts carry SLSA build provenance attestations; verify with
  `gh attestation verify fileops_<version>_amd64.deb --owner andrewczwu` and add
  `--signer-workflow andrewczwu/fileops/.github/workflows/release.yml`; without it, any workflow in
  the repository satisfies the check.
- **Repository rules.** CodeQL (default setup) analyses the C# code, a tag ruleset protects `v*` tags
  from deletion and movement, and releases are immutable once published.
- **Workflow hygiene.** `actionlint` and `zizmor` run on every change to `.github/workflows`.
- **Least surprise at runtime.** The tool never overwrites an existing file without `--force`,
  never deletes directories, and never writes to stdout except for `--help` and `--version`.
