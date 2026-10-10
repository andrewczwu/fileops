#!/usr/bin/env bash
# Run lintian (Debian's package checker) against the built .deb, failing on errors.
# Runs inside a container so the host does not need Debian tooling.
set -euo pipefail

PLATFORM="${DOCKER_PLATFORM:-linux/amd64}"
LINT_IMAGE="${LINT_IMAGE:-fileops-lint:local}"

if ! docker image inspect "${LINT_IMAGE}" >/dev/null 2>&1; then
  docker build --platform "${PLATFORM}" -t "${LINT_IMAGE}" -f docker/Dockerfile.lint docker
fi

docker run --rm --platform "${PLATFORM}" -v "$(pwd)/artifacts/deb:/pkg:ro" "${LINT_IMAGE}" \
  sh -c 'lintian --fail-on error --no-tag-display-limit --info /pkg/*.deb && echo "lintian: no errors"'
