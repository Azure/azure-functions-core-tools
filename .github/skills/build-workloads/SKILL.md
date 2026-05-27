---
name: build-workloads
description: 'Use when a user wants to build every workload in this repo and publish the resulting .nupkgs to a local NuGet feed for end-to-end testing of `func workload install` against a real v3 feed.'
---

# Build Workloads to a Local NuGet Feed

End-to-end loop: pack every `PackageType=FuncCliWorkload` project, stand up a
local NuGet v3 feed ([BaGet](https://github.com/loic-sharma/BaGet) in Docker),
push the `.nupkg`s to it, and print the `func workload install` invocation that
targets the feed.

Use this skill when the user says things like:

- "build all the workloads to a local feed"
- "push the workloads to a local NuGet feed"
- "set up a local feed for `func workload install` testing"

Do **not** use it for shipping packages, for the `infra` feed, or to test the
file-based `func workload install <path-to-nupkg>` flow. That works without a
feed.

## What the skill does

1. Starts (or reuses) the local feed container via `assets/docker-compose.yml`.
   Default endpoint: `http://localhost:5555/v3/index.json`. Default API key:
   `NUGET-SERVER-API-KEY`. Packages persist in a named Docker volume so reruns
   keep history.
2. Packs every workload csproj listed in `Azure.Functions.Cli.slnx` under
   `src/Workloads/**`, into a single output folder. Packs with
   `PackAllRids=true` so RID-specific packages (notably the per-RID Host
   workload, e.g. `Azure.Functions.Cli.Workloads.Host.osx-arm64`) are
   produced alongside the RID-less ones, matching what CI publishes.
3. Pushes each produced `.nupkg` to the feed, skipping duplicates so a rerun
   without bumping `VersionPrefix` is idempotent.
4. Echoes the install command the user runs from another shell:
   ```
   func workload install <name> --source http://localhost:5555/v3/index.json
   ```

## Running it

From the repo root:

```pwsh
pwsh ./.github/skills/build-workloads/scripts/build-workloads.ps1
```

Flags worth knowing (see script header for the full list):

- `-Port <int>` — host port for the local feed (default `5555`).
- `-Configuration <Debug|Release>` — defaults to `Debug`.
- `-VersionSuffix <string>` — appended to the package version so each run
  produces a fresh `1.0.0-<suffix>` package without editing csproj files.
  Defaults to a UTC timestamp; pass an empty string to use the version baked
  into the csproj.
- `-PackOnly` — pack the workloads but don't touch Docker or push.
- `-NoBuild` — push existing `.nupkg`s from the output folder without
  re-packing.
- `-Down` — stop and remove the feed container, then exit.

The script is the source of truth. If a flag is in the script but missing here,
trust the script.

## Verifying the feed

```pwsh
curl http://localhost:5555/v3/index.json
curl 'http://localhost:5555/v3/search?q=PackageType:FuncCliWorkload'
```

A browser at `http://localhost:5555/` shows the feed UI listing every pushed
workload.

## Notes for the agent running this skill

- The feed rejects republishing the same `id@version`. If a workload is already
  pushed and you didn't change `VersionPrefix`, either use `-VersionSuffix`
  (default) or `-Down` first to wipe the volume.
- The script discovers workloads by parsing `Azure.Functions.Cli.slnx` for
  projects under `src/Workloads/`, so new workloads added per the
  `create-workload` skill are picked up automatically. Do **not** hardcode the
  current eleven workload paths.
- This skill is local-loop tooling. Do not wire it into CI, do not commit
  generated `.nupkg`s, and do not change the published feed configuration.
