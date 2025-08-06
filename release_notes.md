# Azure Functions CLI 4.1.1

#### Host Version

- Host Version: 4.1040.300
- In-Proc Host Version: 4.40.100

#### Changes

- Fix dotnet templates installation (#4538)
- Disable diagnostic events in local development by replacing the `IDiagnosticEventRepository` with a `DiagnosticEventNullRepository` (#4542)
- Add `func pack` support for in-proc functions (#4529)
- Update KEDA templates & `kubernetes create` command to correctly use a provided namespace, or use default namespace (#4558)
- Update `func init` to default to the .NET 8 template for in-proc apps (#4557)
- Implement (2 second) graceful timeout period for the CLI shutdown (#4540)
- Overwrite `AZURE_FUNCTIONS_ENVIRONMENT` to `Development` if it is already set (#4563)
- Warn if there is a `JsonException` when parsing the `local.settings.json` file (#4571)
- Add support for .NET 10 isolated model (#4589)
- Enabled verbose logs in MSI by default (#4578)
