# Version Management Command

Manage the project version across all configuration files.

## Arguments: $ARGUMENTS

## Instructions

1. Read the current version:

```bash
./scripts/get-version.sh
```

2. **If arguments are empty or "show"**: display the current version and exit

3. **If a version is provided**: validate and update the version

### Validation

The new version MUST match the pattern `X.Y.Z` where X, Y, and Z are
non-negative integers (e.g., `0.1.0`, `1.0.0`, `2.3.14`). If the pattern doesn't
match, show an error and exit.

### Files to Update

One file, one property: `<Version>` in `Directory.Build.props`. Both executables
inherit it, and `AssemblyVersion`, `FileVersion`, and `InformationalVersion`
derive from it.

This is unrelated to the wire contract version, which lives in
`Infrastructure.Contracts.V1` and moves only on a breaking protocol change.

### Verify

```bash
make build test
```

Then confirm the client reports the new version, since it reads
`AssemblyInformationalVersion` at runtime rather than from a constant:

```bash
dotnet run --project src/KnightOwl.GitHubTokenBroker.Cli -- version
```

### Output

After updating and verifying, confirm the changes by showing:

- The old version
- The new version
- List of files updated
- Test results (passed/failed)

## Not yet wired

These steps join this command when the work behind them lands. Add them here at
the same time, so the version never has a second home this command does not know
about.

- `docs/man/man1/github-token.1` and `docs/man/man8/github-token-broker.8` each
  carry a `.Dd` date and a VERSION section to update. `docs/man` is empty today.
