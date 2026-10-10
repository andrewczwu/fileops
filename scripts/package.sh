#!/usr/bin/env bash
# Build the Debian package from the published binary using nfpm.
# Inputs (env): VERSION (default 0.0.0-dev), RID (default linux-x64)
# Output: artifacts/deb/fileops_<version>_<arch>.deb and SHA256SUMS
set -euo pipefail

VERSION="${VERSION:-0.0.0-dev}"
RID="${RID:-linux-x64}"
OUT="artifacts/deb"

case "${RID}" in
  linux-x64)   ARCH=amd64 ;;
  linux-arm64) ARCH=arm64 ;;
  *) echo "unsupported RID: ${RID}" >&2; exit 1 ;;
esac

# Debian versions cannot contain '-' in the upstream part unless there is a revision; map
# SemVer pre-release "1.2.3-rc.1" to "1.2.3~rc.1" so it sorts before the final release.
DEB_VERSION="${VERSION/-/\~}"

# Stage everything at the fixed paths packaging/nfpm.yaml expects.
STAGE="artifacts/stage"
rm -rf "${STAGE}" && mkdir -p "${STAGE}" "${OUT}"
cp "artifacts/publish/${RID}/fileops" "${STAGE}/fileops"
gzip -9 -n -c packaging/fileops.1 > "${STAGE}/fileops.1.gz"
# Debian policy wants a changelog in /usr/share/doc; derive a minimal one from the project CHANGELOG.
{
  printf 'fileops (%s) stable; urgency=medium\n\n  * See https://github.com/andrewczwu/fileops/blob/main/CHANGELOG.md\n\n -- Andrew Wu <andrewczwu@gmail.com>  %s\n' \
    "${DEB_VERSION}" "$(date -R -u -d @"${SOURCE_DATE_EPOCH:-$(date +%s)}" 2>/dev/null || date -R -u)"
} | gzip -9 -n > "${STAGE}/changelog.gz"

VERSION="${DEB_VERSION}" ARCH="${ARCH}" \
  nfpm package --config packaging/nfpm.yaml --packager deb --target "${OUT}/"

(
  cd "${OUT}"
  sha256sum ./*.deb > SHA256SUMS
  cat SHA256SUMS
)
dpkg-deb --info "${OUT}"/*.deb 2>/dev/null || true
