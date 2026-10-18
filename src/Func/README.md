# Azure Functions CLI

The func CLI is used to create, develop, test, and deploy Azure Functions locally.

## Usage

```
func <command> [options]
```

## Global Options

| Option | Description |
|--------|-------------|
| `--verbose` | Enable verbose output. |
| `--version` | Show version information. |
| `-h, --help` | Show help and usage information. |

## Commands

### Project Commands

| Command | Description |
|---------|-------------|
| `init` | Initialize a new Azure Functions project. |
| `new` | Create a new function from a template. |
| `run` | Launch the Azure Functions host runtime. |
| `setup` | Prepare local CLI dependencies (host, worker, stack, templates, extension bundle). |
| `version` | Display the current CLI version. |

### Quickstart

| Command | Description |
|---------|-------------|
| `quickstart` | Browse and scaffold complete function apps from the template catalog. |
| `quickstart list` | List available templates from the catalog. |
| `quickstart info` | Show detailed information about a template. |

### Workload Management

| Command | Description |
|---------|-------------|
| `workload install` | Install a workload. |
| `workload uninstall` | Uninstall a workload. |
| `workload update` | Update an installed workload in place. |
| `workload list` | List installed workloads. |
| `workload search` | Search the workload catalog. |
| `workload prune` | Remove inactive side-by-side workload installs. |

### Profile Management

| Command | Description |
|---------|-------------|
| `profile list` | List available Azure Functions profiles. |
| `profile show` | Show details for an Azure Functions profile. |
| `profile set` | Set the default profile for a Functions project. |

## Examples

```bash
# Initialize a new project
func init --stack dotnet

# Create a new function
func new --template HttpTrigger --name MyFunction

# Search installed item templates for the current project
func new --list --search http

# Inspect the selected template's effective parameters, defaults and choices
func new --template http --help

# Preview a project or function without writing files or running preparation tools
func init MyApp --stack node --language js --dry-run --output json
func new --template http --name MyFunction --dry-run --output json

# Run the Functions host
func run

# Browse quickstart templates
func quickstart list

# Scaffold a quickstart template
func quickstart --language python

# Install a workload
func workload install dotnet

# List installed workloads
func workload list

# Set up all dependencies for the current project
func setup
```

`init` and `new` accept `--output plain|json` and `--non-interactive`. JSON is a
single result document and never prompts. `--dry-run` uses the same template
selection, parameter validation and path guards as creation, but does not create
the target, write CLI configuration, acquire packages or run preparation tools.
With `init --force --dry-run`, existing files (including `.git`) are not deleted.
The preview separately lists planned force-cleanup entries; listed directories
include their contents.
Adoption and repair previews report planned configuration without persisting it.
Before loading installed templates, generation previews, item listings and
selected-template help refuse pending recovery without restoring the live
registry. Run an explicit workload install or update to recover the store, then
retry. Configuration-only adoption and repair previews do not load templates.

`new --list` shows eligible installed items for the project's language and selected
workload owner, not remote quickstarts or available workload packages. Add
`--search <text>` for a case-insensitive substring match against identity, display
name, short names and description; no matches is a successful empty list.
`--template` can further restrict the list to an exact identity or short name.
Do not combine listing with generation parameters, `--name`, `--force` or
`--dry-run`. Use `--list --output json` for package and eligibility metadata.

Template options take explicit scalar values, including booleans. Use
`--option=value` for dash-prefixed literals or empty strings where supported.
Repeated options and invalid choices fail before generation. When a template
alias conflicts with a CLI option, selected-template help shows the effective
`--param:<CanonicalName>` alias.

## Additional documentation

- [Azure Functions documentation](https://learn.microsoft.com/azure/azure-functions)
- [Azure Functions Core Tools on GitHub](https://github.com/Azure/azure-functions-core-tools)

## Feedback & contributing

https://github.com/Azure/azure-functions-core-tools
