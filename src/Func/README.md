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

## Additional documentation

- [Azure Functions documentation](https://learn.microsoft.com/azure/azure-functions)
- [Azure Functions Core Tools on GitHub](https://github.com/Azure/azure-functions-core-tools)

## Feedback & contributing

https://github.com/Azure/azure-functions-core-tools
