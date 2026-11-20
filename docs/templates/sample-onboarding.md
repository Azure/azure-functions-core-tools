# Azure Functions sample onboarding naming proposal

This proposal updates the naming in the [historical producer design](archive/azure-samples-template-pipeline/design.md) for future work in `func-templates`. It does not activate the deferred producer design or change the Azure Functions CLI contract.

The onboarding registry contains references to approved source repositories and their release rules, not the sample code. Use **sample onboarding** for the registry, covering both QuickStart and connector samples. Use **template package** for the producer's output, not for the source repository.

| Registry element | Historical name | Proposed name |
|---|---|---|
| Folder for onboarding files | `src/Quickstarts/` | `src/SampleOnboarding/` |
| Onboarding schema | `src/Schema/quickstart.schema.json` | `src/Schema/sample-onboarding.schema.json` |
| Top-level YAML array | `quickstarts` | `samples` |

For example, `src/SampleOnboarding/service-bus.yaml` could contain:

```yaml
schemaVersion: 1
samples:
  - id: service-bus
    repository: Azure-Samples/functions-quickstart-dotnet-azd-service-bus
    packageId: Azure.Functions.Templates.ServiceBusDotNet
    release:
      minimumVersion: 1.2.3
```

The version is illustrative, not an onboarding decision. Use the first eligible published release agreed with the sample owners.

The producer still combines all `.yaml` files directly in the registry folder. Filenames do not define entry identity. Entry fields, schema version, release eligibility, repository and package identity rules, and the source template descriptor remain unchanged. This naming proposal does not define connector monorepo subfolder selection or change staging or publication policy.

If approved, apply the folder, schema, and YAML key changes together in `func-templates`: update the loader, registry files, schema, tests, and directly affected documentation. Until then, the current producer implementation retains its existing names.
