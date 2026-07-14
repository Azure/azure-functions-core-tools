# Azure Functions CLI – Java workload

This workload extends the Azure Functions CLI (`func`) with Java project
support: `func init --stack java` scaffolding (a Maven project), language
detection for existing projects, and a `func start` flow that builds the
project with Maven before launching the host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Java
# or by alias
func workload install java
```

The Java worker payload ships separately in the `java-worker` workload and is
acquired automatically the first time you run `func start`.

## Prerequisites

- JDK 21 (set `JAVA_HOME`)
- Apache Maven 3.6+ (`mvn` on `PATH`, or a `mvnw` wrapper in the project)

## Status

Preview.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
