# template-search

Template discovery: the func-owned index-building service (based on
`Microsoft.TemplateSearch.TemplateDiscovery`) and the CLI's search
surface. (Design: D22, D24, D26; design.md §2A.)

## ADDED Requirements

### Requirement: Discovery service builds the func template index
A func-owned discovery pipeline SHALL scan configured NuGet feeds
(nuget.org at minimum; local paths and other feed sources supported) for
packages declaring the `FuncItemTemplates` or `FuncAppTemplates` package
types, prefilter candidates for `template.json` presence, scan them with
the Microsoft templating engine, and publish a search index in the
engine's search-cache format (`NuGetTemplateSearchInfoVer2.json`),
including per-template metadata (identity, shortNames, tags, package id +
version). Runs SHALL be incremental (unchanged package versions are not
rescanned; known non-template packages are skipped via a persisted
skip-list).

#### Scenario: Community package appears in the index
- **WHEN** a publisher pushes a package with the `FuncItemTemplates`
  package type containing valid templates to nuget.org and a discovery
  run completes
- **THEN** the package's templates appear in the published index with
  their metadata

#### Scenario: Non-template package excluded
- **WHEN** a scanned package contains no loadable `template.json`
- **THEN** it is excluded from the index and recorded in the skip-list
  for future runs

### Requirement: CLI search over the published index
`func new --search [term]` SHALL query the func-published index
(downloaded from the configured index URI, cached locally, with a
local-file override for air-gapped scenarios) and render matching
templates/packages with enough metadata to install (package id, version,
template names, stack/language tags). Search SHALL degrade with an
actionable error when the index is unreachable and no cached/local copy
exists.

#### Scenario: Search by term
- **WHEN** `func new --search kafka` runs with index connectivity
- **THEN** matching templates are listed with their package ids so the
  user can run `func new --install <pkg>`

#### Scenario: Local index override
- **WHEN** a local index file override is configured
- **THEN** search runs fully offline against that file

### Requirement: Direct feed search via --source
`func new --search --source <feed>` SHALL additionally query the
specified feed's NuGet search API directly at invocation time, filtered
to the func package types — covering local/private feeds the discovery
service never indexed.

#### Scenario: Private feed searched directly
- **WHEN** `func new --search --source https://myfeed/v3/index.json` runs
- **THEN** results come from that feed's live search API, filtered to
  func template package types

### Requirement: Search results distinguish installed state
Search output SHALL indicate which results are already installed (and at
which version), so users can distinguish discover-and-install from
already-available templates, consistent with the offline rule that
installed templates are the only scaffoldable set.

#### Scenario: Installed package flagged
- **WHEN** a search result's package is already installed at 1.0.0 and
  the index lists 1.2.0
- **THEN** the result shows it as installed with an update available
