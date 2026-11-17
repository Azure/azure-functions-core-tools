# Template Engine and Packages

Navigation gateway, not an approved package or integration specification. Start with [Templates Architecture](../docs/templates/README.md), then the [proposed behavior contract](../docs/templates/architecture.md) and [implementation baseline and authorization gates](../docs/templates/implementation.md).

## Current boundaries

[TemplaterFactory](../src/Func/Templates/Engine/TemplaterFactory.cs) creates a synchronous command-scoped session in the existing CLI-home `templates` hive. [Templater](../src/Func/Templates/Engine/Templater.cs) lists installed entries; catalog/parameter projection and raw constraint reader/matcher/evaluator helpers exist. Constraint-aware command resolution and invocation are not wired; `FuncTemplateComponents.AllComponents` is empty. Project/item kinds use `tags.type`; engine types stay inside the integration.

[TemplateFixtures](../test/Func.Tests/Templates/TemplateFixtures.cs) explicitly registers local content in isolated test hives. [WorkloadDeploymentService](../src/Func/Workloads/Install/WorkloadDeploymentService.cs) deploys workload payloads and store metadata, not production template registration. The working .NET initializer uses a separate `dotnet new` hive. Existing setup must not be described as making template content ready.

Production workload-to-CLI-hive registration, approved package-relative location, version/channel/integrity, ownership and retry policy require Sarah/Fabio decisions. Workers and extension bundles retain their workload responsibilities. No discovery-time installation, new registry, package manager or `func new install/update/uninstall` is authorized by this gateway.
