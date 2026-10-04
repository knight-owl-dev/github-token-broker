# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A local service that mints short-lived, repository-scoped GitHub App
installation tokens, and a client that supplies them to Git and the GitHub CLI.

## Where to read

| For | Read |
| --- | --- |
| what it does, the API, configuration, build and test, assembly layout | `README.md`, which is the source of truth |
| the commands, settings, and exit statuses | `docs/man/`, via `make man NAME=github-token` |
| the recipes and their options | `make help` |
| why the integration suite has container machinery | `tests/integration/container/README.md` |

## Rules

- Everything goes through `make`. Add a recipe rather than reaching for a tool
  directly, so a command means the same thing here and in CI.
- `ENV=host|container` picks where a recipe runs and defaults to `host`.
  Anything `make lint` invokes has to exist in `ghcr.io/knight-owl-dev/ci-tools`,
  or it passes here and fails in CI.
- In the binaries, a message a person reads is a sentence: capitalized, ending in
  a period, and worded so a literal config key or path never starts one. **Every
  exception message is one**, whoever ends up reading it — a thrower does not get
  to assume its consumer. A string composed into another message stays a
  lowercase fragment with no terminal period — the `out string? error` of a
  domain `Try*` method, and a value passed as a `LoggerMessage` argument.
- A wrapped exception keeps its own wording; `DiagnosticReport` prints the chain
  rather than splicing an inner message into ours.
- Tooling — the Makefile, scripts, workflows, and the integration suite — prints
  lines: no terminal punctuation, an `ERROR:` or `WARN:` label on stderr with the
  text after it lowercase, continuations indented two spaces.
- The man pages own the reference material — commands, settings, exit statuses.
  Prose links to `github-token(1)`, `github-token-broker-config(5)`, or
  `github-token-broker(8)` rather than restating them. `README.md` still carries
  its own copy until it is split by reader.
- Interfaces document the contract; implementations use `<inheritdoc />` and
  extend it only where their behavior differs.
- A breaking wire change adds `Infrastructure.Contracts.V2` alongside `V1`, which
  keeps working for one release, then goes. `BrokerProtocol` holds what outlives
  any version: `/health` and the request limit.
- A configuration a release accepts, every later release accepts. An upgrade
  restarts the broker on the operator's file, and one it now refuses exits `78`,
  which the unit leaves down on every upgraded host. Deprecate a setting; never
  reject one that used to work.
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

**`ENV=host` inherits the terminal's environment**, so an assertion that a
variable is absent decides by who ran it: `GIT_TERMINAL_PROMPT=0` closes the
terminal prompt, and an askpass helper answers the same question anyway.
`run-cases.sh` unsets what a case asserts about, being the one file both
environments enter through. `ENV=container` starts clean regardless.

**`IDE1006` naming violations cannot be auto-fixed**, so `make lint-fix` leaves
them and the rename has to be deliberate.

**A `switch` over an enum omits its default arm on purpose**, so `CS8509` fails
the build on a member added without a case. Adding `_ =>` silences it. See
`Directory.Build.props` for the suppression that makes this compile.

**MSBuild reads only the nearest `Directory.Packages.props`**, which is why the
one under `tests/` imports the root policy instead of standing alone.

**American spelling is pinned but leaks.** `cspell.json` sets `en-US`, and the
dictionary's compound matching still admits `colour` and `analyser`. Spelling is
a review concern, not only a lint one.

**The version moves only by release PR.** It lives once, as `<Version>` in
`Directory.Build.props`; `make release` stamps it on a `release/vX.Y.Z` branch,
and merging that PR tags it. Both executables inherit it and ship as one package
under one tag. The man pages carry `@VERSION@`, which `make man-build` replaces
into `artifacts/man`, and the release PR dates their `.Dd`.

**An mdoc macro name eats an ordinary word.** `.Ss An occupied path` renders as
"occupied path", because `An` is the AUTHOR macro; `No`, `In`, `At`, and `St`
bite the same way. `mandoc` reports nothing, so it only shows up in the render.
Escape the leading word with `\&`. Blank lines are refused too — `.\"` is what
separates sections in the source.

**`TestKeys` hands out two RSA keys per assembly**, since generating them per
test dominates the run.
