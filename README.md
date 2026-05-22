![Azure Functions Logo](https://raw.githubusercontent.com/Azure/azure-functions-core-tools/refs/heads/main/eng/res/functions.png)

|Branch|Status|
|------|------|
|main|[![Build Status](https://dev.azure.com/azfunc/public/_apis/build/status%2Fazure%2Fazure-functions-core-tools%2Fcoretools.public?repoName=Azure%2Fazure-functions-core-tools&branchName=main)](https://dev.azure.com/azfunc/public/_build/latest?definitionId=579&repoName=Azure%2Fazure-functions-core-tools&branchName=main) |
|vnext|[![Build Status](https://dev.azure.com/azfunc/public/_apis/build/status%2Fazure%2Fazure-functions-core-tools%2Fcoretools.public?repoName=Azure%2Fazure-functions-core-tools&branchName=vnext)](https://dev.azure.com/azfunc/public/_build/latest?definitionId=579&repoName=Azure%2Fazure-functions-core-tools&branchName=vnext)|

# Azure Functions Core Tools

The Azure Functions Core Tools provide a local development experience for creating, developing, testing, running, and debugging Azure Functions.

## Installation

### One-line install

**PowerShell** (Windows, macOS, Linux):

```powershell
irm https://aka.ms/func-cli/install.ps1 | iex
```

**Bash** (macOS, Linux):

```bash
curl -sSL https://aka.ms/func-cli/install.sh | bash
```

Both scripts auto-detect your OS and architecture, download the latest 5.x release from GitHub, and install to `~/.azure-functions`.

### Options

| Option | PowerShell | Bash |
|--------|-----------|------|
| Include pre-releases | `& ([scriptblock]::Create((irm https://aka.ms/func-cli/install.ps1))) -Prerelease` | `PRERELEASE=true curl -sSL https://aka.ms/func-cli/install.sh \| bash` |
| Specific version | `& ([scriptblock]::Create((irm https://aka.ms/func-cli/install.ps1))) -Version 5.0.0` | `VERSION=5.0.0 curl -sSL https://aka.ms/func-cli/install.sh \| bash` |
| Custom install dir | `& ([scriptblock]::Create((irm https://aka.ms/func-cli/install.ps1))) -InstallDir ~/my-tools` | `INSTALL_DIR=~/my-tools curl -sSL https://aka.ms/func-cli/install.sh \| bash` |

### Unattended install

To chain commands after the installer (e.g., in CI pipelines or Dockerfiles), source the updated PATH and then run `func` directly:

**PowerShell:**

```powershell
& ([scriptblock]::Create((irm https://aka.ms/func-cli/install.ps1)))
func workload install dotnet
```

**Bash:**

```bash
curl -sSL https://aka.ms/func-cli/install.sh | bash
source ~/.bashrc
func workload install node
```

The install script adds `~/.azure-functions` to your PATH automatically. In PowerShell, the current session is updated immediately. In Bash, you need to `source` your shell profile to pick up the change before running `func`.

## Usage

```bash
func <command> [path] [options]
```

| Command | Description |
|---------|-------------|
| `func init [path]` | Initialize a new Azure Functions project |
| `func new [path]` | Create a new function from a template |
| `func start [path]` | Start the Functions host locally |
| `func workload install <id>` | Install a language workload |
| `func workload list` | List installed workloads |
| `func version` | Display version information |
| `func help [command]` | Display help for a command |

## Architecture

The v5 CLI is a ground-up rebuild using:

- **System.CommandLine 2.0.6** — command parsing (following the dotnet CLI model)
- **Spectre.Console** — rich terminal output (tables, prompts, spinners)
- **Dependency Injection** — Microsoft.Extensions.DependencyInjection
- **.NET 10** — target framework
- **Workload model** — core CLI + optional language workloads (dotnet, node, python, etc.)

## Project Structure

```
src/
  Func/                    # CLI application
    Commands/              # Command definitions (one file per command)
    Console/               # Console output abstractions (Spectre wrappers)
    Workloads/             # Workload engine (contracts, manager, manifest)
    Program.cs             # Entry point with DI wiring
    Parser.cs              # Command tree composition
  Abstractions/            # Public abstractions
  Workloads/
    <kind>/<Name>/         # An individual grouped workload
    <Name>/                # An individual ungrouped workload
test/
  Func.Tests/              # Unit tests core cli (xUnit + NSubstitute)
  Workloads/
    <kind>/<Name>.Tests/   # Unit tests for a grouped workload
    <Name>.Tests/          # Unit tests for an ungrouped workload
eng/
  build/                   # MSBuild props and targets
  ci/                      # CI pipeline definitions (Azure DevOps)
  scripts/                 # Build and validation scripts
  res/                     # Resources (icons, MSI assets)
docs/
  vnext.md                 # v5 design document
  modernization.md         # Modernization design document
Azure.Functions.Cli.slnx   # Solution file
```

## Development

**Prerequisites:** .NET 10 SDK (see `global.json` for the exact version)

### Build

```bash
dotnet restore
dotnet build
```

### Test

```bash
dotnet test
```

### Run Locally

```bash
# Option 1 — dotnet run
dotnet run --project src/Func -- <command> [args]

# Option 2 — add to PATH
export PATH="$PATH:$(pwd)/out/bin/Func/debug"
func <command>
```

### Publish a Self-Contained Build

```bash
dotnet publish src/Func/Func.csproj -c Release -r osx-arm64
./out/pub/Func/release_osx-arm64/func --version
```

Common runtime identifiers: `osx-arm64`, `osx-x64`, `linux-x64`, `win-x64`

## Branches

| Branch | Description |
|--------|-------------|
| `main` | v4 — current stable release |
| `vnext` | v5 — next-generation CLI rebuild |

## Documentation

- [Code and test Azure Functions locally](https://docs.microsoft.com/azure/azure-functions/functions-run-local)
- [Contributing Guide](CONTRIBUTING.md)

## Telemetry

The Azure Functions Core Tools collect usage data in order to help us improve your experience.
The data is anonymous and doesn't include any user specific or personal information. The data is collected by Microsoft.

You can opt-out of telemetry by setting the `FUNC_CLI_TELEMETRY_OPTOUT` environment variable to any value other than `no`, `n`, `0`, `false`, or `off`. The legacy `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT` variable from Core Tools v4 is still honored.

[Microsoft privacy statement](https://privacy.microsoft.com/privacystatement)

## License

This project is under the benevolent umbrella of the [.NET Foundation](http://www.dotnetfoundation.org/) and is licensed under [the MIT License](LICENSE).

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Contact Us

For questions on Azure Functions or the tools:

- [Azure Functions Q&A Forum](https://docs.microsoft.com/answers/topics/azure-functions.html)
- [Azure-Functions tag on StackOverflow](http://stackoverflow.com/questions/tagged/azure-functions)
- [File bugs on GitHub](https://github.com/Azure/azure-functions-core-tools/issues)
