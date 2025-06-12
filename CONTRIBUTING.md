## Running the latest runtime version

## Dependencies

There is a dependency on the .NET Core tools for the cross platform support. You can [install these here](https://www.microsoft.com/net/core).

To install the required dotnet packages navigate into the repository root and run `dotnet restore`

## Compiling the CLI Tools

To build the project run `dotnet build` from the root of the repository. This will build the project and all of its dependencies.
The output will be placed in the `out/bin/Azure.Functions.Cli/debug` directory.

`dotnet run --project src/Cli/func <command>` will run the CLI tool from the source directory.

### Running against a function app

To test this project against a local function app you can run from that function app's directory

- `cd myTestFunctionApp`
- `dotnet run --project PATH_TO_FUNCTIONS_CLI/src/Cli/func <command>`

where PATH_TO_FUNCTIONS_CLI is the absolute or relative path to the root of this repository.

Or you can add `out/bin/Azure.Functions.Cli/debug/func` to your `PATH` environment variable and run the command from anywhere.

- `export PATH=$PATH:/path/to/Azure.Functions.Cli/out/bin/Azure.Functions.Cli/debug/func`
- `func <command>`

### Running the Test Suite

- Build the solution `dotnet build Azure.Functions.Cli.sln`
  - As part of this build, the cli is copied into the test project's output directory (`out/bin/Azure.Functions.Cli.Tests/debug`) - this is what will be used by the tests
  - If you wish to override this, you can set the `FUNC_PATH` environment variable to the path of the `func`/`func.exe` you wish to test against
- Run the test suite in Visual Studio Test Explorer or by running `dotnet test` from the `test` project root.
  - i.e. `cd test/Azure.Functions.Cli.Tests; dotnet test`

#### Storage Emulator

Some tests, namely E2E, require an Azure storage emulator to be running. You can download the storage emulator [here](https://learn.microsoft.com/azure/storage/common/storage-use-azurite?tabs=visual-studio%2Cblob-storage).

Run the emulator before your run the tests.

> There is a script you can use for this as well, see `tools/start-emulators.ps1`

#### Templates Missing

If you see an error saying the templates folder is missing, you can download the templates using the `download-templates.ps1` script.

From the root of the repo, run:

- `./eng/scripts/download-templates.ps1 -OutputPath "./out/bin/<test_project_name>/debug`
  - e.g. "./out/bin/Azure.Functions.Cli.E2E.Tests/debug"

The script will download the template packages to a `templates` folder in the specified output directory.

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
