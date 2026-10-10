#!/usr/bin/env bash
# Build the Debian package from the published binary using nfpm.
# Inputs (env): VERSION (default 0.0.0-dev), RID (default linux-x64), SOURCE_DATE_EPOCH (optional)
# Output: artifacts/deb/fileops_<version>_<arch>.deb and SHA256SUMS
set -euo pipefail

VERSION="${VERSION:-0.0.0-dev}"
RID="${RID:-linux-x64}"
OUT="artifacts/deb"
BIN="artifacts/publish/${RID}/fileops"
# Oldest distribution the install smoke test covers (Ubuntu 22.04). A binary that needs a newer glibc
# than this would pass the build and fail at install time on a supported target.
MAX_GLIBC="2.35"

case "${RID}" in
  linux-x64) ARCH=amd64 ;;
  linux-arm64) ARCH=arm64 ;;
  *)
    echo "unsupported RID: ${RID}" >&2
    exit 1
    ;;
esac

test -x "${BIN}" || {
  echo "${BIN} not found; run 'make publish' first" >&2
  exit 1
}

# Debian versions cannot contain '-' in the upstream part unless there is a revision; map
# SemVer pre-release "1.2.3-rc.1" to "1.2.3~rc.1" so it sorts before the final release.
DEB_VERSION="${VERSION/-/\~}"

# The NativeAOT binary links glibc dynamically. Derive the minimum version from the versioned
# symbols it imports so the package declares an honest `Depends:` instead of none.
GLIBC_FLOOR="$(objdump -T "${BIN}" | grep -oE 'GLIBC_[0-9]+\.[0-9]+' | sed 's/GLIBC_//' | sort -t. -k1,1n -k2,2n -u | tail -n1)"
if [[ -z "${GLIBC_FLOOR}" ]]; then
  echo "could not determine the glibc floor of ${BIN}" >&2
  exit 1
fi
if [[ "$(printf '%s\n%s\n' "${GLIBC_FLOOR}" "${MAX_GLIBC}" | sort -t. -k1,1n -k2,2n | tail -n1)" != "${MAX_GLIBC}" ]]; then
  echo "binary requires glibc ${GLIBC_FLOOR}, newer than the ${MAX_GLIBC} on the oldest supported target" >&2
  exit 1
fi
echo "glibc floor: ${GLIBC_FLOOR} (limit ${MAX_GLIBC})"

# Stage everything at the fixed paths the nfpm config expects.
STAGE="artifacts/stage"
rm -rf "${STAGE}" && mkdir -p "${STAGE}" "${OUT}"
cp "${BIN}" "${STAGE}/fileops"
gzip -9 -n -c packaging/fileops.1 >"${STAGE}/fileops.1.gz"
# Debian policy wants a changelog in /usr/share/doc; derive a minimal one from the project CHANGELOG.
# SOURCE_DATE_EPOCH (set by the release workflow from the commit date) keeps the entry reproducible.
{
  printf 'fileops (%s) stable; urgency=medium\n\n  * See https://github.com/andrewczwu/fileops/blob/main/CHANGELOG.md\n\n -- Andrew Wu <andrewczwu@gmail.com>  %s\n' \
    "${DEB_VERSION}" "$(date -R -u -d @"${SOURCE_DATE_EPOCH:-$(date +%s)}" 2>/dev/null || date -R -u)"
} | gzip -9 -n >"${STAGE}/changelog.gz"

# nfpm expands ${VAR} in top-level fields only, so the dependency line is substituted into a staged copy.
sed "s|@LIBC6_DEPENDS@|libc6 (>= ${GLIBC_FLOOR})|" packaging/nfpm.yaml >"${STAGE}/nfpm.yaml"

VERSION="${DEB_VERSION}" ARCH="${ARCH}" \
  nfpm package --config "${STAGE}/nfpm.yaml" --packager deb --target "${OUT}/"

(
  cd "${OUT}"
  sha256sum ./*.deb >SHA256SUMS
  cat SHA256SUMS
)
dpkg-deb --field "${OUT}"/*.deb Package Version Architecture Depends 2>/dev/null || true
