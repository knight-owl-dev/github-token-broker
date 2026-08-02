# Containers for the integration suite

Everything here produces a Linux environment for the cases to run in. Running
them is `../run-cases.sh`, whichever environment it lands in.

Two things need such an environment.

**A developer whose machine is not the platform under test.** `run.sh --env
container` builds a run image per distro and runs the cases inside. On macOS
that also means borrowing a Linux host to publish, since Native AOT cannot cross
that boundary — see `Dockerfile.publish`.

**CI.** GitHub's `container:` key supplies the distro, so the job is already
inside one of these images and does a plain host run. It builds nothing here,
taking only `prepare.sh` and `images.json`.

| File | |
| --- | --- |
| `images.json` | the distros, pinned by digest; one is the glibc floor |
| `prepare.sh` | packages, the GitHub CLI, and the accounts the socket cases need |
| `Dockerfile` | the run image: a distro, `prepare.sh`, and the two executables |
| `Dockerfile.publish` | a Linux host for `make publish`, for a machine that is not one |
| `build-image.sh` | resolves the pinned SDK image |

## Keeping the pins current

A digest means an upstream rebuild cannot fail a build that has nothing to do
with the change under review. It also means a pin goes stale quietly, so refresh
it when a distro moves:

```sh
docker buildx imagetools inspect ubuntu:24.04
```

The SDK image is pinned in `global.json`, beside the version it provides,
because the two have to move together — `build-image.sh` stops a run where they
have drifted.
