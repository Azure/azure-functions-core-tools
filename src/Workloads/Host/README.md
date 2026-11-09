# Azure Functions CLI - Host workload

Content workload that ships a small Azure Functions host shell and the host
payload consumed by the Azure Functions CLI when running function apps locally.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Host.<rid>
# or by alias
func workload install host
```
> Note: released packages are RID-specific, for example `Azure.Functions.Cli.Workloads.Host.win-x64`. The CLI resolves the current RID package internally; end users should normally install/manage the workload by the shorter alias `host`.

## Status

Preview. The shell expects the Azure Functions CLI to prepare the process
environment and command-line arguments before launch. It does not translate
private CLI configuration into host settings.

## Launch contract

The workload executable is a thin bootstrapper around the packaged Azure
Functions Host. It supports one shell-specific command-line option; every other
argument passed to the workload executable is forwarded unchanged to the host.

The Azure Functions CLI is responsible for resolving the RID-specific workload package, setting the process working
directory, overlaying prepared environment variables, and building host arguments before launching the workload process.
The working directory and `AzureWebJobsScriptRoot` environment variable both point to the prepared function app startup
directory.

| Argument | Owner | Description |
| --- | --- | --- |
| `--enable-auth` | Host workload shell | Enables the host's full authentication pipeline. When omitted, the shell starts the host with local auth bypassed, matching local Core Tools behavior. This argument is consumed by the shell and is not forwarded to the host. |
| `--urls <value>` | Azure Functions Host / ASP.NET Core | Listener URL configuration forwarded to the host. By default, the CLI launches with `http://127.0.0.1:<port>` and presents local URLs as `http://localhost:<port>`. |
| Any other host-supported argument | Azure Functions Host | Forwarded unchanged. The workload shell does not validate, normalize, or translate host arguments. |

Use `func run . --port 0` (or its `func start` alias) to let the OS assign an
available port. By default, the host binds directly to `http://127.0.0.1:0`, without reserving
and releasing a candidate port first. After binding, the shell reads the
server's actual listening address and emits `host.listen_uri` in a structured
log record, before its function metadata snapshot. The CLI uses the selected
port for local URLs in plain, JSON, and dashboard output. For example:

```bash
func run . --no-build --format json --port 0 --no-azurite
```

In JSON output, read `attributes["host.listen_uri"]` on the `HostListening`
log record to obtain the selected base URL. A zero port is not advertised as a
listening URL.

## Bind address

Use `--address` to specify an IPv4 or IPv6 bind address, or set
`Host.LocalHttpAddress` in the app's `local.settings.json`. The command-line
option takes precedence over the setting. `func start` accepts the same option.
Explicit addresses other than `127.0.0.1`, `::1`, and wildcard addresses are
preserved in reported URLs rather than rewritten to `localhost`.

The shell does not support private CLI options such as `--script-root`, `--port`,
`--address`, `--cors`, `--cors-credentials`, or `--functions`. The CLI should resolve those
concepts before process launch and express them using the host's expected
environment/configuration surface.

## Diagnostics

The workload writes the normal stdout/stderr host event stream as newline-delimited
JSON records with `source: "azure-functions-cli-host"` and `schema_version: 1`.
The CLI parses those records before falling back to plain line output. Records may
include dashboard attributes such as `cli.event_kind`, `host.state`,
`function.name`, `function.invocation_id`, HTTP request details, and function
metadata snapshots emitted after host startup.

Set `FUNC_HOST_RAW_LOG_CAPTURE_PATH` to an absolute file path to capture raw host
`ILogger` records as newline-delimited JSON. This is a diagnostic-only capture
file used to inspect category, level, event id, structured state, scopes, and
exception data without changing the stdout/stderr stream consumed by the CLI.
