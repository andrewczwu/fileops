#!/usr/bin/env bash
# Validate a release tag (vMAJOR.MINOR.PATCH[-prerelease]) and print `version=<semver>` for $GITHUB_OUTPUT.
# Also refuses to release a version that has no section in CHANGELOG.md.
set -euo pipefail

TAG="${1:?usage: version-from-tag.sh vX.Y.Z}"

if [[ ! "${TAG}" =~ ^v([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?)$ ]]; then
  echo "::error::tag '${TAG}' is not vMAJOR.MINOR.PATCH[-prerelease]" >&2
  exit 1
fi
VERSION="${BASH_REMATCH[1]}"

if ! grep -qE "^## \[${VERSION//./\\.}\]" CHANGELOG.md; then
  echo "::error file=CHANGELOG.md::no '## [${VERSION}]' section in CHANGELOG.md; add release notes before tagging" >&2
  exit 1
fi

echo "version=${VERSION}"
