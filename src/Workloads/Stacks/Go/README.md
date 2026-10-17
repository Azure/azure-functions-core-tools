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

## Status

Preview. The public initializer scaffolds the Go worker SDK project and runs
`go mod tidy` unless `--skip-go-mod-tidy` is supplied.

The workload also bundles a native `basic` project archive from the same owned
sources. It retains bundle options and module naming, but remains staged:
the shared native workflow must provide Go preparation before public activation.
No separate template feed or Go item catalog is introduced.

Controlled native-project fixtures supply the owned Go preparation helper
through the shared init-runner preview/complete callback. They cover tidy
skips, preserved exit/stderr results, cancellation and retained native files
on preparation failure. The callback is explicitly injected by tests only;
public `func init` remains on the existing initializer.

An opt-in CLI-owned workflow provider now supplies that preparation using
narrowly source-linked helper code and the archive's exact project provenance.
It does not consume a physical workload's preparation services. Public command
routing and the existing Windows worker launch contract are unchanged.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
