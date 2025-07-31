# Azure Functions CLI 4.2.2

#### Host Version

- Host Version: 4.1041.200
- In-Proc Host Version: 4.41.100 (4.841.100, 4.641.100)

#### Changes

- Fix .NET template install bug (#4612)
- Fix dotnet templates installation (#4538)
- Disable diagnostic events in local development by replacing the `IDiagnosticEventRepository` with a `DiagnosticEventNullRepository` (#4542)
- Add `func pack` support for in-proc functions (#4529)
- Update `func init` to default to the .NET 8 template for in-proc apps (#4557)
