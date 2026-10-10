#!/usr/bin/env bash
# Run lintian (Debian's package checker) against the built .deb, failing on errors.
# Uses the lintian on PATH when there is one (the toolchain image ships it); otherwise runs it in a
# container so the host does not need Debian tooling.
set -euo pipefail

if ! compgen -G "artifacts/deb/*.deb" >/dev/null; then
  echo "no .deb in artifacts/deb; run 'make package' first" >&2
  exit 1
fi

if command -v lintian >/dev/null 2>&1; then
  lintian --fail-on error --no-tag-display-limit --info artifacts/deb/*.deb
  echo "lintian: no errors"
  exit 0
fi

PLATFORM="${DOCKER_PLATFORM:-linux/amd64}"
LINT_IMAGE="${LINT_IMAGE:-fileops-lint:local}"

if ! docker image inspect "${LINT_IMAGE}" >/dev/null 2>&1; then
  docker build --platform "${PLATFORM}" -t "${LINT_IMAGE}" -f docker/Dockerfile.lint docker
fi

docker run --rm --platform "${PLATFORM}" -v "$(pwd)/artifacts/deb:/pkg:ro" "${LINT_IMAGE}" \
  sh -c 'lintian --fail-on error --no-tag-display-limit --info /pkg/*.deb && echo "lintian: no errors"'
