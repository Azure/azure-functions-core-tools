# Azure Functions CLI: .NET (azd) templates workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Template source

The template content is a verbatim snapshot of the
[functions-quickstart-dotnet-azd](https://github.com/Azure-Samples/functions-quickstart-dotnet-azd)
sample's tracked files (`git ls-files`), authored into a single v2
`NewTemplate` under `content/v2/templates/templates.json`. Every file
is inlined into the template's `files` map and materialised by a
`GetTemplateFileContent` + `WriteToFile` action pair, targeting the
same repo-relative path (e.g. `http/host.json`, `infra/main.bicep`).

Regenerate when the upstream sample changes by re-running the authoring
steps below (the content is static in-repo, so this is a manual,
reviewable refresh rather than a build-time fetch):

1. `git -C <sample-checkout> ls-files` for the authoritative file set.
2. Exclude `.github/prompts/.DS_Store` — the only binary file in the
   tree. The v2 engine writes UTF-8 strings verbatim and cannot
   faithfully reproduce binary content, so it is dropped (macOS Finder
   metadata; not part of a scaffolded project anyway).
3. Read each file with a BOM-stripping UTF-8 decode (the sample's
   `http.csproj` carries a BOM; the engine writes without one) while
   preserving exact newlines.

No token substitution is applied to file bodies: none of the sample
files contain `$(WORD)` v2 tokens (C# interpolation uses `${...}`
braces, Bicep uses `${...}`, GitHub Actions uses `${{...}}` — none
match the engine's `$(NAME)` pattern), so the scaffold reproduces the
sample exactly. The `trigger-functionName` input exists only to satisfy
the CLI's function-name concept; `$(FUNCTION_NAME_INPUT)` is not
referenced by any file.

## No bundle channel

.NET isolated functions have no extension-bundle dependency
(proposed/templates-workload-spec.md §4.4.3), so this workload:

- ships **no** `templates-workload.json` min-bundle manifest,
- has **no** `$(BundleChannel)` / prerelease-label axis,
- does **not** import the bundle-oriented
  `eng/build/Workloads/Workloads.Templates.targets`; it packs
  `content/v2/**/*.json` directly from the csproj.

## Consumption caveat (func new)

The CLI derives a project's **stack** from its worker runtime and lists
templates from `Azure.Functions.Cli.Workloads.Templates.<Stack>`
(`InstalledTemplatesWorkloads` / `TemplatesWorkloadConstants`). This
workload's stack id is `dotnetazd`, which does not match a plain
`dotnet` project's stack, and the existing `Templates.DotNet` workload
(DotNet engine, NuGet hive) already owns the `dotnet` stack. So `func
new` will not auto-surface this template for a standard dotnet project
without CLI-core work to resolve the `dotnetazd` stack. The package is
nonetheless a valid `FuncCliWorkload`: installable via `func workload
install`, and its v2 payload loads via `V2PayloadReader` and applies
via `V2TemplateEngine`. `func new` orchestration lives in the CLI core
and is out of scope for the workload (templates-workload-spec §2).

## Layout

```
<workload-home>/workloads/azure.functions.cli.workloads.templates.dotnetazd/<version>/
  tools/any/content/
    v2/
      bindings/userPrompts.json
      resources/Resources.json
      templates/templates.json   ← single project-scaffold NewTemplate (jobs/actions)
  workload.json                  ← generated (kind: content)
```
