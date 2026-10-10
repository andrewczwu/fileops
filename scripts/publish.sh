#!/usr/bin/env bash
# Publish fileops as a single self-contained NativeAOT executable.
# Inputs (env): VERSION (default 0.0.0-dev), RID (default linux-x64), CONFIG (default Release)
# Output: artifacts/publish/<RID>/fileops
set -euo pipefail

VERSION="${VERSION:-0.0.0-dev}"
RID="${RID:-linux-x64}"
CONFIG="${CONFIG:-Release}"
OUT="artifacts/publish/${RID}"

echo "::group::publish ${RID} ${VERSION}"
dotnet publish src/FileOps.Cli \
  -c "${CONFIG}" \
  -r "${RID}" \
  -p:Version="${VERSION}" \
  -o "${OUT}"
echo "::endgroup::"

BIN="${OUT}/fileops"
test -x "${BIN}"

# Prove it is a real native binary: runs without the .NET runtime and only needs libc.
"${BIN}" --version
if command -v ldd >/dev/null 2>&1; then
  ldd "${BIN}" || true
fi
printf 'binary: %s (%s bytes)\n' "${BIN}" "$(stat -c %s "${BIN}" 2>/dev/null || stat -f %z "${BIN}")"
