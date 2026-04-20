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

The output will be in `out/bin/Func.Cli/debug/`.

### Running the CLI

**Running and debugging:**

- **VS Code** - Press `F5` to run and debug. You'll be prompted for the command args (e.g., `init MyApp -w dotnet`).
- **Visual Studio** - Press `F5` to run and debug. Configure the launch profile to set command line arguments and working directory.

**Command line:**

Run the CLI from source:

```bash
dotnet run --project src/Func.Cli -- <command>
```

**Running against a specific function app:**

Option 1 - Run from the function app directory:

```bash
cd myTestFunctionApp
dotnet run --project PATH_TO_CORE_TOOLS_REPO/src/Func.Cli -- <command>
```

Option 2 - Use the path argument:

```bash
dotnet run --project src/Func.Cli -- start PATH_TO_TEST_APP
```

Option 3 - Add the built executable to your PATH:

```bash
export PATH=$PATH:/path/to/azure-functions-core-tools/out/bin/Func.Cli/debug
func <command>
```

### Publishing a Self-Contained Build

To produce a self-contained executable for local testing:

```bash
dotnet publish src/Func.Cli/Func.Cli.csproj -c Release -r osx-arm64
```

The output is in `out/pub/Func.Cli/release_osx-arm64/`. Common RIDs: `osx-arm64`, `osx-x64`, `linux-x64`, `win-x64`.

```bash
./out/pub/Func.Cli/release_osx-arm64/func --version
```

### Running Tests

Tests can be run using:

- **Visual Studio Test Explorer** - Use Test Explorer in Visual Studio
- **VS Code** - Using the `.NET Core Test Explorer` extension to discover and run tests
- **Command line** - Use `dotnet test` commands below

#### Unit Tests

```bash
dotnet test test/Func.Cli.Tests/Func.Cli.Tests.csproj
```
