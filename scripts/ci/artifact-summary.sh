#!/usr/bin/env bash
# Markdown summary of the build artefacts for the job summary: sizes, dependencies, package metadata.
set -euo pipefail

BIN="artifacts/publish/${RID:-linux-x64}/fileops"
DEB="$(ls artifacts/deb/*.deb 2>/dev/null | head -n1 || true)"

human() { numfmt --to=iec --suffix=B "$1" 2>/dev/null || echo "$1 B"; }

echo "### Build artefacts"
echo
echo "| Artefact | Size |"
echo "|---|---:|"
if [[ -f "${BIN}" ]]; then
  echo "| \`${BIN}\` (NativeAOT) | $(human "$(stat -c %s "${BIN}")") |"
fi
if [[ -n "${DEB}" ]]; then
  echo "| \`${DEB}\` | $(human "$(stat -c %s "${DEB}")") |"
fi
echo

if [[ -f "${BIN}" ]] && command -v ldd >/dev/null; then
  echo "<details><summary>Shared library dependencies</summary>"
  echo
  echo '```'
  ldd "${BIN}" || true
  echo '```'
  echo "</details>"
  echo
fi

if [[ -n "${DEB}" ]] && command -v dpkg-deb >/dev/null; then
  echo "<details><summary>Package control file</summary>"
  echo
  echo '```'
  dpkg-deb --field "${DEB}"
  echo '```'
  echo "</details>"
fi
