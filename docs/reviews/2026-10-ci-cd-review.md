# CI/CD review, October 2026

Reviewed at commit `f5089cc` on 2026-10-10. Scope: the whole repository, with emphasis on the pipeline, read
as a hiring panel for a defense/aerospace DevOps role would read it (CI/CD skill, version control,
implementation quality; supply-chain security, reproducibility, auditability, fast feedback; no
over-engineering).

Every finding was checked against the code, the GitHub API or a local run. `dotnet test` passes (43 unit, 31
integration, 0 skipped, SDK 10.0.301). The v1.0.0 assets were downloaded, `SHA256SUMS` checked, and the
`.deb` and tarball verified with
`gh attestation verify --signer-workflow andrewczwu/fileops/.github/workflows/release.yml`.

## 1. Executive summary

Verdict: well above the norm for a CLI of this size, and above most production repositories on supply-chain
hygiene. Every action is SHA-pinned, every job has least-privilege `permissions` and a timeout, there is no
`pull_request_target`, NuGet restores are locked and audited, releases build in a digest-pinned image with no
cache, and the `.deb` and tarball carry SLSA provenance that verifies. The pipeline is measured (2 m 40 s to
1 m 42 s, in `docs/ARCHITECTURE.md`), the logic lives in scripts behind a Makefile, and one integration suite
runs against the managed build, the NativeAOT binary and the installed package on four distributions.
Telemetry is a tested contract.

What costs points is a short list of defects that contradict the repository's own claims: `make lint`
cannot fail on a found secret, two smoke-test assertions can never fail, the package declares no `libc6`
dependency while the README says glibc is the only one, the release workflow at HEAD has never run, the
`build-image` concurrency group can cancel the publish that CI depends on, and the first command a reviewer
types (`act -j test`) no longer exists. Each is a small fix; tag protection and immutable releases are the
missing platform settings.

## 2. Scorecard

Status: `in place` / `partial` / `missing` / `deliberately omitted`. Tier: `must` / `should` / `nice` /
`overkill` for a single-maintainer CLI shipped to Debian hosts. Where I overrule a researcher's tier, the
rationale says so.

