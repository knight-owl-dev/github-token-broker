# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A local service that mints short-lived, repository-scoped GitHub App
installation tokens, and a client that supplies them to Git and the GitHub CLI.

## Where to read

| For | Read |
| --- | --- |
| what it does, the API, configuration, build and test, assembly layout | `README.md`, which is the source of truth |
| the recipes and their options | `make help` |
| why the integration suite has container machinery | `tests/integration/container/README.md` |

## Rules

- Everything goes through `make`. Add a recipe rather than reaching for a tool
  directly, so a command means the same thing here and in CI.
- `ENV=host|container` picks where a recipe runs and defaults to `host`.
  Anything `make lint` invokes has to exist in `ghcr.io/knight-owl-dev/ci-tools`,
  or it passes here and fails in CI.
- A message a person reads is a sentence: capitalized, ending in a period, and
  worded so a literal config key or path never starts one. A string composed into
  another message stays a lowercase fragment with no terminal period — the
  `out string? error` of a domain `Try*` method, and anything passed to a
  `LoggerMessage` template.
- A wrapped exception keeps its own wording; `DiagnosticReport` prints the chain
  rather than splicing an inner message into ours.
- Interfaces document the contract; implementations use `<inheritdoc />` and
  extend it only where their behavior differs.
- A breaking wire change adds `Infrastructure.Contracts.V2` alongside `V1`, which
  keeps working for one release, then goes. `BrokerProtocol` holds what outlives
  any version: `/health`, the credential header, the request limit.
- **Confirm an assertion can fail before believing it.** A false pass is
  indistinguishable from a real one, so the shapes that produce them are ruled out
  by construction: a case that cannot run reports `[SKIP]` and exits 3 rather than
  asserting nothing, a check for absence goes where the thing could have been
  present, and a count that should not move is paired with one showing it started
  non-zero.

## Gotchas

**Native AOT publishing has no reflective fallback.** What survives a reading of
the code and still breaks the publish: a `JsonSerializer` call taking only
`JsonSerializerOptions`, or a dependency that reflects internally. Analyzers
catch most of it; `make publish` catches the rest.

**Startup cost is a feature.** A DI container scan, a configuration file read, or
a CLI framework is a user-visible regression, because Git spawns the client on
every remote operation.

**The unit suite runs on macOS; the service runs on Linux.** Syscall error codes
are not portable, so behavior derived from one is right on the platform it was
written on and wrong on the other — `connect` to an ordinary Unix path answers
`ENOTSOCK` on Darwin and `ECONNREFUSED` on Linux. Anything branching on an errno
or a `SocketError` needs a case in `tests/integration`, the only thing that runs
the real binaries on a real distro.

**`IDE1006` naming violations cannot be auto-fixed**, so `make lint-fix` leaves
them and the rename has to be deliberate.

**MSBuild reads only the nearest `Directory.Packages.props`**, which is why the
one under `tests/` imports the root policy instead of standing alone.

**American spelling is pinned but leaks.** `cspell.json` sets `en-US`, and the
dictionary's compound matching still admits `colour` and `analyser`. Spelling is
a review concern, not only a lint one.

**Version is hand-set once**, as `<Version>` in `Directory.Build.props`. Both
executables inherit it and ship as one package under one tag.

**`TestKeys` hands out two RSA keys per assembly**, since generating them per
test dominates the run.
