# Makefile for github-token-broker development tasks
#
# Usage:
#   make build      Build the solution
#   make test       Run the test suite
#   make publish    Publish native binaries for one runtime
#   make lint       Run all linters
#   make lint-fix   Auto-fix formatting
#
# A gitignored .env holds a developer's own defaults for this repository, in
# Makefile syntax: NAME=value, unquoted. The command line outranks it.
-include .env

.PHONY: help restore build clean test publish integration-test \
	lint lint-tools lint-fix lint-dotnet lint-dotnet-fix lint-shfmt lint-shfmt-fix \
	lint-shellcheck lint-actions lint-markdown lint-spelling lint-docker lint-control-characters

.DEFAULT_GOAL := help

# Shell scripts to lint (all .sh and .bats files in scripts/ and tests/)
SHELL_SCRIPTS := $(shell find scripts tests \( -name '*.sh' -o -name '*.bats' \) -type f 2>/dev/null)

DOCKERFILES := $(shell find tests -name 'Dockerfile*' -not -name '*.dockerignore' 2>/dev/null)

# The linters ENV=container delegates, in the order a developer wants them.
LINT_TOOL_TARGETS := lint-shfmt lint-shellcheck lint-actions \
	lint-markdown lint-spelling lint-docker lint-control-characters

# Always run here, whatever ENV says, because the image cannot run them. Move a
# target between this and LINT_TOOL_TARGETS as the image changes.
LINT_HOST_ONLY := lint-dotnet

# Exclude specific targets: make lint SKIP=lint-dotnet
SKIP ?=

# Runtime for `make publish`; the release workflow builds every supported RID.
RID ?= $(shell ./scripts/host-rid.sh)

# Where a target runs. `host` is this machine, and is what a CI job wants once
# it is already inside the environment under test. `container` is for a
# developer whose machine is not that environment: the distro images for
# integration-test, the ci-tools image for the linters.
ENV ?= host

# An unrecognized value would fall through to the host branch and report
# success for a run that never happened.
ifeq ($(filter host container,$(ENV)),)
$(error ENV must be host or container, not "$(ENV)")
endif

LINT_COMPOSE = docker compose -p github-token-broker-lint --file scripts/lint/docker-compose.yaml

# Run images live in tests/integration/images.json, which the CI matrix reads
# directly. ENV=container runs the glibc floor alone; ALL=1 runs every image.
# Narrow the cases with CASES="01-socket-mode".
ALL ?=
CASES ?=

SOLUTION := github-token-broker.slnx

help: ## Show available targets
	@echo "Usage: make [target]"
	@echo ""
	@echo "Targets:"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  %-23s %s\n", $$1, $$2}'

restore: ## Restore packages
	@dotnet restore $(SOLUTION)

build: ## Build the solution
	@dotnet build $(SOLUTION)

clean: ## Remove build output
	@rm -rf artifacts

test: ## Run the test suite
	@dotnet test $(SOLUTION)

publish: ## Publish native binaries for RID (default: this machine's)
	@test -n "$(RID)" || { echo "ERROR: No RID; host-rid.sh said why above." >&2; exit 1; }
	@dotnet publish src/KnightOwl.GitHubTokenBroker.Service -c Release -r $(RID)
	@dotnet publish src/KnightOwl.GitHubTokenBroker.Cli -c Release -r $(RID)

integration-test: ## Run the integration suite (ENV=host|container, ALL=1, CASES=)
	@./tests/integration/run.sh --env $(ENV) $(if $(ALL),--all-images) \
		$(foreach case,$(CASES),--case $(case))

lint: $(filter-out $(SKIP),$(LINT_HOST_ONLY)) lint-tools ## Run all linters (ENV=host|container)

# Fix targets always run here: the image is mounted read-only, and a tool that
# rewrites the tree has to reach the tree.
lint-fix: lint-dotnet-fix lint-shfmt-fix ## Auto-fix formatting issues (host only)
	@echo "Note: shellcheck and markdownlint issues must be fixed manually"
	@echo "OK"

lint-dotnet: ## Check C# code formatting (host only)
	@echo "Checking C# formatting (dotnet format)..."
	@dotnet format --verify-no-changes
	@echo "OK"

lint-dotnet-fix: ## Fix C# code formatting
	@echo "Fixing C# formatting (dotnet format)..."
	@dotnet format
	@echo "OK"

lint-shfmt-fix: ## Fix shell script formatting
	@echo "Fixing shell script formatting (shfmt)..."
	@$(if $(SHELL_SCRIPTS),shfmt -w $(SHELL_SCRIPTS),echo "  no shell scripts yet")
	@echo "OK"

ifeq ($(ENV),container)

# One container for the whole set, so `make lint` starts it once. ENV=host
# inside, where the tools are local.
lint-tools:
	@echo "Running the ci-tools linters in container..."
	@$(LINT_COMPOSE) run --rm lint make $@ ENV=host SKIP="$(SKIP)"
	@$(LINT_COMPOSE) down > /dev/null 2>&1

$(LINT_TOOL_TARGETS):
	@echo "Running $@ in ci-tools..."
	@$(LINT_COMPOSE) run --rm lint make $@ ENV=host
	@$(LINT_COMPOSE) down > /dev/null 2>&1

else

lint-tools: $(filter-out $(SKIP),$(LINT_TOOL_TARGETS)) ## Run the linters ENV can containerize

lint-shfmt: ## Check shell script formatting
	@echo "Checking shell script formatting (shfmt)..."
	@$(if $(SHELL_SCRIPTS),shfmt -d $(SHELL_SCRIPTS),echo "  no shell scripts yet")
	@echo "OK"

lint-shellcheck: ## Run shellcheck on shell scripts
	@echo "Running shellcheck..."
	@$(if $(SHELL_SCRIPTS),shellcheck $(SHELL_SCRIPTS),echo "  no shell scripts yet")
	@echo "OK"

lint-actions: ## Validate workflows, and that SHA pins match their tags
	@echo "Checking GitHub Actions workflows (actionlint, validate-action-pins)..."
	@if ls .github/workflows/*.yml >/dev/null 2>&1; then \
		actionlint .github/workflows/*.yml \
		&& validate-action-pins .github/workflows/*.yml; \
	else echo "  no workflows yet"; fi
	@echo "OK"

lint-markdown: ## Check Markdown file formatting
	@echo "Checking Markdown files (markdownlint)..."
	@markdownlint-cli2
	@echo "OK"

lint-spelling: ## Check spelling
	@echo "Checking spelling (cspell)..."
	@cspell lint --no-progress --no-summary
	@echo "OK"

lint-docker: ## Lint the Dockerfiles
	@echo "Linting Dockerfiles (hadolint)..."
	@hadolint $(DOCKERFILES)
	@echo "OK"

lint-control-characters: ## Check for raw control characters in source
	@echo "Checking for raw control characters..."
	@./scripts/check-control-characters.sh
	@echo "OK"

endif
