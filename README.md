# github-token-broker

A local service that mints short-lived, repository-scoped GitHub App
installation tokens, and a client that hands them to Git and the GitHub CLI.

Automation that needs to push branches and open pull requests usually ends up
holding a personal access token: long-lived, broadly scoped, and attributed to
a human. This replaces that with a GitHub App installation token — minted on
demand, restricted to one repository, expiring within the hour, and attributed
to the App. Only the service reads the private key; the process doing the work
never sees it.

Two executables:

| Executable | Role |
| --- | --- |
| `github-token-broker` | holds the private key, signs App JWTs, calls GitHub, enforces the allowlist, caches tokens |
| `github-token` | Git credential helper, authenticated `gh` launcher, authorization check |

## Trust boundary

Only the service reads the private key, signs a JWT, or calls GitHub. The
client asks for one repository and receives a token; it never learns the App
identity, the key path, or what else is allowlisted.

That separates duties. It is **not** an isolation boundary: anything that can
reach the service endpoint can obtain a token for any allowlisted repository.
Restricting who can reach it is the deployment's job, and depends on the
transport.

## Install

Artifacts are self-contained, so no .NET runtime is required on the host.

```sh
# Homebrew and apt packaging are planned; until then, build from source.
git clone https://github.com/knight-owl-dev/github-token-broker
cd github-token-broker
make publish RID=osx-arm64
```

## Configure

The service reads one JSON file, given as an absolute path:

```sh
github-token-broker --config /etc/github-token-broker/config.json
```

It names the App to mint as, where to accept requests, and which repositories
may be minted for. Nothing else configures the service, and no client can
influence any of it. An unrecognized member anywhere in the document fails
startup, so a misspelled security-sensitive key is refused rather than ignored.

| Setting | | Meaning |
| --- | --- | --- |
| `app_id` | required | The App's numeric identity, from its registration. Signed into every JWT as `iss`. |
| `installation_id` | required | Which installation of that App to mint against, the number in the installation's settings URL. An allowlist entry may name a different one; this is what the rest take. |
| `private_key_path` | required | Absolute path to the App's PEM private key. Re-read on every mint, so replacing the file rotates the key with no restart. |
| `listen` | required | Where requests are accepted. See the three shapes below. |
| `repositories` | required | The allowlist, and the permission ceiling for each entry. |
| `github_host` | `github.com` | The only accepted value today; the setting exists so a second host is a configuration change rather than a code change. |
| `api_url` | `https://api.github.com` | Where the GitHub API lives. It exists to point the service at a fake in a test, so plaintext is accepted against loopback and nowhere else. Not GitHub Enterprise support. |
| `token_refresh_margin_seconds` | `300` | How much life a cached token must have left to be reused. Between 30 and 1800. |

### The repositories allowlist

`repositories` is the whole of what may be minted. A client names one repository
and receives a token for it. It cannot reach a repository absent from this list,
and cannot influence the permissions attached to one that is present.

```json
{
  "repositories": {
    "example-owner/example-repo": {
      "permissions": {
        "contents": "write",
        "pull_requests": "write",
        "actions": "read"
      }
    },
    "example-owner/another-repo": {
      "permissions": { "contents": "read" }
    }
  }
}
```

Each key names a repository in `OWNER/REPOSITORY` form, spelled the way GitHub
spells it and without a `.git` suffix. Matching ignores case, so two entries
differing only in case are refused. At least one repository is required, and
each entry declares at least one permission.

A repository absent from the list is refused with the same status and message as
every other refusal, so a caller cannot use error text to discover what is here.

#### Installations

An entry may carry its own `installation_id`, which is how a repository outside
the App owner's account is served: its owner installs the same App, and that
installation's number goes here. The top-level value serves every entry that
omits one.