| Practice | What the project does | Status | Tier and rationale |
| --- | --- | --- | --- |
| Action SHA pinning + Dependabot | Every `uses:` is a full SHA with a version comment; weekly grouped Dependabot for actions, NuGet, Docker | in place | must: tag-rewrite attacks are the canonical CI compromise |
| Least-privilege tokens | Top-level `contents: read`, per-job escalation, `persist-credentials: false` everywhere | in place | must |
| No `pull_request_target` / `workflow_run` | None; zizmor runs on every change | in place | must |
| Build provenance | `.deb` and tarball attested; verified with the signer pinned to `release.yml` | in place | must; `SHA256SUMS` itself is not a subject (F18) |
| Tag ruleset on `v*` | The only ruleset targets the default branch; tags can be moved or deleted | missing | must: provenance is bound to a tag, so the tag must be immutable; one settings change |
| Immutable releases | Setting is off (`enabled: false` via API); v1.0.0 is mutable | missing | should: locks assets and adds a release attestation; enable before v1.1.0 |
| Toolchain image pinning | Release by digest; CI by the moving `:main` tag, justified in ADR 0004 | partial | nice. Overrules the security report's "do first": only this repo's workflow writes the tag. The real gap is the unpinned apt and unchecksummed nfpm in the Dockerfile (F15) |
| CodeQL / SAST | None; analyzers at `latest-recommended`, warnings as errors | missing | should: free, `build-mode: none`, 20 lines; the first thing a security reviewer asks about |
| SBOM | None | missing | nice. Overrules "should": lock files already enumerate eight packages; add CycloneDX when a buyer asks |
| Secret scanning | gitleaks pre-commit and full-history CI; GitHub secret scanning and push protection on (verified) | partial | must: `make lint` masks a positive result (F2) |
| Workflow linting | actionlint and zizmor, SARIF to code scanning | in place | should; actionlint arrives via `curl \| bash` (F11) |
| harden-runner | Not used | deliberately omitted | overkill: a root third-party action per job on a repo with no secrets |
| Job structure and speed | Three parallel host jobs, one container job, 4-way smoke matrix; 1 m 42 s measured | in place | must: consolidated on a measurement |
| Caching | NuGet cache keyed on lock files; release deliberately uncached | in place | must |
| Concurrency and timeouts | On every workflow | partial | must: `cancel-in-progress` also hits `main` (F10) and the image build (F5) |
| Required checks | Eight named checks, strict policy, no bypass actors | in place | must. Gate job deliberately omitted: all jobs run unconditionally |
| Job summaries | Coverage, telemetry table, test report, artefact sizes, `ldd`, control file | in place | should: zero-click feedback |
| Local reproducibility | Makefile, `docker-%`, `act` with `.actrc` | partial | must: documented commands are stale (F1); `docker-%` runs as root (F13) |
| Pipeline-duration trend | ADR table only | missing | nice. Overrules "should": about twenty runs exist; a weekly p95 job is machinery |
| Version from tag, changelog gate | `version-from-tag.sh` refuses tags without a CHANGELOG section | in place | must |
| Artifacts and checksums | `.deb`, tarball, `SHA256SUMS` in `sha256sum -c` format | in place | must; tarball holds only the binary (F18) |
| Verification docs | README: `gh attestation verify --owner` | partial | should: without `--signer-workflow` any workflow in the repo satisfies the check |
| Debian packaging quality | nfpm, DEP-5 copyright, man page, changelog, lintian `--fail-on error` | partial | must: no `Depends`, lintian warns, stale override (F6) |
| glibc floor | Built on noble (2.39), proved on 22.04 (2.35) by smoke; undeclared | partial | should: encode as `libc6 (>= 2.35)` |
| Release dry run | None; `release.yml` at HEAD has not executed | missing | must (F4) |
| Post-publish verification | None | missing | nice. Overrules the release report: the verify matrix tests the exact bytes that are uploaded |
| Reproducible-build claim | `Deterministic`, locked restore; changelog date from `date` | partial | should: `SOURCE_DATE_EPOCH` or soften "bit for bit" (F14) |
| Test layers | 43 unit (FakeLogger, ActivityListener, MetricCollector) + 31 subprocess tests | in place | must |
| Artifact testing | Same suite on managed, native and installed `.deb`, four distros, uid 1001 | in place | should: unusual; name it in the README |
| Telemetry as evidence | Contract tests on logs, spans, metrics; NDJSON captured and summarised | in place | should |
| Smoke-test assertions | Two `!` assertions are no-ops under `set -e` | partial | must (F3) |
| Coverage policy | Reported, no threshold | partial | nice: report, do not gate; say so |
| AI triage | `diagnose` job, opt-in by secret; the repo has no secrets (`gh secret list` empty) | partial | nice: keep opt-in; narrow permissions (F12) |
| OTLP export path | Wired and documented; never exercised in CI | missing | nice |
| Edge-case tests | Empty path, long names, symlinks, mode after `--force` | missing | should: two defects below would have been caught |
| Conventional Commits | `commit-msg` hook and CI | in place | should |
| Branch ruleset | PR required, linear, squash/rebase, thread resolution, no bypass | in place | must |
| CODEOWNERS | Present; 0 approvals, no code-owner review | partial | nice: decorative on a solo repo; say so |
| Dependency updates | Dependabot, three ecosystems | partial | should: `lint.sh` image tags and SDK/toolchain coupling are outside it (F16) |

## 3. Findings

Severity is panel impact: a `high` contradicts a claim the repository makes about itself. Effort: S under an
hour, M a few hours.

### Blocker

None. Nothing stops the pipeline or ships a broken artifact.

### High

- **F1** `README.md:153`, `CONTRIBUTING.md:51-52`, `Makefile:4`. Documented commands are `act -j test` and
  `act -j package`; the jobs are `build` and `smoke`, and the Makefile header still names "the CI test job".
  First thing a reviewer types. Fix: `act -j build`, `act -j smoke --matrix image:debian:bookworm-slim`. S.
