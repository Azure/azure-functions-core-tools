# Azure Functions CLI – Java workload

This workload extends the Azure Functions CLI (`func`) with Java project
support: `func init --stack java` scaffolding (a Maven project), language
detection for existing projects, and a `func run` flow that builds the
project with Maven before launching the host.

## Install

```bash
func workload install Azure.Functions.Cli.Workloads.Java
# or by alias
func workload install java
```

The Java worker payload ships separately in the `java-worker` workload and is
acquired automatically the first time you run `func run`.

## Prerequisites

- JDK 21 (set `JAVA_HOME`)
- Apache Maven 3.6+ (`mvn` on `PATH`, or a `mvnw` wrapper in the project)

## Bundled templates

The workload includes TemplateEngine templates for a Maven project, an HTTP
trigger, and a timer trigger. The project name is normalized for the Maven
artifact and Functions app; `--package-name` sets the Java package and Maven
group ID (default `com.function`). Trigger templates accept the same package
option, so use the project's package when adding functions. Package names are
normalized for Java. Function and class names use upper camel case; generated
classes use a `Function` prefix. Names that normalize to the same value refer
to the same function, so choose a unique name. The starter function is named
`HelloWorldSample`.

The project template supports omitting the sample HTTP function or extension
bundle and selecting the bundle channel. HTTP items default to function-key
authorization and also support anonymous and admin authorization. Generated
projects use Azurite for local storage. These templates do not yet back
`func init` or `func new`.

## Build and deploy

Run `mvn clean package` to stage the app under
`target/azure-functions/<functionAppName>`. To deploy, use
`mvn clean package azure-functions:deploy`; the Maven plugin packages the
staged app at the ZIP root and omits `local.settings.json`. Do not ZIP the
project root or the staging directory directly: the project root has the wrong
ZIP layout, and the staging directory contains local settings.

## Template testing

Run `pwsh ./test/Workloads/Templates/Java.Tests/JavaTemplateBuild.ps1` from the
repository root to generate and package a project with both trigger types. The
test requires the .NET SDK, JDK 21, and Maven.

## Status

Preview.

## Links

- [Azure Functions CLI](https://github.com/Azure/azure-functions-core-tools)
- [Workload spec](https://github.com/Azure/azure-functions-core-tools/blob/docs/proposed/workload-spec.md)
