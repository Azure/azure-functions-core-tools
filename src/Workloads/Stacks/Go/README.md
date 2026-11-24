# Azure Functions CLI – Go workload

This workload extends the Azure Functions CLI (`func`) with Go project
support: `func init --stack go` scaffolding, language-specific options,
and (via the workload model) any future Go-only commands.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Go
# or by alias
func workload install go
```

## Templates

The workload includes TemplateEngine templates for a Go project, HTTP trigger,
and timer trigger. The project targets Go 1.24 or later and pins the Go worker
SDK. Generated trigger files register with the project's shared `app` during
package initialization; use a unique name for each function. These templates
do not yet back `func init` or `func new`.

## Status

Preview.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
