# vnext Release Process (Draft)

## Overview

The `vnext` branch ships the v5 CLI as a set of independently versioned
components: the host, bundles, templates, workers, and language stacks. Each
component is released by pushing a **prefixed git tag** from `vnext`. CI
picks up the tag, packs the matching workload(s), and publishes them to the
appropriate feed.

Versions follow SemVer. Preview releases use `-preview.N` suffixes.

## Tag format

```
<component>/v<semver>
```

| Component         | Tag prefix          | Example                              |
| ----------------- | ------------------- | ------------------------------------ |
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
   - Add the `vnext` label.
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

## Host + worker coordination

If a host tag changes the worker contract, also bump the affected workers
and call it out in the PR description with the command users should run:

```bash
pwsh ./eng/scripts/validate-worker-versions.ps1 -Update -HostVersion <NewHostVersion>
```

## Notes

- Do not retag. If a release is broken, bump the patch (or `-preview.N+1`)
  and tag again.
- Tags are the source of truth for what shipped; release notes describe it.
