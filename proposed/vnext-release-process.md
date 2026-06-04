# vnext Release Process (Draft)

## Overview

The `vnext` branch ships the v5 CLI as a set of independently versioned
components: the host, bundles, templates, workers, and language stacks. Each
component is released by pushing a git tag from `vnext` and then queuing
its release pipeline against that tag. The pipeline packs the matching
workload(s) and publishes them to the appropriate feed.

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
5. **Trigger the release pipeline** for the component from the
   [internal release pipelines folder][release-pipelines] (one pipeline per
   component). Queue the run against the tag you just pushed.
   - Check **Publish to NuGet** if the release is going out to customers.
   - Leave it unchecked to publish only to the internal feed for testing.
6. **Verify CI**: the pipeline packs the workload and publishes the
   `.nupkg` to the selected feed.
7. **Smoke test** the published package via `func workload install` against
   the feed.

[release-pipelines]: https://azfunc.visualstudio.com/internal/_build?definitionScope=%5Cazure%5Cazure-functions-core-tools%5Cvnext%5Crelease

## Multi-component releases

When several components ship together (e.g. a host bump that requires worker
updates), push all tags from the same `vnext` commit. Call out the grouping
in the PR description, and in `release_notes.md`, so consumers know which
versions are compatible.

## Releasing bundles (stable, preview, experimental)

The extension bundles workload ships three channels: `stable`, `preview`,
and `experimental`. Unlike other workloads, bundles is **content-only**:
the channel is just a label and a CDN bundle id, not compiled output. So
for bundles, **the tag is the version**, including the channel.

`vnext` keeps `BundleChannel` at its default in
`src/Workloads/Tools/ExtensionBundles/Directory.Version.props`. The release
pipeline inspects the tag it was queued against and overrides the channel
at pack time:

| Channel        | Example tag                       | Resulting package version |
| -------------- | --------------------------------- | ------------------------- |
| `stable`       | `bundles/v4.35.0`                 | `4.35.0`                  |
| `preview`      | `bundles/v4.35.0-preview.1`       | `4.35.0-preview.1`        |
| `experimental` | `bundles/v4.35.0-experimental.1`  | `4.35.0-experimental.1`   |

The `BundleVersion` (payload version) is shared across channels and
defaults to `$(LatestExtensionBundleVersion)` so the bundle payload always
matches the snapshot the templates workloads were built against.

### Release steps (bundles)

1. **Tag a commit on `vnext`** with the channel-suffixed tag, e.g.
   `bundles/v4.35.0`, `bundles/v4.35.0-preview.1`, or
   `bundles/v4.35.0-experimental.1`. No PR is required for a channel
   change.
2. **Trigger the bundles release pipeline** from the
   [internal release pipelines folder][release-pipelines] against the tag.
   - Check **Publish to NuGet** only when pushing to customers.
   - Leave it unchecked for internal-feed testing.
3. The pipeline parses the tag's prerelease suffix, sets
   `-p:BundleChannel=preview` (or `experimental`) for the pack, and
   publishes the workload with the matching version.
4. **Smoke test** with `func workload install bundles --version <tag-version>`.

> Increment prerelease tags per release (`-preview.1`, `-preview.2`, ...).
> Do not reuse a prerelease tag.

> Tracking work to support this: props-based channel conditions in
> `Directory.Version.props` and the tag-parsing logic in the bundles
> release pipeline.

## Notes

- Do not retag. If a release is broken, bump the patch (or `-preview.N+1`)
  and tag again.
- Tags are the source of truth for what shipped; release notes describe it.
