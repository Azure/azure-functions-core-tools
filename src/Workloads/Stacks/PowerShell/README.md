# Azure Functions CLI – PowerShell workload

This workload extends the Azure Functions CLI (`func`) with PowerShell project
support: `func init --stack powershell` scaffolding, language-specific options,
and (via the workload model) any future PowerShell-only commands.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.PowerShell
# or by alias
func workload install powershell
```

## Templates

The workload includes TemplateEngine templates for a PowerShell project
(`func-powershell`), HTTP trigger (`httptrigger`), and timer trigger
(`timertrigger`). These templates do not yet back `func init` or `func new`.

Projects include `host.json`, `local.settings.json`, `profile.ps1`, `.funcignore`,
and a sample `HelloWorld` HTTP function. Each function has its own directory
containing `run.ps1` and `function.json`; use a unique name for each function.

Project options include `no-http`, `runtime-version` (7.6, the default and only
supported version; 7.4 reached end of life), `managed-dependencies` (disabled by
default), `no-bundles`, and `bundles-channel` (Stable, Preview, or Experimental).
Enabling managed dependencies adds `requirements.psd1`, with a commented `Az`
example, and enables them in `host.json`; uncomment or add the modules your app
needs in that file. No PowerShell Gallery lookup or module installation runs
during generation.

HTTP triggers support `auth-level` (`function` by default, `anonymous`, or
`admin`). Timer triggers use `0 */5 * * * *`; edit the generated `function.json`
to change the schedule. The default local storage setting uses Azurite, which
must be running when testing timer triggers locally.

Packaged script templates use a `.ps1.txt` suffix to avoid NuGet installation
script handling. TemplateEngine renames them to `.ps1` during generation.

## Status

Preview.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
