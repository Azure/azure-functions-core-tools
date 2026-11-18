# Template Engine and Packages

Navigation gateway, not an approved package or integration specification. Start with [Templates Architecture](../docs/templates/README.md), then the [proposed behavior contract](../docs/templates/architecture.md) and [implementation baseline and authorization gates](../docs/templates/implementation.md).

## Current boundaries

[TemplaterFactory](../src/Func/Templates/Engine/TemplaterFactory.cs) creates a command-scoped `Templater` in the existing CLI-home `templates` hive. [Templater](../src/Func/Templates/Engine/Templater.cs) lists installed templates as the engine's `ITemplateInfo` values. Catalog and parameter projection, and raw constraint reader/matcher/evaluator helpers, are not part of the current foundation. Constraint-aware command resolution and invocation are not wired. Project/item kinds use `tags.type`; engine types stay inside the integration.

[TemplateFixtures](../test/Func.Tests/Templates/TemplateFixtures.cs) installs local content into isolated test hives. Workloads can declare a `templateRoot` that [WorkloadTemplatePackageProviderFactory](../src/Func/Templates/Engine/WorkloadTemplatePackageProviderFactory.cs) exposes to the engine through [WorkloadTemplateSource](../src/Func/Templates/Engine/WorkloadTemplateSource.cs); nothing is registered in the CLI hive, and workload packaging does not emit `templateRoot` yet. The working .NET initializer uses a separate `dotnet new` hive. Existing setup must not be described as making template content ready.

Packaging of `templateRoot`, the approved package-relative location, version/channel/integrity and update policy require Sarah/Fabio decisions. Workers and extension bundles retain their workload responsibilities. No discovery-time installation, new registry, package manager or `func new install/update/uninstall` is authorized by this gateway.
