## Running the latest runtime version
### Dependencies

There is a dependency on the .NET Core tools for the cross platform support. You can [install these here](https://www.microsoft.com/net/core).

To install the required dotnet packages navigate into the repository root and run `dotnet restore`

### Compiling the CLI Tools
To build the project run `cd src/Azure.Functions.Cli; dotnet build` (note navigating into the src directory is required due to the test suite currently failing to compile on non-windows environments - see below)

### Running against a function app
To test this project against a local function app you can run from that function app's directory

`dotnet run --project PATH_TO_FUNCTIONS_CLI/src/Azure.Functions.Cli start`

where PATH_TO_FUNCTIONS_CLI is the absolute or relative path to the root of this repository.

## Running the Test Suite
You cannot run the test suite at this moment in time using DotNet Core. We will update this file when this becomes possible.

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