---
name: setup-local-cli
description: 'Use when a contributor wants a ready-to-use local v5 CLI build: publishes func for the host RID to artifacts/func-cli, refreshes the local workloads feed, and prints paste-ready bash and PowerShell snippets that alias f5 to the binary and export the workloads feed env var.'
---

# Set Up the Local v5 CLI for Hand Testing

Contributors iterating on v5 want to drive the CLI from a real shell, not
`dotnet run`. This skill builds everything needed for that loop in one go and
hands back a snippet per shell to paste.

Both the CLI publish and the delegated `build-workloads` invocation run with
`CI=true` so contributors get the same `FileVersion`, workload version suffix,
and warnings-as-errors behaviour that customers see in official builds.

What it produces:

1. A self-contained `func` binary for the host OS/arch at
   `artifacts/func-cli/func` (or `func.exe`).
2. A clean local NuGet feed at `http://localhost:5555/v3/index.json` populated
   with every workload package in the repo (delegates to the `build-workloads`
   skill).
3. Two paste-ready snippets, one per shell. Pick the one for the shell you
   want to run `f5` from:

   Bash / zsh:
   ```
   alias f5="<repo>/artifacts/func-cli/func" && export FUNC_CLI_WORKLOADS_SOURCE="http://localhost:5555/v3/index.json"
   ```

   PowerShell:
   ```
   function f5 { & "<repo>/artifacts/func-cli/func" @args }; $env:FUNC_CLI_WORKLOADS_SOURCE = "http://localhost:5555/v3/index.json"
   ```

Use this skill when the user says things like:

- "set up the CLI so I can test it"
- "build me a local func I can run"
- "give me an f5 alias for the v5 CLI"

## Running it

From the repo root:

```pwsh
pwsh ./.github/skills/setup-local-cli/scripts/setup-local-cli.ps1
```

Flags worth knowing (see script header for the full list):

- `-Configuration <Debug|Release>`, default `Release`.
- `-Rid <rid>`, override the auto-detected host RID
  (`osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`).
- `-Port <int>`, host port for the workloads feed (default `5555`).
- `-SkipWorkloads`, skip the feed teardown and rebuild (the feed is already up
  and current).
- `-SkipCli`, skip the CLI publish (an existing
  `artifacts/func-cli/func` is fine).

The script is the source of truth. If a flag is in the script but missing
here, trust the script.

## Notes for the agent running this skill

- The workloads step delegates to the `build-workloads` skill, including its
  Docker requirement. If Docker isn't available, the script fails there. The
  contributor can rerun with `-SkipWorkloads` to still get a fresh CLI binary.
- The feed is wiped (`build-workloads -Down`) before rebuilding so a stale
  BaGet volume from a different branch doesn't cause duplicate-version push
  errors or surface workloads that no longer exist in this checkout.
- This skill is local-loop tooling. It does not touch CI or the published
  feed; do not wire it into either.
