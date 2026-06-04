# vnext Release Process (Draft)

## Overview

The `vnext` branch ships the v5 CLI as a set of independently versioned
components: the host, bundles, templates, workers, and language stacks. Each
component is released by pushing a **prefixed git tag** from `vnext`. CI
picks up the tag, packs the matching workload(s), and publishes them to the
appropriate feed.

Versions follow SemVer. Preview releases use `-preview.N` suffixes.

## Tag format

Every tag's `<semver>` comes from the `VersionPrefix` (+ optional
`VersionSuffix`) in that component's `Directory.Version.props`. The tag is
the signal; the props file is the source of truth.

- **CLI** has no prefix: `v<version>` (e.g. `v5.0.0`). Version lives in
  `src/Directory.Version.props`.
- **Workloads** are prefixed by their alias: `<component>/v<version>`.
  Version lives in `src/Workloads/<area>/<Name>/Directory.Version.props`.

| Component         | Tag prefix          | Example                              |
| ----------------- | ------------------- | ------------------------------------ |
| CLI (`func`)      | _(none)_            | `v5.0.0`                             |
| Host              | `host/`             | `host/v4.1048.200-preview.1`         |
| Bundles           | `bundles/`          | `bundles/v4.35.0`                    |
| Node templates    | `node-templates/`   | `node-templates/v1.0.1`              |
| Python templates  | `python-templates/` | `python-templates/v1.0.1`            |
| .NET templates    | `dotnet-templates/` | `dotnet-templates/v1.0.1`            |
| Node worker       | `node-worker/`      | `node-worker/v3.13.0-preview.1`      |
| Python worker     | `python-worker/`    | `python-worker/v4.43.0-preview.1`    |
| Go worker         | `go-worker/`        | `go-worker/v1.0.0-preview.1`         |
| Node stack        | `node-stack/`       | `node-stack/v1.0.0-preview.1`        |
| Python stack      | `python-stack/`     | `python-stack/v1.0.0-preview.1`      |
| .NET stack        | `dotnet-stack/`     | `dotnet-stack/v1.0.0-preview.1`      |
| Go stack          | `go-stack/`         | `go-stack/v1.0.0-preview.1`          |

Rules:
- Always prefix the version with `v`.
- The version in the tag must match `Directory.Version.props` on the tagged
  commit.
- Use `-preview.N` for unstable releases, no suffix for GA.
- One component per tag. Bumping multiple components means multiple tags.

## SemVer guidance

- **MAJOR** — breaking changes (host contract, worker protocol, template API).
- **MINOR** — backward-compatible features.
- **PATCH** — bug fixes and hotfixes.

## Release steps

1. **Pick the component(s)** to release and decide the new version per SemVer.
2. **Open a release-prep PR against `vnext`** that:
   - Bumps the component's version in its props file (e.g.
     `src/Workloads/<area>/<Name>/Directory.Version.props`).
   - Updates `release_notes.md` with the new version and a short changelog.
3. **Merge the PR** into `vnext` once CI is green and reviews are in.
4. **Tag the merge commit** on `vnext`:
   ```bash
   git checkout vnext && git pull
   git tag <component>/v<semver>
   git push origin <component>/v<semver>
   ```
5. **Verify CI**: the tag-triggered pipeline packs the workload and publishes
   the `.nupkg` to the configured feed.
6. **Smoke test** the published package via `func workload install` against
   the feed.

## Multi-component releases

When several components ship together (e.g. a host bump that requires worker
updates), push all tags from the same `vnext` commit. Call out the grouping
in the PR description, and in `release_notes.md`, so consumers know which
versions are compatible.

## Releasing preview and experimental bundles

The extension bundles workload ships three channels: `stable`, `preview`,
and `experimental`. The channel is selected by the `BundleChannel` property
in `src/Workloads/Tools/ExtensionBundles/Directory.Version.props`, which
drives both the CDN bundle id pulled at pack time and the prerelease label
on the workload package:

| Channel        | Example package version | Example tag                       |
| -------------- | ----------------------- | --------------------------------- |
| `stable`       | `4.35.0`                | `bundles/v4.35.0`                 |
| `preview`      | `4.35.0-preview.1`      | `bundles/v4.35.0-preview.1`       |
| `experimental` | `4.35.0-experimental.1` | `bundles/v4.35.0-experimental.1`  |

The `BundleVersion` (payload version) is shared across all three channels
and defaults to `$(LatestExtensionBundleVersion)` so the bundle payload
matches the snapshot the templates workloads were built against.

### Release steps (preview / experimental)

Two options below; the team will pick one in review.

#### Option A: channel checked into `vnext` at tag time

Treat `BundleChannel` like every other workload's version: whatever is in
`Directory.Version.props` on the tagged commit is what ships.

1. **Set the channel** in
   `src/Workloads/Tools/ExtensionBundles/Directory.Version.props`:
   ```xml
   <BundleChannel>preview</BundleChannel>   <!-- or experimental -->
   ```
   Override `BundleVersion` only if the payload version needs to diverge
   from the default.
2. **Open a release-prep PR against `vnext`** with the channel change and a
   `release_notes.md` entry calling out the channel.
3. **Merge and tag** the merge commit using the matching prerelease tag,
   e.g. `bundles/v4.35.0-preview.1` or `bundles/v4.35.0-experimental.1`.
4. **CI** packs the workload with the prerelease label and publishes it to
   the same feed; consumers opt in by installing the prerelease version
   (`func workload install bundles --version 4.35.0-preview.1`).
5. **Promoting to stable**: open a follow-up PR that flips `BundleChannel`
   back to `stable`, then tag `bundles/v<version>` (no suffix). The
   workload is repackaged from the same payload without the prerelease
   label.

Pros: consistent with all other workloads, reproducible from the tag
alone. Cons: every preview release needs a flip-to-preview PR and a
flip-back PR, which churns `vnext` history and leaves a window where HEAD
disagrees with intent.

#### Option B: `vnext` stays on `stable`, tag drives the channel

Bundles is a content-only workload; the channel is just a label and a CDN
id, not compiled output. Keep `BundleChannel` set to `stable` in `vnext`
permanently and let the tag select the channel at pack time.

1. **No props change required.** `vnext` HEAD always reads `stable`.
2. **Tag the commit** to release from, using the channel-suffixed tag:
   - `bundles/v4.35.0` (stable)
   - `bundles/v4.35.0-preview.1` (preview)
   - `bundles/v4.35.0-experimental.1` (experimental)
3. **CI** parses the tag's prerelease suffix and packs with
   `-p:BundleChannel=preview` (or `experimental`), overriding the default.
   The published package version still matches the tag.
4. **Promoting to stable**: tag `bundles/v<version>` (no suffix) on the
   same or a later commit. No PR needed.

Pros: no flip-flop PRs, tag is the single signal, prerelease workflow
behaves like any other prereleased package. Cons: reproducing a build
requires both the tag and the pipeline's tag-parsing logic, not just the
tagged commit. Bundles becomes the one workload that derives a build
input from the tag instead of from props.

> Preview and experimental tags should be incremented per release
> (`-preview.1`, `-preview.2`, ...). Do not reuse a prerelease tag.

## Notes

- Do not retag. If a release is broken, bump the patch (or `-preview.N+1`)
  and tag again.
- Tags are the source of truth for what shipped; release notes describe it.
