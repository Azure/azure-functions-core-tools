# Contributing to the Azure Functions CLI

## Contributing to this Repository

### Filing Issues

Filing issues is a great way to contribute to the SDK. Here are some guidelines:

* Include as much detail as you can be about the problem
* Point to a test repository (e.g. hosted on GitHub) that can help reproduce the issue. This works better then trying to describe step by step how to create a repro scenario.
* Github supports markdown, so when filing bugs make sure you check the formatting before clicking submit.

### Submitting Pull Requests

If you don't know what a pull request is read this https://help.github.com/articles/using-pull-requests.

Before we can accept your pull-request you'll need to sign a [Contribution License Agreement (CLA)](http://en.wikipedia.org/wiki/Contributor_License_Agreement). You can sign ours [here](https://cla2.dotnetfoundation.org). However, you don't have to do this up-front. You can simply clone, fork, and submit your pull-request as usual.

When your pull-request is created, we classify it. If the change is trivial, i.e. you just fixed a typo, then the PR is labelled with `cla-not-required`. Otherwise it's classified as `cla-required`. In that case, the system will also also tell you how you can sign the CLA. Once you signed a CLA, the current and all future pull-requests will be labelled as `cla-signed`. Signing the CLA might sound scary but it's actually super simple and can be done in less than a minute.

Before submitting a feature or substantial code contribution please discuss it with the team and ensure it follows the product roadmap. Note that all code submissions will be rigorously reviewed and tested by the Azure Functions Core Tools team, and only those that meet the bar for both quality and design/roadmap appropriateness will be merged into the source.

## Contributing template support for a language stack

