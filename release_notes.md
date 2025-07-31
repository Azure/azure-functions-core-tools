# Azure Functions CLI <version>

#### Host Version

- Host Version: <version>
- In-Proc Host Version: <version>

#### Changes

- Fix dotnet templates installation (#4538)
- Disable diagnostic events in local development by replacing the `IDiagnosticEventRepository` with a `DiagnosticEventNullRepository` (#4542)
- Add `func pack` support for in-proc functions (#4529)
- Default to remote build for `func pack` for python apps (#4530)
- Add `--build-local` option for `func pack` for Python apps only. (#4550)
  - Default to local build for Python apps on windows to avoid breaking existing behavior
  - Update logging for `func pack` to include more details
  - Include a 'Deferred' build option for `func pack` that is ready for remote deployment

