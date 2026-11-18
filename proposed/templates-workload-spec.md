# Template Engine and Packages

Navigation gateway, not an approved package or integration specification. Start with [Templates Architecture](../docs/templates/README.md), then the [proposed behavior contract](../docs/templates/architecture.md) and [implementation baseline and authorization gates](../docs/templates/implementation.md).

## Current boundaries

[TemplaterFactory](../src/Func/Templates/Engine/TemplaterFactory.cs) creates a command-scoped `Templater` in the existing CLI-home `templates` hive. [Templater](../src/Func/Templates/Engine/Templater.cs) lists installed templates as the engine's `ITemplateInfo` values. Catalog and parameter projection, and raw constraint reader/matcher/evaluator helpers, are not part of the current foundation. Constraint-aware command resolution and invocation are not wired. Project/item kinds use `tags.type`; engine types stay inside the integration.

[TemplateFixtures](../test/Func.Tests/Templates/TemplateFixtures.cs) installs local content into isolated test hives. Workloads can declare a `templateRoot` that [WorkloadTemplatePackageProviderFactory](../src/Func/Templates/Engine/WorkloadTemplatePackageProviderFactory.cs) exposes to the engine through [WorkloadTemplateSource](../src/Func/Templates/Engine/WorkloadTemplateSource.cs), without a separate CLI-hive installation. Workload packaging does not emit `templateRoot` yet. The working .NET initializer uses a separate `dotnet new` hive. Existing setup must not be described as making template content ready.

The CLI hive is used for engine settings, managed-package state, and the persistent template cache even when workload payloads live elsewhere. Source changes and strictly newer timestamps trigger lazy engine rebuilds; ordinary commands reuse unchanged cache data. Duplicate identities currently follow native engine precedence; the CLI adds no highest-package-version selection or duplicate rejection.

Current acquisition scope is workload-provided core templates, which are versioned and serviced with their workload ([versioning model](../docs/templates/architecture.md#core-and-independently-packaged-template-versioning)). Future `func templates` commands will delegate standalone NuGet package acquisition and lifecycle to the engine's managed provider in the same hive. Known-package-ID installation does not need remote search. The older `func new install/update/uninstall` proposal is historical.

Workload packaging and future standalone package-type/feed/update policy remain separate work. Functions-specific constraint validation is a separate increment and is not implemented yet. This gateway adds no discovery-time installation, new registry, package manager or lifecycle commands.
