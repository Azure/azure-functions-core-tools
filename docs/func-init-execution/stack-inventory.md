# Installed stack metadata inventory

This is the additive metadata step for [#5648](https://github.com/Azure/azure-functions-core-tools/issues/5648).
It describes current `vnext` initializers, not the future template parameters.

## Current stacks and init options

Canonical stack IDs and language labels are preserved. Language aliases and
worker runtime aliases are separate: for example, `pwsh` is a language alias,
not an alias for `--stack powershell`.

| Stack ID / display name | Canonical languages (aliases) | Additional worker runtime aliases | Workload-contributed init options |
| --- | --- | --- | --- |
| `dotnet` / .NET | `C#` (`csharp`), `F#` (`fsharp`) | `dotnet-isolated` | `--target-framework` / `-tfm` (default and supported value: `net10.0`) |
| `node` / Node.js | `JavaScript` (`js`), `TypeScript` (`ts`) | None | Bundle options; `--skip-npm-install` (default: false) |
| `python` / Python | `Python` (`py`) | None | Bundle options |
| `go` / Go | `Go` (`golang`) | `native` | Bundle options; `--skip-go-mod-tidy` (default: false) |
| `java` / Java | `Java` (none) | None | Bundle options |
| `powershell` / PowerShell | `PowerShell` (`pwsh`, `ps`) | None | Bundle options; `--managed-dependencies` (default: false); `--runtime-version` (default: `7.4`; supported: `7.4`, `7.6`) |

Bundle options are shared through `CommonInitOptions` and `InitOptionRegistry`:
`--no-bundles` (default: false) and `--bundles-channel` / `-c`
(`Stable` by default, also `Preview` and `Experimental`). They appear once in
help even when multiple workloads contribute them.

`InitCommand` still owns `[path]`, `--stack` / `-s`, `--language` / `-l`,
`--name` / `-n`, and `--force`, in addition to inherited/global options.
Stack and language matching remains case-insensitive. Multi-language stacks
prompt for a language; single-language stacks select their sole language.
The .NET, Node and Python initializers also normalize language aliases when
called directly; their direct-call defaults are C#, JavaScript and Python.

Current scaffolding remains unchanged: .NET runs `dotnet new func` using
`Microsoft.Azure.Functions.Worker.ProjectTemplates` and a private template hive;
Node generates v4-model JS/TS files and optionally runs `npm install`;
Python generates v2-model files; Go writes worker SDK files and optionally runs
`go mod tidy`; Java writes Maven/sample HTTP-trigger files; PowerShell writes
profile/runtime files and optionally resolves the Az module for managed
dependencies. Non-.NET stacks write a default extension bundle unless disabled.

## Additive contract and catalog

`Abstractions/Projects/IProjectStack` is public because separately shipped
workloads implement it. Its only members are `Stack`, `DisplayName`,
`WorkerRuntimeAliases`, `SupportedLanguages`, and `SupportedLanguageAliases`.
It owns no command options, validators, template acquisition, or scaffolding.
Each stack registers a singleton metadata implementation alongside its existing
`IProjectInitializer`; the initializer delegates its metadata to that same
instance so the two contracts cannot drift.

The CLI registers `InstalledProjectStackCatalog` as a singleton over DI's
`IEnumerable<IProjectStack>`. It validates on resolution, not host startup,
rejecting duplicate stack IDs case-insensitively and ambiguous stack/runtime
or language/alias mappings rather than choosing the first registration.
Malformed metadata is an argument error; ownership conflicts are
`InvalidOperationException`s. `FindStack` accepts stack IDs/runtime aliases;
`GetCanonicalLanguage` accepts language labels/aliases; `GetStacksForLanguage`
returns **all** owners of that canonical language in registration order,
including owners that do not declare the particular alias supplied.
Lookups trim input and ignore case, return null/an empty list for unknown
values, and reject null/blank input. Metadata names must be nonblank and trimmed.

Existing consumers (`InitCommand`, template option hydration, and related
initializer registrations) stay on the old interface until the later command
switch. This step does not introduce template orchestration or initializer
fallback policy. The [quickstarts design](../func-init-quickstarts/design.md)
remains authoritative for future template-first, multi-project initialization.

## Release order

**Ship a CLI containing the new Abstractions contract before shipping any
stack workload that references `IProjectStack`.** These workload binaries are
not safe to load into an older CLI. CLI/workload compatibility gating
([#5413](https://github.com/Azure/azure-functions-core-tools/issues/5413)) is
unresolved; this step does not add a gate. Release coordination must enforce
the order, and mixed old/new installations expose metadata only for workloads
that register the new contract while current initializer behavior is retained.

Template acquisition (#5649), first-party template publishing, runtime
integration, and the `func init` switch are separate work. The switch must wait
for usable template packages and its regression coverage; none is required
to register and inspect this metadata.
