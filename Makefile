# Entry point for humans and CI alike. Every CI job calls a target defined here, so a green
# pipeline can always be reproduced locally with the same command.
#
#   make test            unit + integration tests, as the CI "Build, test & package" job runs them
#   make docker-test     the same, inside the pinned toolchain image as the same unprivileged user
#   make package         NativeAOT publish + .deb (Linux only; use docker-package on macOS)
#   make smoke           install the .deb into clean Debian/Ubuntu containers and exercise it
#   make ci              lint, test, package, lintian, smoke in order (Linux with Docker)

# GNU Make 3.82 or newer is required: older versions silently ignore .SHELLFLAGS and would run
# recipes without `-euo pipefail`, letting a failing pipeline stage pass locally that fails in CI.
# macOS ships 3.81 (the last GPLv2 release); `brew install make` provides a current one as `gmake`.
ifneq ($(filter 3.80 3.81,$(MAKE_VERSION)),)
$(error GNU Make 3.82 or newer is required, found $(MAKE_VERSION). On macOS: brew install make, then use gmake or put "$$(brew --prefix make)/libexec/gnubin" first on PATH)
endif

SHELL := /usr/bin/env bash
.SHELLFLAGS := -euo pipefail -c
.DEFAULT_GOAL := help

VERSION   ?= 0.0.0-dev
RID       ?= linux-x64
CONFIG    ?= Release
ARTIFACTS := artifacts
BUILD_IMAGE ?= fileops-build:local
DOCKER_PLATFORM ?= linux/amd64

# Run as the image's unprivileged user, exactly like CI, so permission tests execute rather than
# skip. The host NuGet cache is mounted so container builds are fast and work offline.
DOCKER_RUN = docker run --rm --platform $(DOCKER_PLATFORM) \
	--user 1001:1001 -e HOME=/home/builder -e DOTNET_CLI_HOME=/tmp/dotnet-home \
	-v "$(CURDIR)":/src -w /src \
	-v "$(HOME)/.nuget/packages":/home/builder/.nuget/packages \
	-e VERSION=$(VERSION) -e RID=$(RID) -e CONFIG=$(CONFIG) -e CI=$${CI:-} \
	$(BUILD_IMAGE)

.PHONY: help setup restore build lint format test coverage publish test-native package release-assets smoke lint-deb clean build-image docker-% act-% ci

help: ## Show this help
	@grep -E '^[a-zA-Z_%-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-14s\033[0m %s\n", $$1, $$2}'

setup: ## One-time developer setup: git hooks and local tools
	git config core.hooksPath .githooks
	dotnet tool restore
	@echo "Hooks installed (pre-commit: lint staged files and scan for secrets; commit-msg: Conventional Commits)."
	@echo "Linters run from Docker images when not installed; for a faster local loop:"
	@echo "  brew install gitleaks actionlint zizmor shellcheck shfmt hadolint yamllint editorconfig-checker && npm i -g markdownlint-cli2"

restore: ## Restore packages (locked mode in CI so the lock files are authoritative)
	dotnet restore $(if $(CI),--locked-mode,)

build: restore ## Build all projects
	dotnet build --no-restore -c $(CONFIG) -p:Version=$(VERSION)

lint: restore ## C# format/style, shell, Dockerfile, YAML, Markdown, editorconfig, workflows, commit messages, secrets
	dotnet format --no-restore --verify-no-changes
	scripts/lint.sh
	@if command -v gitleaks >/dev/null; then gitleaks git --no-banner --redact .; else echo "gitleaks not installed; CI scans the full history"; fi

format: ## Apply formatting fixes
	dotnet format

test: build ## Unit + integration tests with coverage and TRX results
	dotnet test --no-build -c $(CONFIG) \
		--logger "trx" --results-directory $(ARTIFACTS)/test-results \
		--collect:"XPlat Code Coverage" \
		-- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura

coverage: ## Render coverage report from the last test run (HTML + Markdown summary)
	dotnet tool run reportgenerator \
		-reports:"$(ARTIFACTS)/test-results/**/coverage.cobertura.xml" \
		-targetdir:$(ARTIFACTS)/coverage \
		-reporttypes:"Html;MarkdownSummaryGithub;Cobertura"

publish: ## Publish a self-contained NativeAOT binary for $(RID)
	scripts/publish.sh

test-native: ## Run the integration suite against the published native binary
	FILEOPS_BIN="$(CURDIR)/artifacts/publish/$(RID)/fileops" FILEOPS_TEST_LOG_DIR=$(ARTIFACTS)/logs-native \
		dotnet test tests/FileOps.Integration.Tests -c $(CONFIG) --no-restore \
		--logger "trx" --results-directory $(ARTIFACTS)/test-results-native

package: publish ## Build the .deb package from the published binary
	scripts/package.sh

release-assets: package ## Tarball (binary, licence, man page) and SHA256SUMS next to the .deb
	scripts/release-assets.sh

smoke: ## Install the .deb in clean Debian and Ubuntu containers and exercise every command
	scripts/smoke-test.sh

lint-deb: ## Run lintian against the built .deb
	scripts/lint-deb.sh

clean: ## Remove build output
	rm -rf $(ARTIFACTS)

build-image: ## Build the pinned toolchain image used by CI and docker-* targets
	docker build --platform $(DOCKER_PLATFORM) -t $(BUILD_IMAGE) -f docker/Dockerfile.build docker

docker-%: ## Run any target inside the build container, e.g. make docker-test
	$(DOCKER_RUN) make $* VERSION=$(VERSION) RID=$(RID) CONFIG=$(CONFIG)

# Extra arguments for act, e.g. ACT_ARGS='--matrix image:debian:bookworm-slim'. Scoped to ci.yml
# because every workflow has a job called `build`. The container job restores packages from the
# network: act cannot add a cache mount to a job that declares its own container `options`.
ACT_ARGS ?=
act-%: ## Run a ci.yml job locally with act, e.g. make act-build, make act-smoke
	act -W .github/workflows/ci.yml -j $* $(ACT_ARGS)

ci: lint test package lint-deb smoke ## Everything the pipeline runs, in order
