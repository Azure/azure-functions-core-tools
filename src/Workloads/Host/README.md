# Azure Functions CLI - Host workload

Content workload that ships a small Azure Functions host shell and the host
payload consumed by the Azure Functions CLI when running function apps locally.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Host
# or by alias
func workload install host
```
> Note: the actual package ID is `Microsoft.Azure.Functions.Workloads.Host.<rid>`. The format and workload specs will be updated to detail how this would be handled. The workload also supports installation by the shorter alias `host`.

## Status

Preview. The shell expects the Azure Functions CLI to prepare the process
environment and command-line arguments before launch. It does not translate
private CLI configuration into host settings. Local builds are
framework-dependent by default.

Release packaging should be performed with a RID-specific self-contained payload under `tools/any/` by passing
`-p:PackRidSpecificHostWorkload=true -r <rid> -p:SelfContained=true`, which also suffixes the package id with the RID.

## Launch contract

The workload executable is a thin bootstrapper around the packaged Azure
Functions Host. It supports one shell-specific command-line option; every other
argument passed to the workload executable is forwarded unchanged to the host.

The Azure Functions CLI is responsible for setting the process working
directory, environment variables, and any host arguments before launching the
workload process.

| Argument | Owner | Description |
| --- | --- | --- |
| `--enable-auth` | Host workload shell | Enables the host's full authentication pipeline. When omitted, the shell starts the host with local auth bypassed, matching local Core Tools behavior. This argument is consumed by the shell and is not forwarded to the host. |
| `--urls <value>` | Azure Functions Host / ASP.NET Core | Optional listener URL configuration forwarded to the host. The CLI may use this when it wants command-line URL binding instead of setting `ASPNETCORE_URLS`. |
| Any other host-supported argument | Azure Functions Host | Forwarded unchanged. The workload shell does not validate, normalize, or translate host arguments. |

The shell does not support private CLI options such as `--script-root`, `--port`,
`--cors`, `--cors-credentials`, or `--functions`. The CLI should resolve those
concepts before process launch and express them using the host's expected
environment/configuration surface.
