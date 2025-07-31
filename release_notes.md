# Azure Functions CLI <version>

#### Host Version

- Host Version: <version>
- In-Proc Host Version: <version>

#### Changes

- Fix dotnet templates installation (#4538)
- Disable diagnostic events in local development by replacing the `IDiagnosticEventRepository` with a `DiagnosticEventNullRepository` (#4542)
- Add `func pack` support for in-proc functions (#4529)
- Default to remote build for `func pack` for python apps (#4530)
- Update `func init` to default to the .NET 8 template for in-proc apps (#4557)
- Overwrite `AZURE_FUNCTIONS_ENVIRONMENT` to `Development` if it is already set (#4563)
