# TemplateDiscovery — vendored template-search index builder

This build-time/ops tool generates the Azure Functions CLI template **search
index** (`NuGetTemplateSearchInfoVer2.json`, format version `2.0`) plus the
companion `nonTemplatePacks.json` skip-list. It scans candidate NuGet packages
with the **real** template engine, so the index reflects exactly what
`func new` can load.

It is **not shipped** in the `func` CLI binary — it is not referenced by
`src/Func/Func.csproj`. It builds in CI only so the index-build path stays
compiling and testable.

## Provenance

This code is **vendored and adapted** from the .NET Templating engine:

- **Source repo:** https://github.com/dotnet/templating
- **Source paths:**
  - `src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/**` (the index builder)
  - `src/Microsoft.TemplateSearch.Common/TemplateSearchCache/*.Json.cs` (the ver2 wire format)
- **Version taken from:** `10.0.302` (matches the engine packages pinned in
  `eng/build/Packages.props`).
- **License:** MIT, Copyright (c) .NET Foundation — the same license and
  copyright this repository already uses. The original headers are preserved on
  every vendored file. **Do not relicense or strip copyright.**

### Re-syncing

When bumping the engine packages, re-check the upstream sources above for the
matching tag/version and reconcile:

- `TemplateEngineHostFactory.cs` ← `TemplateEngineHostHelper.cs`
- `PackageScanner.cs` ← `Filters/TemplateJsonExistencePackFilter.cs` and
  `PackChecking/PackSourceChecker.cs` (`TryGetTemplatesInPackAsync`)
- `DirectoryPackageProvider.cs` ← `TestProvider/TestPackProvider.cs`
- `NuGetFeedPackageProvider.cs` ← `NuGet/NugetPackProvider.cs`
- `SearchCacheStore.cs` ← `Results/UnifiedPackCheckResultReportWriter.cs` and
  `TemplateSearchCache/*.Json.cs`
- `DiscoveryRunner.cs` ← `PackChecking/PackSourceChecker.cs`
- `Program.cs` ← `TemplateDiscoveryCommand.cs`

## What was adapted (differs from upstream)

- **Configurable query + feed (the whole reason we vendored).** Upstream
  hard-codes the nuget.org feed and a fixed query dictionary
  (`packageType=Template`, `q=template`). Here the **feed** is overridable
  (`--feed <url|dir>` / `--packages-path <dir>`) and the **package types** are
  overridable (`--package-type`, default `FuncItemTemplates` + `FuncAppTemplates`).
- **Re-implemented the ver2 writer instead of referencing
  `Microsoft.TemplateSearch.Common`.** That package is not available in the
  offline NuGet cache used for CI, and byte-copying its `Utf8JsonWriter`
  converters would drag in obsolete `BlobStorageTemplateInfo`/legacy paths. The
  writer in `SearchCacheStore.cs` reproduces the exact field ordering and
  omission rules of the upstream ver2 converters, so the output stays
  **interchangeable** with the upstream dotnet template-search ecosystem.
- **Directory metadata from the nuspec.** Upstream's offline `TestPackProvider`
  parses a synthetic `name##version.nupkg` filename convention. Our
  `DirectoryPackageProvider` reads the real id/version/owners/description from
  each package's nuspec via `PackageArchiveReader`.

## Deliberately dropped

- **Legacy v1 metadata writer** (`LegacyMetadataWriter` /
  `NuGetTemplateSearchInfo.json`). The func consumer only reads ver2.
- **Non-Microsoft-author anti-spoofing filter** (`FilterNonMicrosoftAuthors`).
  It exists to police the open nuget.org corpus; the func index is built from
  first-party template packages, so it does not apply.
- **Per-template `Parameters`, `BaselineInfo`, and `PostActions`** in the
  emitted ver2 JSON. These are valid ver2 fields but the func search consumer
  never reads them, so they are omitted to keep the index small. All fields the
  consumer uses (identity, name(s), author, description, classifications, tags,
  owners, version) are preserved.
- **`--test`, `--savePacks`, `--onePage`, `--allowPreviewPacks`, paging-debug**
  switches and the CLI-host `AdditionalData` producer, which are dotnet-CLI
  specific.

## A note on `Console` output

AGENTS.md forbids `Console.WriteLine` in **product** code (it must go through
`IInteractionService`). This project is a standalone ops tool, not product code
shipped in the CLI, so it writes progress to `Console` directly — matching
upstream and every other .NET SDK command-line tool.

## Usage

Build an index from a local directory feed, fully offline:

```pwsh
dotnet run --project tools/TemplateDiscovery/TemplateDiscovery.csproj -- `
  --packages-path artifacts/local-template-feed `
  --output artifacts/template-index
```

This writes `artifacts/template-index/SearchCache/NuGetTemplateSearchInfoVer2.json`
(and `nonTemplatePacks.json`). Point the CLI at it with:

```pwsh
$env:FUNC_CLI_TEMPLATE_SEARCH_INDEX = "artifacts/template-index/SearchCache/NuGetTemplateSearchInfoVer2.json"
func new --search
```

Other sources:

- `--feed <dir>` — same as `--packages-path`, scans a local directory feed.
- `--feed <https://.../v3/index.json>` — queries a remote V3 NuGet feed,
  once per `--package-type`, downloading candidates on demand.

See `eng/scripts/build-template-index.ps1` for a one-command wrapper.