- **F2** `Makefile:48`. `command -v gitleaks && gitleaks ... || echo "not installed"`: when gitleaks finds a
  secret the `||` branch runs and `make lint` exits 0 (reproduced). The pre-commit hook is correct; this
  target undermines the headline claim. Fix: an `if command -v gitleaks; then ...; else ...; fi` block. S.
- **F3** `scripts/smoke-test.sh:28,45`. `! test -e out.txt` and `! command -v fileops` run under `set -eu`
  in `sh`. POSIX exempts a pipeline beginning with `!` from `-e` (reproduced: `sh -c 'set -e; ! true; echo
  reached'` prints `reached`), so "delete removed the file" and "removed cleanly" can never fail. Fix:
  `if test -e out.txt; then exit 1; fi`, likewise for the second. S.
- **F4** `.github/workflows/release.yml`. The only release run (38072998861) built `bb88300`. Since then
  lintian moved into the image (`3e5f94e`), the digest changed (`8fc6255`) and both workflows were reshaped.
  HEAD has never executed and there is no dry-run path: the trigger is `v*.*.*` only and `gh release create`
  has no `--prerelease`. Fix: add `--prerelease` when the version contains `-`, then cut `v1.1.0-rc.1`. S.
- **F5** `.github/workflows/build-image.yml:23-25`. `group: build-image` with `cancel-in-progress: true` is
  shared by `main` pushes, PR builds and the weekly schedule, so a PR touching the Dockerfile cancels an
  in-flight `main` publish and CI keeps pulling a stale `:main`. Fix: `group: build-image-${{ github.ref }}`,
  `cancel-in-progress: ${{ github.event_name == 'pull_request' }}`. S.
- **F6** `packaging/nfpm.yaml`, `packaging/lintian-overrides:3`, `README.md:35`. The control file has no
  `Depends` (confirmed from the v1.0.0 `.deb`); lintian in CI run 38082554183 prints
  `W: fileops: undeclared-elf-prerequisites (ld-linux-x86-64.so.2 libc.so.6 libm.so.6)`, passed by
  `--fail-on error`; the README says "the only dependency is glibc". The override suppresses
  `statically-linked-binary`, which cannot fire for a binary lintian itself reports as dynamically linked.
  Fix: `depends: ["libc6 (>= 2.35)"]`, delete the override and its `contents` entry, `--fail-on warning`. S.
- **F7** `src/FileOps.Core/FileOperations.cs:78-81`. `copy` renames the temp file into place and only then
  applies the source mode, so a 0600 source is exposed at the umask default between the two calls, permanently
  if `SetUnixFileMode` throws. Fix: set the mode on the temp path inside the `write` callback, before the
  rename. S.

### Medium

- **F8** `src/FileOps.Core/FileOperations.cs:36,57,99,135`. `ThrowIfNullOrWhiteSpace` runs outside
  `Execute`, so `fileops create ""` exits 70 with "unexpected error" (reproduced), with no span and no `1002`
  event, while the README and man page promise exit 1. Fix: throw `FileOpsException(UsageError)` inside
  `Execute`; add an integration test. S.
- **F9** `src/FileOps.Core/AtomicFile.cs:36`. The temp name `.<name>.<32 hex>.tmp` is 38 bytes longer than
  the destination; a valid 240-character name fails with exit 4 (reproduced). Fix: truncate the name
  component; add a test. S.
- **F10** `.github/workflows/ci.yml:24-26`. `cancel-in-progress: true` applies to `main`; run 38074458368 (a
  Dependabot merge) was cancelled, so that commit has no completed CI. Fix: cancel only on `pull_request`. S.
- **F11** `.github/workflows/ci.yml:92-96`. actionlint is installed with `bash <(curl ...)` from a mutable
  tag, contradicting the pinning story; `scripts/lint.sh:94-99` already runs the pinned image. Fix: add
  `workflows` to the `lint.sh` call and delete the step. S.
