---
title: Azure Functions CLI reference (v5)
description: Reference documentation for the Azure Functions CLI (func.exe), the v5 successor to Azure Functions Core Tools.
ms.topic: reference
ms.date: 05/20/2026
ms.custom:
  - sfi-ropc-nochange
---

# Azure Functions CLI reference (v5)

This article provides reference documentation for the **Azure Functions CLI**, the v5 successor to Azure Functions Core Tools. The CLI lets you develop, manage, and run Azure Functions projects from your local computer. The binary name remains `func` (or `func.exe` on Windows), so existing muscle memory continues to work.

> [!IMPORTANT]
> The v5 Azure Functions CLI is a ground-up rebuild. The set of commands and options documented here reflects what is currently implemented. Commands available in Azure Functions Core Tools v4 (such as `func azure functionapp publish`, `func azurecontainerapps deploy`, `func durable`, `func kubernetes`, and `func extensions`) are not yet ported. Continue to use Core Tools v4 for those workflows.

## Commands at a glance

| Command | Description |
| ----- | ----- |
| [`func init`](#func-init) | Initialize a new Azure Functions project. |
| [`func new`](#func-new) | Create a new function from a template. |
| [`func start`](#func-start) | Launch the Functions host runtime locally. |
| [`func workload`](#func-workload) | Manage installed CLI workloads. |
| [`func help`](#func-help) | Display help for a command. |
| [`func version`](#func-version) | Display version information. |

## Workloads

The Azure Functions CLI follows a **workload model**, similar to the .NET SDK. The `func` binary itself is small and language-agnostic. Everything that's stack-specific (Python, Node.js, .NET, Go) or tool-specific (extension bundles) is delivered as a separate **workload** package that you install on demand.

### Why workloads?

* **Install only what you need.** A Python developer doesn't need .NET project scaffolding on their machine, and vice versa.
* **Independent updates.** A fix to the Node.js workload can ship without re-releasing the whole CLI.
* **Smaller base install.** The base `func` install stays lean; workloads add capability incrementally.
* **Extensible.** Third parties can ship their own workloads (custom templates, custom hosts, etc.).

### What's in a workload?

A workload is a NuGet-style package that contributes one or more of:

* **A project initializer.** Enables `func init --stack <name>` for that language.
* **Templates.** Enables `func new` to scaffold functions in that language.
* **Commands.** Adds new subcommands under `func` (for example, an extension bundle manager).
* **Services.** Plugs into shared CLI services (project detection, host startup, and so on).

Workloads are versioned independently from the CLI and are installed side-by-side. Only the highest installed semver of each workload is loaded at runtime; older versions stay on disk for rollback.

### First-run experience

The first time you run `func init`, `func new`, or `func start`, the CLI checks whether the workloads required for your scenario are installed. If they aren't, the CLI prompts you to install them. Accepting the prompt installs the recommended set for the stack you chose. You can decline the prompt and install workloads manually with `func workload install`.

### Default workload recommendations

Match the workloads you install to the stack you're developing in. The recommended starter set for each stack is:

| Stack | Recommended workloads |
| ----- | ----- |
| **Python** | `python`, `python-worker`, `bundles`, `host`, `templates` |
| **Node.js / TypeScript** | `node`, `node-worker`, `bundles`, `host`, `templates` |
| **.NET (isolated, C# / F#)** | `dotnet`, `host`, `templates` |
| **Go** | `go`, `go-worker`, `bundles`, `host`, `templates` |

What each role does:

| Role | Purpose |
| ----- | ----- |
| **Stack** (`python`, `node`, `dotnet`, `go`) | Project initialization and language-specific tooling for `func init` and `func new`. |
| **Worker** (`python-worker`, `node-worker`, ...) | The language worker that the Functions host uses to execute your functions at run time. |
| **`bundles`** | Resolves and caches Azure Functions extension bundles so triggers and bindings work out of the box. |
| **`host`** | The Azure Functions host runtime used by `func start`. |
| **`templates`** | The function templates surfaced by `func new`. |

> [!NOTE]
> The `bundles` workload is recommended for any non-.NET stack. .NET projects reference extensions through NuGet directly and don't need it. .NET also doesn't require a separate worker workload, because the worker is part of the compiled project itself.

You don't have to install these one at a time. The first time you run `func init`, `func new`, or `func start`, the CLI prompts you to install the recommended set for your chosen stack.

### Currently available workloads

The following workloads are published today. Additional workloads (for example, `host`, `templates`, and per-language workers) ship over time. Run `func workload search` to see the current catalog.

| Alias | Display name | Description |
| ----- | ----- | ----- |
| `dotnet` | .NET | Azure Functions tooling for .NET (C#, F#) projects. |
| `node` | Node.js | Azure Functions CLI tooling for Node.js projects. |
| `python` | Python | Azure Functions CLI tooling for Python projects. |
| `go` | Go | Azure Functions CLI tooling for Go projects. |
| `bundles` | Extension Bundles | Resolves and caches Azure Functions extension bundles. |

## `func init`

Initializes a new Azure Functions project in the specified folder. The scaffolding itself is contributed by the workload for the chosen stack, so the available options depend on which workloads are installed.

```command
func init [<PATH>] [options]
```

When you supply `<PATH>`, the project is created in that folder. Otherwise, the current folder is used.

The `func init` command supports these built-in options:

| Option | Description |
| ----- | ----- |
| **`--stack`**, **`-s`** | The stack to use for the project (for example, `python`, `node`, `dotnet`, `go`). Run `func workload list` to see the stacks contributed by your installed workloads. |
| **`--name`**, **`-n`** | The name of the function app project. |
| **`--language`**, **`-l`** | The programming language (for example, `C#`, `F#`, `JavaScript`, `TypeScript`, `Python`). Used when a stack supports more than one language. |
| **`--force`** | Initialize even when the target folder is not empty. Overwrites existing files with the same name. |

Workloads contribute additional options that are grouped under the workload's name in `func init --help`.

If no workload provides the requested stack, the CLI prints a hint pointing at `func workload install` and exits non-zero.

## `func new`

Creates a new function in the current project from a template.

```command
func new [<PATH>] [options]
```

The `func new` command supports these options:

| Option | Description |
| ----- | ----- |
| **`--name`**, **`-n`** | The function name. |
| **`--template`**, **`-t`** | The function template name. Available templates come from the installed workload for the project's stack. |
| **`--force`** | Overwrite existing files. |

If no workload contributes templates for the current project, the CLI prints a hint pointing at `func workload install`.

## `func start`

Launches the Azure Functions host runtime and loads the project in the current folder.

```command
func start [<PATH>] [options]
```

The `func start` command supports these options:

| Option | Description |
| ----- | ----- |
| **`--port`**, **`-p`** | The local port to listen on. Default: `7071`. |
| **`--cors`** | A comma-separated list of CORS origins, with no spaces. |
| **`--cors-credentials`** | Allow cross-origin authenticated requests that use cookies and the `Authentication` header. |
| **`--functions`** | A space-separated list of functions to load. |
| **`--no-build`** | Don't build the project before running. |
| **`--enable-auth`** | Enable the full authentication-handling pipeline, including authorization requirements. |
| **`--host-version`**, **`-v`** | The host runtime version to use (for example, `4.1049.0`). |
| **`--output`** | Output mode: `compact` (interactive TUI), `plain` (CI / non-TTY), or `json` (NDJSON for AI agents). Defaults to auto-detect based on the terminal. |
| **`--no-tui`** | Alias for `--output=plain`. Disables the interactive TUI. |
| **`--log-file`** | Mirror all host events to the specified log file. |

With the project running, call the function endpoints directly to verify behavior.

> [!NOTE]
> The v5 `func start` runs against an in-memory event source for demonstration in early builds; full host integration replaces this in subsequent releases.

## `func workload`

Manages workloads installed for the Azure Functions CLI. Subcommands:

| Subcommand | Description |
| ----- | ----- |
| [`func workload list`](#func-workload-list) | List installed workloads. |
| [`func workload search`](#func-workload-search) | Search the workload catalog. |
| [`func workload install`](#func-workload-install) | Install a workload. |
| [`func workload update`](#func-workload-update) | Update an installed workload in place. |
| [`func workload uninstall`](#func-workload-uninstall) | Uninstall a workload. |
| [`func workload prune`](#func-workload-prune) | Remove inactive side-by-side workload installs. |

### `func workload list`

Lists installed workloads. By default, only the loaded (highest-semver) version of each workload is shown. Pass `--all-versions` to see every side-by-side install.

```command
func workload list [options]
```

| Option | Description |
| ----- | ----- |
| **`--all-versions`**, **`-a`** | List every installed version of every workload. Default: loaded version only. |
| **`--json`** | Emit machine-readable JSON instead of a table. |

### `func workload search`

Searches the configured workload catalog for available workload packages.

```command
func workload search [<QUERY>] [options]
```

When `<QUERY>` is omitted, all workloads in the catalog are listed.

| Option | Description |
| ----- | ----- |
| **`--source`** | NuGet feed URI to search. Defaults to the configured catalog. |
| **`--prerelease`** | Include prerelease versions in the results. |
| **`--json`** | Emit machine-readable JSON instead of a table. |

### `func workload install`

Resolves a workload package id (or alias) through the configured catalog and installs it.

```command
func workload install <ID> [options]
```

`<ID>` can be a workload package id, an alias (for example, `python`), or a path to a local `.nupkg` file.

| Option | Description |
| ----- | ----- |
| **`--version`**, **`-v`** | Specific semver version to install. Default: the latest stable version in the catalog. |
| **`--source`** | Catalog source URL or local directory to resolve from. Default: the configured catalog. |
| **`--prerelease`** | Allow prerelease versions when resolving from the catalog. Default: stable only. |
| **`--force`**, **`-f`** | Overwrite an existing install of the same id and version. Also skips the "use update instead" prompt. |
| **`--exact`**, **`-e`** | Disable alias matching. `<ID>` must be the literal package id. |

If a version of the workload is already installed, the CLI prompts you to use `func workload update` instead. Non-interactive contexts treat the prompt as a decline.

### `func workload update`

Performs an in-place atomic version swap for an installed workload. Updates are not side-by-side; for side-by-side installs use `func workload install --force`.

```command
func workload update [<ID>] [options]
```

Pass an `<ID>` to update a single workload, or `--all` to update every installed workload. Exactly one of the two must be specified.

| Option | Description |
| ----- | ----- |
| **`--version`**, **`-v`** | Installed version to replace. Default: the highest installed semver. |
| **`--all`** | Update every installed workload. Mutually exclusive with `<ID>`. |
| **`--major`** | Allow crossing a major-version boundary. Default: same major only. |
| **`--source`** | Catalog source URL or local directory to resolve from. Default: the configured catalog. |
| **`--prerelease`** | Allow prerelease versions when resolving from the catalog. Default: stable only. |
| **`--exact`**, **`-e`** | Disable alias matching. `<ID>` must be the literal package id. |

### `func workload uninstall`

Removes one or all installed versions of a workload.

```command
func workload uninstall <ID> [options]
```

| Option | Description |
| ----- | ----- |
| **`--version`**, **`-v`** | Specific version to uninstall. Default: the only installed version. |
| **`--all-versions`**, **`-a`** | Uninstall every installed version of the workload. Mutually exclusive with `--version`. |
| **`--exact`**, **`-e`** | Disable alias matching. `<ID>` must be the literal package id. |

### `func workload prune`

Removes inactive side-by-side workload installs. For each in-scope package id, the highest installed version is kept and older versions are uninstalled. This command is local-only and never touches the catalog.

```command
func workload prune [<ID>] [options]
```

When `<ID>` is omitted, every installed workload is pruned.

| Option | Description |
| ----- | ----- |
| **`--exact`**, **`-e`** | Disable alias matching. `<ID>` must be the literal package id. |

## `func help`

Displays help for the CLI or for a specific command.

```command
func help [<COMMAND>]
```

When `<COMMAND>` is omitted, top-level help is displayed.

## `func version`

Displays the version of the Azure Functions CLI.

```command
func version
```

Equivalent to `func --version`. Pass `func --version --verbose` for detailed build, runtime, OS, and architecture information.

## Global options

These options are available on most commands:

| Option | Description |
| ----- | ----- |
| **`--help`**, **`-h`**, **`-?`** | Display help for the command. |
| **`--version`**, **`-v`** | Display the Azure Functions CLI version. Use `--verbose` together with `--version` for detailed build information. |
| **`--verbose`** | Enable verbose output. Not supported by all commands. |
