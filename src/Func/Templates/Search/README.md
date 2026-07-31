# Template search — vendored consumer

This folder contains the func CLI's template **search index consumer**. It is a
**vendored, adapted port** of the Microsoft templating engine's search provider,
kept in-repo (rather than consuming the upstream NuGet package) so that the
index URI is fully overridable — including pointing at a **local file** for
offline use.

## Provenance

- **Upstream repo:** `dotnet/templating`
- **Upstream project:** `Microsoft.TemplateSearch.Common`
  (`src/Microsoft.TemplateSearch.Common/**`)
- **Key upstream types referenced:**
  - `Providers/NuGetMetadataSearchProvider.cs` — remote index download, 1h
    freshness, ETag / `If-None-Match` revalidation, stale-cache fallback,
    `DOTNET_NEW_SEARCH_FILE_OVERRIDE` / `DOTNET_NEW_LOCAL_SEARCH_FILE_ONLY`
    environment overrides.
  - `TemplateSearchCache.Json.cs`, `TemplatePackageSearchData.Json.cs`,
    `TemplateSearchData.Json.cs` — the `NuGetTemplateSearchInfoVer2.json`
    (version `"2.0"`) wire format.
- **Taken from version:** `10.0.302` (the engine version this repo pins in
  `eng/build/Packages.props`).
- **License:** MIT, Copyright (c) .NET Foundation — the same license and header
  this repo already uses, so the original headers are preserved on the ported
  files unchanged.

## What was adapted (and why)

The whole reason to vendor is that upstream hard-codes two `fwlink` index URIs
and only lets tests override them through an `internal` constructor. Our copy:

- **Makes the index location a first-class override.** Resolution priority:
  1. an explicit **local file path** (`FUNC_CLI_TEMPLATE_SEARCH_INDEX` pointing
     at a file, or a `file://` URI) — served **fully offline**, no network call;
  2. an explicit **URI** override (same env var pointing at an `http(s)` URL);
  3. the default func index URI
     (`FuncTemplateSearchOptions.DefaultIndexUri`, an `aka.ms` vanity URI → the
     Functions CDN, D29).
- **Adds `FUNC_CLI_TEMPLATE_SEARCH_LOCAL_ONLY`** (mirrors upstream
  `DOTNET_NEW_LOCAL_SEARCH_FILE_ONLY`): never download, use only a cached copy.
- **Reads overrides from environment variables only**, bound once into
  `FuncTemplateSearchOptions` at registration time
  (`TemplateSearchRegistration`). AGENTS.md forbids `appsettings.json` / layered
  `IConfiguration`; business logic never touches the environment directly.
- **Parses the ver2 format with `System.Text.Json`** (`FuncSearchIndexReader`)
  instead of upstream's `Newtonsoft.Json` `JObject` model, matching this repo's
  JSON stack. The property names and the string-or-array `Owners` shape are kept
  identical so the format stays interchangeable with the wider ecosystem.
- **Mirrors this repo's own caching blueprint** (`Quickstart/`
  `QuickstartManifestService`): named `HttpClient`, `TimeProvider`-driven
  freshness, ETag revalidation, stale fallback with a warning, and an
  **actionable** error when the index is unreachable and no cache/local copy
  exists.

## What was intentionally dropped

- Upstream's legacy **v1 metadata** reader path — we only read/write ver2
  (`"2.0"`).
- The engine-`IEngineEnvironmentSettings`-coupled provider plumbing — the
  consumer is a plain `System.Text.Json` reader plus an HTTP/cache seam, so it
  is testable without standing up the templating engine.

## Re-syncing

When bumping the pinned engine version, re-read the upstream files listed above
for wire-format or caching-behaviour changes and reconcile them here. The
format contract lives in `FuncSearchIndexReader` and must stay compatible with
the index the discovery tool writes (`tools/TemplateDiscovery`).
