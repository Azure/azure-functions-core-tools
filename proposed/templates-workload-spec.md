# Template Engine and Packages

Navigation gateway, not an approved package or integration specification. Start with [Templates Architecture](../docs/templates/README.md), then the [supporting architecture](../docs/templates/architecture.md), the [implementation baseline and authorization gates](../docs/templates/implementation.md), and the [approved package contract](../docs/templates/packaging-and-acquisition.md).

## Current boundaries

[TemplaterFactory](../src/Func/Templates/Engine/TemplaterFactory.cs) creates a synchronous command-scoped session in the existing CLI-home `templates` hive. [Templater](../src/Func/Templates/Engine/Templater.cs) lists installed entries; catalog/parameter projection and raw constraint reader/matcher/evaluator helpers exist. Constraint-aware command resolution and invocation are not wired; `FuncTemplateComponents.AllComponents` is empty. Project/item kinds use `tags.type`; engine types stay inside the integration.

[TemplateFixtures](../test/Func.Tests/Templates/TemplateFixtures.cs) explicitly registers local content in isolated test hives. [WorkloadDeploymentService](../src/Func/Workloads/Install/WorkloadDeploymentService.cs) deploys workload payloads and store metadata, not production template registration. The working .NET initializer uses a separate `dotnet new` hive. Existing setup must not be described as making template content ready.

For GA, installing or enabling a Stack workload supplies that Stack's curated starter templates and ensures locally available content reaches Installed Templates before the supported template experience is reported ready. This responsibility is approved; the repository mechanism and production integration remain unverified. Package identity, delivery details, versioning, servicing, and future lifecycle experiences remain subject to the package contract and separate owner decisions. Workers and extension bundles retain their workload responsibilities. Discovery does not acquire content, and this gateway authorizes no new registry, package manager, or future lifecycle command UX.
