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

#### Node.js connector template tests

The Office 365, SharePoint, OneDrive, Dataverse, and Teams templates for JavaScript and TypeScript v4 are embedded in `src/Cli/func/StaticResources/node-v4-templates.json`. They are not delivered through the templates repository's extension bundles. No bundle catalog injection or shared-cache replacement is required.

On Windows, use the .NET SDK pinned by `global.json` to make a generation-only build without downloading language workers:

```powershell
dotnet build .\src\Cli\func\Azure.Functions.Cli.csproj -p:NoWorkers=true -p:RuntimeIdentifiers=win-x64 -r win-x64
$func = (Resolve-Path .\out\bin\Azure.Functions.Cli\debug_win-x64\func.exe).Path
& $func --version
```

Use this executable, not an older global `func` installation. The `NoWorkers` build is sufficient for `init`, `templates list`, and `new`, but is not intended for starting a Functions host. The following generates all five connectors in both languages with default and custom names, without configuring an extension bundle or installing npm packages:

```powershell
$testRoot = Join-Path $env:TEMP ("coretools-connectors-" + [guid]::NewGuid().ToString('N'))
$templates = @(
    @{ Template = 'Office 365 trigger'; Name = 'office365NewEmailPoll' },
    @{ Template = 'SharePoint trigger'; Name = 'sharepointNewFilePoll' },
    @{ Template = 'OneDrive trigger'; Name = 'oneDriveNewFilePoll' },
    @{ Template = 'Dataverse trigger'; Name = 'dataverseNewRowPoll' },
    @{ Template = 'Teams trigger'; Name = 'teamsNewChannelMessagePoll' }
)
foreach ($language in 'javascript', 'typescript') {
    $project = Join-Path $testRoot $language
    & $func init $project --worker-runtime node --language $language --model V4 --no-bundle --skip-npm-install
    Push-Location $project
    try {
        & $func templates list --language $language
        foreach ($template in $templates) {
            foreach ($name in @($template.Name, ('custom' + $template.Name))) {
                & $func new --language $language --template $template.Template --name $name
            }
        }
    } finally {
        Pop-Location
    }
}
```

Check the generated modules in both projects' `src\functions` folders. Install the matching SDK dependencies and apply the TypeScript compiler settings in the [connector setup guide](docs/connector-templates.md#create-and-configure-a-function), then run `npm run build` in the TypeScript project. JavaScript templates are CommonJS-only; the guide describes the ESM alternative and the separate host requirements for live polling.

Run the focused native tests from the repository root:

```powershell
dotnet test .\test\Cli\Func.UnitTests\Azure.Functions.Cli.UnitTests.csproj -p:NoWorkers=true -p:RuntimeIdentifiers=win-x64 -r win-x64 --filter "FullyQualifiedName~TemplatesManagerTests"
$env:FUNC_PATH = $func
dotnet test .\test\Cli\Func.E2ETests\Azure.Functions.Cli.E2ETests.csproj -p:NoWorkers=true -p:RuntimeIdentifiers=win-x64 -r win-x64 --filter "FullyQualifiedName~FuncNew_NodeV4Connector_UsesEmbeddedTemplateWithoutBundle"
```

These process-level tests generate code only and do not require Azurite, Azure credentials, or live connections. The unit tests verify discovery from the actual embedded resource and replacement of both function names. The process tests verify the rebuilt CLI creates the expected JavaScript and TypeScript modules without a bundle.

Windows and Linux PR unit jobs also run the dependency-free Node.js 24 catalog/syntax checks:

```powershell
node --test .\test\Cli\Func.UnitTests\node-connector-templates.test.mjs
```

For actual SDK registration, binding defaults, payload normalization, and strict TypeScript checks, set `NODE_CONNECTOR_TEST_DEPENDENCIES` to a project containing TypeScript, `@types/node`, and the SDK versions in the connector guide before running that command. Without it, SDK-dependent checks are explicitly skipped. Neither profile starts a host or validates live event delivery. Remove the isolated `$testRoot` when finished.

#### Missing Templates

If tests fail due to missing templates, download them:

```bash
./eng/scripts/download-templates.ps1 -OutputPath "./out/bin/Azure.Functions.Cli.E2ETests/debug"
```

The script downloads templates to a `templates/` folder in the specified output directory.
