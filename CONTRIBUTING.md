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

## Running the CLI locally

### Dependencies

Install [.NET SDK](https://www.microsoft.com/net/core) for cross-platform support.

### Building the CLI

Build the project from the repository root:

```bash
dotnet build
```

The output will be in `out/bin/Azure.Functions.Cli/debug/`.

### Running the CLI

**Running and debugging:**

- **VS Code** - Press `F5` to run and debug. You'll be prompted for:
  1. The command (e.g., `start`, `new`, `init`)
  2. Optional `--script-root` path to your test function app

- **Visual Studio** - Press `F5` to run and debug. Configure the launch profile to set:
  1. The command via `command line arguments`
  2. Path to your test function app path via `working directory`

**Command line:**

Run the CLI from source:

```bash
dotnet run --project src/Cli/func -- <command>
```

**Running against a specific function app:**

Option 1 - Run from the function app directory:

```bash
cd myTestFunctionApp
dotnet run --project PATH_TO_CORE_TOOLS_REPO/src/Cli/func -- <command>
```

Option 2 - Use the `--script-root` parameter:

```bash
dotnet run --project src/Cli/func -- <command> --script-root PATH_TO_TEST_APP
```

Option 3 - Add the built executable to your PATH:

```bash
export PATH=$PATH:/path/to/azure-functions-core-tools/out/bin/Azure.Functions.Cli/debug
func <command>
```

### Running Tests

Tests can be run using:

- **Visual Studio Test Explorer** - Use Test Explorer in Visual Studio
- **VS Code** - Using the `.NET Core Test Explorer` extension to discover and run tests
- **Command line** - Use `dotnet test` commands below

#### Unit Tests

```bash
dotnet test test/Cli/Func.UnitTests/Azure.Functions.Cli.UnitTests.csproj
```

#### E2E Tests

E2E tests require Azure Storage emulator (Azurite). 

**Option 1 - Using the provided script:**

```bash
./eng/scripts/start-emulators.ps1
```

**Option 2 - Manual setup:**
- Download [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
- Start Azurite before running tests:
  ```bash
  azurite --silent --skipApiVersionCheck
  ```

Then run the E2E tests:

```bash
dotnet test test/Cli/Func.E2ETests/Azure.Functions.Cli.E2ETests.csproj
```

**Note:** The build automatically copies `func` to the test output directory (`out/bin/Azure.Functions.Cli.E2ETests/debug/`). To test a different `func` executable, set the `FUNC_PATH` environment variable:

```bash
export FUNC_PATH=/path/to/custom/func
```

#### Missing Templates

If tests fail due to missing templates, download them:

```bash
./eng/scripts/download-templates.ps1 -OutputPath "./out/bin/Azure.Functions.Cli.E2ETests/debug"
```

The script downloads templates to a `templates/` folder in the specified output directory.