Start with the [stack enablement contract](docs/template-engine-integration/README.md#stack-enablement-contract)
and the [Foundation-based stack readiness assessment](docs/func-init-execution/stack-inventory.md#pr-3-stack-readiness).
The inventory includes [Sarah Vu's first bounded work item](docs/func-init-execution/stack-inventory.md#first-work-item-for-sarah).
Keep a contribution to one project/HTTP-item pair for one stack, with its inspection,
content validation, and qualification evidence. Use the normal contributor workflow
and target `feature/v5-templates`. Stack-owned work can start from merged Foundation;
shared CLI integration is reconciled against the stable PR 2 Node/HTTP reference.

## AI-Assisted Development

This repo includes agent instructions (`AGENTS.md`) and developer
guides (`docs/`) that work well with GitHub Copilot and other AI coding agents. To scaffold
new workloads or commands, try prompts like:

- *"Scaffold a new Python workload following the workload checklist in the agent instructions"*
- *"Scaffold a new func deploy command following docs/adding-a-command.md"*

The agent instructions include the full project setup checklist — props files, CI pipelines,
test projects, solution updates, and documentation.

## Running the CLI locally

### Dependencies

Install [.NET SDK](https://www.microsoft.com/net/core) for cross-platform support. The required version is pinned in `global.json`.

### Building the CLI

Build the project from the repository root:

```bash
dotnet build
```

The output will be in `out/bin/Func/debug/`.

### Running the CLI

**Running and debugging:**

- **VS Code** - Press `F5` to run and debug. You'll be prompted for the command args (e.g., `init MyApp -w dotnet`).
- **Visual Studio** - Press `F5` to run and debug. Configure the launch profile to set command line arguments and working directory.

**Command line:**

Run the CLI from source:

```bash
dotnet run --project src/Func -- <command>
```

**Running against a specific function app:**

Option 1 - Run from the function app directory:

```bash
cd myTestFunctionApp
dotnet run --project PATH_TO_CORE_TOOLS_REPO/src/Func -- <command>
```

Option 2 - Use the path argument:

```bash
dotnet run --project src/Func -- start PATH_TO_TEST_APP
```

Option 3 - Add the built executable to your PATH:

```bash
export PATH=$PATH:/path/to/azure-functions-core-tools/out/bin/Func/debug
func <command>
```

### Publishing a Self-Contained Build

To produce a self-contained executable for local testing:

```bash
dotnet publish src/Func/Func.csproj -c Release -r osx-arm64
```

The output is in `out/pub/Func/release_osx-arm64/`. Common RIDs: `osx-arm64`, `osx-x64`, `linux-x64`, `win-x64`.

```bash
./out/pub/Func/release_osx-arm64/func --version
```

### Running Tests

Tests can be run using:

- **Visual Studio Test Explorer** - Use Test Explorer in Visual Studio
- **VS Code** - Using the `.NET Core Test Explorer` extension to discover and run tests
- **Command line** - Use `dotnet test` commands below

#### Unit Tests

```bash
dotnet test test/Func.Tests/Func.Tests.csproj
```

#### CLI Process Smoke Tests

`WorkloadProgramSmokeTests` and `SetupProgramAdvisoryTests` run bounded CLI processes with isolated user, workload, cache, and temporary state.
`CliProcessTestRunner.RunAsync` retains the test-built CLI as its default. `RunExecutableAsync` requires an absolute executable/apphost path and
launches that target directly, without the test assembly's runtime configuration or dependency file. Invalid targets fail without a default-target fallback.

The empty-inventory test also exercises the normal host-platform product output. Its path comes from the resolved CLI project reference through
test assembly metadata; no artifact download, dependency copying, global CLI installation, or separate publish is required.
Explicit-target diagnostics include the executable SHA-256, available file-version metadata, and a deterministic fingerprint of the provisioned
directory's relative paths and file hashes. These identify the tested inputs; a directory fingerprint is not release authentication.
Explicit apphosts use their own runtime/dependency context and a private `DOTNET_BUNDLE_EXTRACT_BASE_DIR` under the test's `process-state` directory.

##### Optional Preview 3 paired replay (Windows x64)

The empty-inventory contract can also qualify the current product candidate against the independently released **5.0.0-preview.3** baseline
(tag `v5.0.0-preview.3`, RID `win-x64`). This is a bounded real-process check for #5353, not a general differential or cross-platform qualification framework.

Provision `func-win-x64.zip` locally before running the test. Its version-pinned distribution provenance is:

```text
https://cdn.functions.azure.com/public/cli/v5/5.0.0-preview.3/func-win-x64.zip
```

The required archive SHA-256 is:

```text
06fdd97d0e17fff6b7da1e2d2701a01e9fac21f63c6c007cfb79004ec3b5a73b
```

**The test never downloads this archive.** Only this empty-inventory scenario reads `FUNC_CLI_TEST_PREVIEW3_ARCHIVE`.
The value must be an absolute path to an existing, caller-supplied archive. The test verifies the fixed digest before extraction or execution,
requires a Windows x64 OS and test process, and accepts only the release ZIP's single, nonempty root-level `func.exe`.
Links, directories, nested apphosts, duplicate entries, extra files, and paths that could escape extraction storage are rejected before any entry is written.
The exact apphost is extracted into fresh test-owned storage under `process-state`, never installed globally or selected through PATH.

From an already-restored checkout, build and run the ordinary focused check in PowerShell:

```powershell
dotnet build .\test\Func.Tests\Func.Tests.csproj --no-restore
dotnet test .\test\Func.Tests\Func.Tests.csproj --no-build --no-restore `
  --filter "FullyQualifiedName~WorkloadProgramSmokeTests.WorkloadListJson_EmptyIsolatedHome_EmitsEmptyArray"
```

Without the setting, only the existing default test-context control and current-product apphost executions run; that is **not** Preview 3 paired qualification.
To opt in, supply the pre-provisioned archive and repeat the same contract:

```powershell
$env:FUNC_CLI_TEST_PREVIEW3_ARCHIVE = 'C:\preprovisioned\func-win-x64.zip'
dotnet test .\test\Func.Tests\Func.Tests.csproj --no-build --no-restore `
  --filter "FullyQualifiedName~WorkloadProgramSmokeTests.WorkloadListJson_EmptyIsolatedHome_EmitsEmptyArray" `
  --logger "console;verbosity=detailed"
```

The setting adds a separately reported Preview 3 baseline execution, with its release identity, archive path, expected and verified archive hashes,
and the existing executable/layout/process diagnostics. The actual baseline/candidate pair is **Preview 3 apphost versus current-product apphost**;
the default test context remains a control, not a released baseline. Each execution has independent writable homes and project input, with the same
sanitized environment policy, unused endpoints, version-cache policy, 60-second execution limit, and empty-inventory assertions.
The check compares the semantic contract, not stdout bytes. A supplied invalid archive, unsupported platform, extraction/layout failure, apphost launch
failure, timeout, or behavioral failure fails the test; there is no silent skip or substitution with another CLI.