- **F12** `.github/workflows/ci.yml:215-253` (`diagnose`). `needs: [build]` only, so smoke failures are never
  diagnosed; it runs on `push` where there is no PR to comment on; it holds `pull-requests: write` and
  `id-token: write` while feeding PR-authored `.trx` and NDJSON to an LLM; and it has never run because the
  repository has no secrets. Fix: `needs: [build, smoke]`, same-repo `pull_request` only, drop `id-token`,
  label it dormant in the README. S.
- **F13** `Makefile:21-25`. `docker-%` runs as root with `/root/.nuget` mounted; CI runs as uid 1001. The
  `NonRootFact` tests skip locally while the README calls the two "identical". Fix: `--user 1001:1001` with
  `HOME=/tmp/home` and the cache mounted there. S.
- **F14** Documentation drift. `README.md:118` names `debian:12`/`13` (matrix: `bookworm-slim`/`trixie-slim`);
  `README.md:29-30,40` hard-code `fileops_1.0.0_amd64.deb` and verify without `--signer-workflow`;
  `docs/adr/0004-...md:33` claims "reproducible bit for bit" while `scripts/package.sh:32` embeds `date`
  because nothing exports `SOURCE_DATE_EPOCH`; `README.md:149` says "same toolchain everywhere" while `format`
  and `lint` run on the host. Fix: correct each; export `SOURCE_DATE_EPOCH=$(git log -1 --format=%ct)` in
  `release.yml`. S.
- **F15** `docker/Dockerfile.build:8,16-21`. Base image by tag, apt unpinned, nfpm `.deb` downloaded without
  a checksum although nfpm publishes `checksums.txt`. Fix: `sha256sum -c` against a pinned value; let
  Dependabot's Docker ecosystem add the base digest. S.
- **F16** `Directory.Build.props:27`, `docker/Dockerfile.build:8`, `Directory.Packages.props:15-19`.
  `NativeAotToolchainVersion=10.0.12` must move with the SDK tag `10.0.401` and five `Microsoft.Extensions.*`
  pins; Dependabot proposes each alone and the locked restore then fails. Fix: a ten-line check script run
  from `lint`, or a Dockerfile comment listing what else to bump. M.
- **F17** Overwrite and interruption semantics. `create --force` on a 0600 file leaves 0644 (reproduced)
  because the rename replaces the inode; SIGINT leaves `.name.<guid>.tmp` because `finally` does not run on a
  signal; README and CHANGELOG say an interrupted run "never leaves a partial file". Fix: document both (the
  destination is indeed never partial), or copy the existing mode onto the temp file. S.
- **F18** `.github/workflows/release.yml:63,113-117`. The tarball contains only `fileops` (checked), no
  LICENSE or man page; `SHA256SUMS` is not an attestation subject (`gh attestation verify SHA256SUMS` returns
  404). Fix: stage the three files under `fileops-<version>/`; add `SHA256SUMS` to `subject-path`. S.

### Low

- **F19** `.gitleaks.toml:12` allowlists all of `.github/workflows`, where pasted tokens usually land.
  Allowlist the SHA-after-`@` pattern instead.
- **F20** `ci.yml:82,113-115`. `security-events: write` is read-only on fork PRs, so the zizmor SARIF upload
  would fail; container pulls assume the GHCR package is public (not verifiable without `read:packages`).
- **F21** `ci.yml:154-167` and `release.yml:54-64` duplicate the native-test, package, lintian and tarball
  steps; the smoke image list lives in four places. Add `make test-native` and `make release-assets`.
- **F22** Container steps run under `sh -e` (log: `shell: sh -e {0}`), so inline pipes lack `pipefail`. Set
  `defaults.run.shell: bash`.
- **F23** Three commit subjects exceed the 72-character rule the hook enforces (`5d0c876`, `2356ef2`,
  `5d96ccb`); all predate the hook. Expect the question.
- **F24** `Makefile:30` hides `docker-%` from `make help`; `make ci` runs `package` on macOS where it cannot
  work.
