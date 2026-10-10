#!/usr/bin/env bash
# Print the CHANGELOG.md section for one version (Keep a Changelog format) as release notes.
set -euo pipefail

VERSION="${1:?usage: changelog-section.sh X.Y.Z}"

awk -v version="${VERSION}" '
  /^## \[/ { in_section = index($0, "## [" version "]") == 1; if (in_section) { print; next } }
  in_section { print }
' CHANGELOG.md
