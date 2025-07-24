# Azure Functions CLI <version>

#### Host Version

- Host Version: <version>
- In-Proc Host Version: <version>

#### Changes

- Fix dotnet templates installation (#4538)
- Disable diagnostic events in local development by replacing the `IDiagnosticEventRepository` with a `DiagnosticEventNullRepository` (#4542)
- Add `func pack` support for in-proc functions (#4529)
- Default to remote build for `func pack` for python apps (#4530)
- Implement (2 second) graceful timeout period for the CLI shutdown (#4540)