- **F25** `scripts/lint.sh:19-25` pins six linter images by tag outside Dependabot; acceptable with a
  reminder.

## 4. What is done well

1. A real PR flow with a strict ruleset: PR required, linear history, squash or rebase only, thread
   resolution, eight required checks under a strict up-to-date policy, no bypass actors (verified via API).
2. Supply-chain posture most production repositories lack: SHA-pinned actions, per-job `permissions`,
   `persist-credentials: false`, no `pull_request_target`, zizmor plus actionlint plus gitleaks, locked and
   audited restores, and a release job that skips the cache so a poisoned PR cache cannot reach an artifact.
3. Provenance that verifies: both v1.0.0 assets pass `gh attestation verify` with the signer pinned to
   `release.yml@refs/tags/v1.0.0`.
4. The tested artifact is the shipped artifact: one integration suite against the managed build, the
   NativeAOT binary and the installed `.deb` in four clean containers, as an unprivileged user.
5. Telemetry as a contract: stable event-id ranges, source-generated logging, low-cardinality metric tags,
   and tests asserting that logged bytes, exit code and reason match reality.
6. Measured optimisation: the move from three container jobs to one is justified with per-step timings in
   `docs/ARCHITECTURE.md`, and live run times (1 m 32 s to 1 m 48 s) match.
7. Reproducible-restore engineering: the NativeAOT toolchain pin and RID fixes are explained in place, and
   locked restore works across SDK patch versions (confirmed on 10.0.301).
8. Logic in scripts, not YAML: every multi-step job is a Makefile target, `act` works with clean guards, and
   the helper scripts are small, commented and shellcheck-clean.
9. Packaging done properly for a non-Debian-native tool: nfpm, DEP-5 copyright, a man page that matches the
   help, tilde pre-release mapping, lintian in the build job, and a smoke script that checks exit codes,
   stderr discipline and clean removal.
10. Documentation that cross-checks: four ADRs with real trade-offs, an observability contract with the event
    table, a changelog whose 1.0.0 date matches the tag, a PR template, and comments that explain why.

## 5. Deliberately not doing

Considered and rejected as overkill for a single-maintainer CLI; read these as choices, not gaps.

- **apt repository, PPA or signed repo**: key custody and hosting trust for one package that installs with
  `apt install ./file.deb`.
- **RPM, musl or arm64**: the brief targets Debian on x86-64; NativeAOT cross-compilation needs a second
  toolchain for no requested consumer.
- **Homebrew tap, container image, `curl | sh` installer**: an unpinned script run as root is the pattern the
  rest of the repository argues against.
- **Merge queue**: valuable only with concurrent PRs; here it adds a full extra run per merge.
- **Reusable workflows, composite actions**: cross-repository sharing with nothing to share; the Makefile
  already removes duplication.
- **SLSA Build L3 builder**: attestations give L2; L3 adds indirection to a release path that fits in one
  readable file.
- **harden-runner block mode**: an egress allowlist to maintain on a repository with no secrets and pinned
  actions.
- **SBOM, cosign**: lock files enumerate the graph and Sigstore already signs the provenance; add CycloneDX
  when a buyer requires it.
- **Mutation testing, fuzzing, property tests**: 74 deterministic tests on a tool that parses only its own
  arguments; the defects found were missing cases, not weak assertions.
- **Test sharding, larger runners, remote build cache**: the suites take 21 s; runner fixed cost exceeds any
  gain.
- **OpenTelemetry collector, Grafana, Tempo for the pipeline**: persistent infrastructure to chart about
  twenty runs; the ADR table and job summaries are the honest substitute.
- **Dagger or Earthly**: a second execution engine over a Makefile that already runs identically locally and
  in CI.
- **Renovate**: Dependabot already covers the three ecosystems that matter.
- **Coverage ratchets or gates**: a floor on 74 tests encourages padding, not confidence.
- **Required approvals**: a solo repository cannot satisfy it without a second account.
- **Aggregate gate job**: eight explicit required checks are more auditable than one indirection when every
  job runs unconditionally.