```json
{
  "installation_id": 789012,
  "repositories": {
    "example-owner/example-repo": {
      "permissions": { "contents": "write" }
    },
    "other-owner/their-repo": {
      "installation_id": 345678,
      "permissions": { "contents": "write", "pull_requests": "write" }
    }
  }
}
```

One App and one private key throughout: an installation is an address, and the
key is the credential that addresses it. A rejected App JWT therefore still
discards every cached grant.

Whether an installation covers the repository listed against it is settled at
the first mint, the broker reaching GitHub for nothing else. A mismatch answers
as a missing installation — see [Token minting](#token-minting).

#### Permissions are a ceiling

The map under a repository is exactly what the service asks GitHub for, and a
grant exceeding it is refused rather than returned. A token therefore never
carries more authority than is written here, even where the installation would
allow more.

Supported permissions are `metadata`, `contents`, `pull_requests`, `issues`,
`actions`, `checks`, and `statuses`, each at `read` or `write`. GitHub reports
`metadata: read` on every installation token whether or not it was asked for, so
that one grant is tolerated in a response even when configuration omits it.

`workflows` is refused at any level, and says so distinctly from an unsupported
name: authority over workflow files is outside this design rather than merely
unset.

For how much to grant, see [Recommended GitHub App
setup](#recommended-github-app-setup). Narrowing an entry takes effect on the
next mint rather than the next restart — see [Cache](#cache).

### Listening on a Unix socket

The recommended shape, and the one to reach for unless a caller cannot see the
host filesystem. Authorization is filesystem access, so there is no shared
secret to distribute, rotate, or leak.

```json
{
  "app_id": 123456,
  "installation_id": 789012,
  "private_key_path": "/etc/github-token-broker/app.pem",
  "listen": {
    "unix_socket": {
      "path": "/run/github-token-broker/broker.sock",
      "mode": "0600"
    }
  },
  "repositories": {
    "example-owner/example-repo": {
      "permissions": { "contents": "write", "pull_requests": "write" }
    }
  }
}
```

| Setting | | Meaning |
| --- | --- | --- |
| `unix_socket.path` | required | Absolute path to bind. Within the platform's `sockaddr_un` limit — 104 bytes on macOS, 108 on Linux. |
| `unix_socket.mode` | `0600` | Three octal digits. Connecting needs write permission, so this is who may mint. |

Anyone who can write the socket can mint for the whole allowlist, so a mode
granting write to others is refused. Sharing with a group is `"0660"` plus group
ownership from the service unit. The socket's directory is the outer half of the
same control — see [Transports](#transports).

### Reaching it from a container

Bind-mount the socket. One socket serves any number of containers at once, and
no shared secret is involved:

```sh
docker run \
  -v /run/github-token-broker/broker.sock:/broker.sock \
  -e GITHUB_TOKEN_BROKER_ENDPOINT=unix:///broker.sock \
  my-bot-image
```

The container's process needs write permission on the socket. On Linux the
mount keeps the host's owner, group, and mode, so a container running as another
user needs `--user` matching the owner or `--group-add` against a `0660` socket.
Docker Desktop re-owns the socket to `root` inside the container, so a root
container connects with no further arrangement.

There is no network transport. A caller that cannot see the host filesystem has
no route to the service.

`listen` must configure `unix_socket`. Each transport is one object rather than
a flat set of prefixed keys, so a second one arrives beside it instead of
alongside its members.

## Use it from Git

Configure the helper and path-aware credential matching. Without the latter,
one repository's token would satisfy a request for another:

```sh
git config credential.helper '!github-token credential'
git config credential.https://github.com.useHttpPath true
export GITHUB_TOKEN_BROKER_ENDPOINT=unix:///run/github-token-broker/broker.sock
```

The leading `!` is load-bearing. Git resolves a bare name as
`git-credential-NAME`, and runs the value as a command only when it starts with
`!` or names an absolute path.

Use HTTPS remotes with no embedded credentials. Clone, fetch, and push then
work without further ceremony:

```sh
git clone https://github.com/example-owner/example-repo
```

A repository the service does not serve makes the helper decline, and Git
continues unauthenticated. See [credential helper
behavior](#credential-helper-behavior).

## Use it from the GitHub CLI

```sh
github-token gh example-owner/example-repo -- pr create --fill
github-token gh example-owner/example-repo -- run list
```

The repository is named explicitly rather than inferred from the working
directory, and `--` separates the arguments. The token is set as `GH_TOKEN` in
the child process only, `GITHUB_TOKEN` is removed so it cannot take precedence,
and `GH_REPO` pins the child to the repository the token is scoped to.
Arguments are passed individually, never through a shell. Nothing is written to
`hosts.yml` and `gh auth login` is never run, so no durable credential is left
behind.

## Client configuration

| Variable | Purpose |
| --- | --- |
| `GITHUB_TOKEN_BROKER_ENDPOINT` | required; `unix:///path/to/broker.sock` |
| `GITHUB_TOKEN_BROKER_GH` | explicit path to the real GitHub CLI |

### Commands

```text
github-token credential [get|store|erase]
github-token gh OWNER/REPOSITORY -- GH_ARGUMENT...
github-token check OWNER/REPOSITORY
github-token version
```

`check` confirms the service is reachable and will serve a repository, without
minting anything:

```console
$ github-token check example-owner/example-repo
example-owner/example-repo actions:read;checks:read;contents:write;pull_requests:write;statuses:read
```

### Exit statuses

| Status | Meaning |
| --- | --- |
| 0 | success |
| 64 | wrong command line or environment |
| 69 | the service could not be reached |
| 70 | the service answered but could not serve the request; its log holds the reason |
| 75 | the service was reached but GitHub was not, so retrying can help |
| 77 | the service refused the repository |
| 78 | the service and GitHub disagree about what it may mint; its log says which |

`github-token gh` instead returns the child's status verbatim, so a value here
only ever reflects a failure before the child started.

### Credential helper behavior

The helper answers `get` with:

```text
username=x-access-token
password=INSTALLATION_TOKEN
```

`store` and `erase` are answered without minting: a token lives for an hour and
is never persisted.

Declining and failing are deliberately different. Returning nothing makes Git
proceed unauthenticated, which is right for a repository the service does not
serve. So a refused repository, and a host or protocol the helper does not
serve, both decline silently. A `github.com` HTTPS request that cannot name a
repository is reported, because that means `useHttpPath` is missing. A service
that cannot be reached fails loudly rather than degrading a private repository
into a confusing anonymous failure.

## Service API

| Route | Behavior |
| --- | --- |
| `GET /health` | liveness and allowlist size; never mints |
| `POST /v1/token` | returns a cached or freshly minted token |
| `POST /v1/check` | confirms authorization without minting |

Both `POST` routes take `{"host": "github.com", "repository":
"OWNER/REPOSITORY"}`. `/v1/token` answers `{"token": "...", "expires_at":
"..."}`.

Every refusal is one status and one message, so error text cannot be used to
probe the allowlist. The precise reason goes to the log, which never contains a
token, a JWT, or key material.

Requests are bounded: `application/json` only, at most 4096 bytes, JSON depth
8, and only the declared method per route.

## Transports

One client abstraction covers both endpoint forms, so behavior above the
endpoint does not depend on which is selected:

```text
unix:///absolute/path/to/broker.sock
http://host:port
```

Which of them to configure, and what each costs, is under
[Configure](#configure).

The mode is applied by the broker rather than left to the process umask, since
connecting needs write permission on the socket file and a umask would decide
who may mint. Its directory is the outer half of that control, and covers the
moment between binding and the mode being applied: the broker creates it `0700`
when absent, and refuses to start when it already exists and is writable by
anyone but its owner — unless the sticky bit is set, which is what makes a
shared directory like `/tmp` safe.

Binding fails if the socket path is occupied, and nothing is removed to free it:
a socket left by an unclean shutdown reads the same as a mistyped path naming a
real file. Clearing it belongs to the operator or the service manager — systemd's
`RuntimeDirectory=` recreates the directory empty on every start.

```console
$ github-token-broker --config /etc/github-token-broker/config.json
configuration error: another process is already listening on /run/github-token-broker/broker.sock
```

## Token minting

An RS256 JWT is signed with `iat` backdated 60 seconds and a nine-minute
lifetime, inside GitHub's ten-minute ceiling with room for clock skew. It stays
in memory and is never returned through the API.

The service then calls `POST /app/installations/{id}/access_tokens` with
exactly one repository name and exactly the configured permission map. The
repository is sent without its owner, since the installation implies it.

A response is refused unless it proves the narrowing held: a usable token, an
expiry in the future, `repository_selection` of `selected`, exactly one
repository matching the request, and no permission beyond the ceiling. Token
values are treated as opaque — no prefix, format, or length is assumed. GitHub
reports `metadata: read` on every installation token whether or not it was
requested, so that one grant is tolerated.

Failures are classified, because the class decides what happens next and what
an operator should check:

| Class | Cause | Effect |
| --- | --- | --- |
| `AppUnauthorized` | `401`: wrong key, clock skew, rotated key | the whole cache is dropped, and the client is told to go and look, since only clock skew clears on its own |
| `InstallationForbidden` | `403`: installation suspended | the client is told to fix the installation |
| `InstallationOrRepositoryMissing` | `404`: installation or repository gone | the client is told to fix the installation |
| `PermissionDrift` | `422`: configuration asks for more than the App grants | reported distinctly |
| `UntrustworthyResponse` | the response could not be proven narrow | reported distinctly |
| `PrivateKeyUnusable` | the key was readable at startup and is not now | reported distinctly |
| `UnrecognizedStatus` | a status this broker has no reading for | reported distinctly |
| `Unavailable` | transport failure, or a `500`, `502`, `503`, or `504` | attempted again inside the request, then retried on the next one |
| `TimedOut` | GitHub did not answer within 15 seconds | retried on the next request |
| `RateLimited` | `429`, or `403` carrying a rate-limit header | retried on the next request |

A failed mint always drops whatever that grant had cached, because a token is
only minted once the cached one is no longer fresh enough to serve.

`Unavailable`, `TimedOut`, and `RateLimited` answer `503`, being the classes that
clear without anyone acting. Every other class tells a client to go and look.

### Key rotation

The private key is re-read on every mint, so replacing the file rotates the key
with no restart.

To rotate: generate a new key in the App settings, replace the file, confirm a
mint succeeds, then delete the old key at GitHub. A `401` clears the whole
cache, since every cached grant was minted with the credential GitHub just
rejected.

## Cache

In memory only, keyed by normalized repository, its installation, and its
effective permissions, so a configuration change cannot silently reuse a token
minted under the previous, broader grant.

A token is reused only while more than the refresh margin remains. There is no
refresh loop: a token is minted by the first request that finds none fresh
enough. Concurrent misses for one key are coalesced onto a single mint, and no
lock is held while that mint is in flight. A caller that gives up abandons its
own wait without cancelling a mint others are waiting on; the mint is bounded
by its own timeout instead. A failed mint clears its entry, so it fails only
the callers already waiting on it and never poisons a later request.

Configuration is read once at startup, so changing it requires a restart.

## Recommended GitHub App setup

Register a private App owned by the account that owns the target repositories.
Leave webhooks inactive, request no user authorization, and install it on
**only select repositories**.

Grant no more than the work requires — `metadata: read`, `contents: write`,
`pull_requests: write`, and read on `actions`, `checks`, and `statuses` covers
branch pushes and pull requests. Leave workflow, administration, secrets, and
environment permissions off.

Protect each default branch with a ruleset that requires pull requests, blocks
force pushes and deletion, and does **not** list the App as a bypass. App
permissions govern what the API allows; branch rules govern what may reach a
protected branch. Both layers are needed.

Keep the private key outside any build context, image layer, repository, or
workspace. It can mint tokens for every installation of the App, which makes it
more valuable than any token it produces.

### Repositories owned by someone else

An App installed on one account grants no access to another's repositories, even
when the App owner is a collaborator there; only that owner can install it. Two
setups follow.

**Ask the owner to install the App.** They install it on the repositories they
are willing to serve and send back the installation's number, which goes on
those allowlist entries — see [Installations](#installations). Nothing is
copied, and pull requests work as they normally do.

**Fork into the account that owns the App.** Where an owner will not install it,
fork and work in the copy. Keep pull requests inside the fork, targeting its own
default branch: creating one requires `pull_requests: write` on the **base**
repository, so a token scoped to a fork cannot open a pull request against an
upstream. Review and merge in the fork; forwarding the change upstream
afterwards needs nothing from this service.

Forking is not always available either. Organizations disallow forking of
private and internal repositories by default, and an enterprise policy can
withhold it outright.

## Build and test

```sh
make build
make test
make lint                       # ENV=container runs the pinned tool image
make lint-fix                   # apply what can be fixed automatically
make integration-test           # ENV=container for the Linux matrix
make publish                    # this machine; RID= names another runtime
```

The SDK is pinned in `global.json`, package versions in
`Directory.Packages.props` with per-project lock files, and build settings in
`Directory.Build.props`. XML documentation is generated with warnings as
errors, so an undocumented public member fails the build. All output lands in
the untracked `artifacts/` directory.

Tests run offline with no real GitHub, no secret, and no sleep: an injected
HTTP handler stands in for GitHub, RSA keys are generated in memory, and a
hand-advanced clock drives every expiry assertion.

They also run in parallel. xUnit v3 runs on Microsoft.Testing.Platform, so each
test project is a standalone executable that starts in milliseconds, and test
classes within an assembly run concurrently. Nothing shares mutable state: temp
directories, sockets, and ephemeral ports are per test, and the environment is
injected rather than mutated. Each test project is also a standalone executable
under `artifacts/bin`, which can be run directly to skip MSBuild entirely.

The integration suite runs the published binaries instead of the assemblies, on
this host or across the Linux images pinned in
`tests/integration/container/images.json`. Behavior that differs by platform,
such as a syscall error code, is only proven there.

Artifacts are Native AOT binaries for `osx-arm64`, `linux-x64`, and
`linux-arm64`: real executables with no runtime to install, roughly 13 MB for
the service and 5.5 MB for the client. The client starts in about four
milliseconds, which is the figure that matters, because Git spawns the
credential helper on every remote operation.

Ahead-of-time compilation has no reflective fallback, so all JSON is
source-generated. Adding a reflection-based serializer or a reflection-heavy
dependency breaks the publish rather than degrading quietly.

### Layout

Three assemblies, two executables. `.Service` produces `github-token-broker`
and `.Cli` produces `github-token`; `.Core` carries what both share, under two
namespaces. `KnightOwl.GitHubTokenBroker.Domain` holds pure value objects and
the `RepositoryAllowlist` aggregate that owns the "exactly one allowlisted
repository, at no more than its configured ceiling" invariant.
`KnightOwl.GitHubTokenBroker.Infrastructure` holds adapters: the wire contract,
GitHub's models, configuration parsing, endpoint addressing.

`.Service` and `.Cli` each then have their own `Domain`, `Application`, and
`Infrastructure`, so service-only concerns such as token issuance cannot leak
into the client.

## Not yet done

- Homebrew and apt packaging, and a released artifact to install from.
- Service definitions for `launchd` and `systemd`, including socket ownership
  and mode.
- Hosts other than `github.com`.