## 6. Implementation plan for the must-haves

Four PR-sized batches, ordered so each leaves `main` green; an afternoon in total. CodeQL, immutable
releases and the tarball contents are `should` items folded in where they cost nothing extra.

### Batch 1: `fix(ci): close the gaps that let failures pass silently`

Changes: F2, F3, F5, F10, F11, F12. Verification: commit a scratch file with a fake AWS key on a branch and
confirm `make lint` exits non-zero; comment out `fileops delete out.txt` in the smoke script and confirm the
matrix goes red; open a PR touching `Dockerfile.build` while a `main` image build runs and confirm neither
cancels the other; `act -j build` still passes.

### Batch 2: `fix(packaging): declare libc6, drop the stale override and prove the release path`

Changes: F6, F4 (`--prerelease` for versions containing `-`), F18 (tarball with `LICENSE` and `fileops.1`,
`SHA256SUMS` attested), the `SOURCE_DATE_EPOCH` export from F14. Settings, outside the PR: a tag ruleset on
`v*` restricting updates and deletions with no bypass actors, and immutable releases on. Verification: lintian
clean at warning level in CI; `dpkg-deb --field` shows `Depends: libc6 (>= 2.35)`; tag `v1.1.0-rc.1` and
confirm a prerelease with four assets; `gh attestation verify --signer-workflow ...` on each; a
`git push --delete origin v1.0.0` is refused.

### Batch 3: `fix(core): map empty paths to usage errors and preserve modes before the rename`

Changes: F8, F9, F7. Tests first: `create ""` exits 1 with a `1002` event; a 240-character name succeeds; a
0600 source copies as 0600 with the temp file never wider. Verification: the three new tests fail on `main`
and pass on the branch; the native-binary run and smoke matrix stay green.

### Batch 4: `docs: align the README and CONTRIBUTING with the pipeline as built`

Changes: F1, F13 (so "identical" becomes true), F14, F17, the dormant-`diagnose` note from F12, and a
"Non-goals" paragraph lifted from section 5. Verification: `scripts/lint.sh markdown`, then follow the README
from a clean clone and run every command it gives.

Optional fifth PR, `ci: add CodeQL for C#` with `build-mode: none` on PRs and weekly: the `should` item a
security-minded panel is most likely to ask about, independent of everything above.

## 7. Sources

Primary sources the recommendations rest on; items the research reports marked unverified are omitted unless
confirmed during this review.

- GitHub, "Available rules for rulesets" (tag targets; restrict creations, updates, deletions):
  <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets>
- GitHub, "Immutable releases" (locks tag and assets, generates a release attestation):
  <https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases>
- GitHub, "Artifact attestations" (Build L2 by default; L3 needs a reusable workflow):
  <https://docs.github.com/en/actions/concepts/security/artifact-attestations>
- GitHub CLI, `gh attestation verify` (`--signer-workflow`, `--format json`):
  <https://cli.github.com/manual/gh_attestation_verify>
- SLSA v1.2 build requirements: <https://slsa.dev/spec/v1.2/build-requirements>
- OpenSSF Scorecard checks (SAST, SBOM, Signed-Releases, Branch-Protection):
  <https://github.com/ossf/scorecard/blob/main/docs/checks.md>
- `actions/attest-build-provenance` README (v4 wraps `actions/attest`):
  <https://github.com/actions/attest-build-provenance/blob/main/README.md>
- Debian lintian, `missing-dependency-on-libc` (the `undeclared-elf-prerequisites` text is quoted from CI run
  38082554183): <https://udd.debian.org/lintian-tag/missing-dependency-on-libc>
- reproducible-builds.org, `SOURCE_DATE_EPOCH`: <https://reproducible-builds.org/docs/source-date-epoch/>
- POSIX `set -e` exemption for pipelines beginning with `!` (reproduced locally):
  <https://pubs.opengroup.org/onlinepubs/9699919799/utilities/V3_chap02.html#set>
- GitHub community discussion 44490, required checks and path filters:
  <https://github.com/orgs/community/discussions/44490>
