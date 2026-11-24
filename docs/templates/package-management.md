# Template Package Management

**Status:** Proposed; not yet available. This documentation-only layer changes
no CLI behavior. The NuGet catalog, package lifecycle, and CLI integration
layers must ship before the commands below are available. The behavior and
acceptance criteria below describe that proposed implementation, not completed
features or executed validation.

`func templates` would manage template NuGet packages independently of function
generation. `func new` remains the command for adding a function, and `func init`
remains the command for initializing a project. Package management does not
require an initialized project, resolve project compatibility, or acquire
workloads automatically.

## Command surface

```text
func templates search [query] [--source <feed>] [--prerelease] [--format <plain|json>]
func templates install <package-id> [--version <version>] [--source <feed>] [--prerelease]
func templates uninstall <package-id>
```

The initial surface excludes update, folder and local archive installation,
batch operations, package aliases, and advanced search. Package IDs identify
NuGet packages, not individual template identities or short names.

## Feed configuration

Template commands resolve one feed the same way workload commands do. The first
configured value wins:

1. The command's `--source` option.
2. `FUNC_CLI_TEMPLATES_SOURCE`.
3. The nuget.org V3 service index, `https://api.nuget.org/v3/index.json`.

Search and install use that single feed. They do not read `NuGet.Config`, apply
package source mappings, or aggregate several feeds. A broader func CLI
configuration story, including whether to honor user or machine NuGet
configuration, is out of scope for this proposal and would apply to workloads and
templates together.

An explicit source must be an absolute HTTP(S) V3 feed URL, matching workload
source validation. Unsupported sources fail with guidance rather than falling
back to nuget.org.

Reuse the existing NuGet transport and source-validation mechanisms, not the
workload catalog's configuration values; the template and workload feed
variables are independent. Do not add an application configuration file or a
layered configuration pipeline.

Uninstall uses installed package identity and recorded state; it does not
contact or resolve a feed.

## Search

Keep the initial search similar to the basic workload search experience:

- Accept an optional free-text query; omission searches the package catalog.
- Return up to 20 packages from one request. When the page is full, explain that
  more packages may exist and suggest refining the query.
- Render package cards by default, with title, package ID, latest matching
  version, and description. Use the package ID when the title is absent.
- With `--format json`, emit an array containing `packageId`, `latestVersion`,
  `title`, and `description`, without presentation hints in the JSON output.
- Return an empty result successfully when no packages match.

Search targets the `FuncTemplate` NuGet package type using the V3
`packageType=FuncTemplate` parameter. Defensively filter incompatible declared
package types in the response. Feeds that omit package-type metadata may return
unverified candidates; installation always validates the downloaded package.
Do not broaden the search to ordinary dependency packages or workloads.

The search prerelease default follows whether the running CLI is a prerelease
build, as workload search does. `--prerelease` includes prerelease packages.

Do not expand hits into release-channel rows, retrieve every package's version
history, add stack filtering, or search individual templates inside packages.
A feed without a V3 search service produces an actionable failure, not an empty
successful search. Network and malformed-response failures remain failures.

## Install

Without `--version`, resolve the latest stable version by default;
`--prerelease` permits prerelease versions. An explicit `--version` pins an
exact NuGet version, including an explicitly requested prerelease version.
Reject invalid package IDs and versions before changing installed state.
`--version` and `--prerelease` are mutually exclusive: choose either an exact
version or prerelease-inclusive latest-version resolution.

Version resolution stays in `TemplateCatalog` over the resolved feed. The CLI
downloads that exact version to a temporary directory and verifies the archive
in-process before changing installed state. NuGet verifies the content integrity
of a signed archive. Unsigned template packages are accepted, and the official
workload publisher allowlist does not apply. This requires no .NET SDK. If a
package has no stable release, install fails with an error suggesting
`--prerelease`; there is no warning followed by an implicit preview install. Use
`--prerelease` to select the latest preview, or `--version` to request a
specific preview.

Before installation, inspect the downloaded archive. Require the resolved
package ID and version, the `FuncTemplate` package type, and no workload
package type. Require valid `func.host.json` metadata and at least one
template identity. A standard `Template` package type alone is not sufficient
for this initial catalog. Leave archive path safety and template-format
validation to NuGet and Microsoft.TemplateEngine.

Pass the validated local `.nupkg` to Microsoft.TemplateEngine's built-in
managed NuGet provider. The engine owns package registration, storage and
discovery; do not snapshot or rewrite its hive. The engine uninstalls an
existing version before installing its replacement, so keep a temporary copy
of the previous package when replacing. After installation, refresh discovery
and verify the expected template identities. If installation or discovery
fails, use the same managed provider to remove any partially installed version
and reinstall the backup when replacing. If recovery fails, report the
incomplete effects and recovery failure instead of reporting success. Delete
temporary downloads and backups after success, failure, or cancellation.

Installation does not execute authored post-actions or load package-supplied
code components. A valid installed template is not necessarily compatible with
the current project; compatibility remains a selection and generation concern.

Compare installed identity and version; a feed change is not a conflict.
The engine records the archive as a local NuGet package with no `NuGetSource`
detail. Do not add a separate provenance registry. The initial commands do not
use the engine's update or vulnerability-check paths.

| Existing installation | Result |
| --- | --- |
| No managed package with this ID | Install the resolved version. |
| Same ID and version | Report already installed without replacing it, regardless of the feed. |
| Same ID, different version | Replace with the requested version; explicit downgrades are allowed. |

## Uninstall and ownership

Resolve the installed managed NuGet package by package ID and remove that
package's registration and owned content through Microsoft.TemplateEngine.
Refresh discovery before reporting success. An unknown package ID is a user
error rather than an idempotent success.

Workload template roots come from the read-only workload package provider.
`func templates uninstall` must not remove their files, registrations, or
workload inventory. If the supplied ID identifies workload-owned content,
explain that the user must use `func workload uninstall`.

Uninstalling a managed package may reveal a template previously shadowed by
that package. This command does not introduce a new duplicate-template or
precedence policy.

## Engine boundary and failure safety

Use the same func hive and DI-registered engine components as installed-template
discovery. Keep the command-scoped `Templater` boundary limited to engine
primitives; package preflight validation and recovery orchestration belong
outside it. The factory supplies the shared hive path and coordinated discovery
entry point. Keep managed providers, installers, and engine package objects
internal. Do not maintain a second package registry or directly rewrite
Microsoft.TemplateEngine's private persistence schema.

Qualify the pinned engine's install, replacement, uninstall, and cache behavior
with focused integration tests. Replacement must preserve a copy of the old
package for recovery because the engine removes it before installing a new
version.

Archive verification and preflight validation complete before hive mutation.
There is no separate installer or whole-hive snapshot. The service uses the
engine's managed provider for install, uninstall, and recovery. Acquisition,
verification, or preflight failure leaves installed state untouched. Template
operations do not coordinate concurrent CLI processes that share a func home,
matching workload package operations. Avoid running template installs or
uninstalls concurrently against the same home.

If replacement, discovery refresh, or cancellation fails after mutation begins,
use the managed provider to restore the previous package when possible. Report
incomplete effects and recovery failures explicitly; never report success after
a failed registration, refresh, or recovery.

Commands remain thin: parse a typed request, delegate to a service, and render
through `IInteractionService`. Use narrow command-boundary catches for
documented user failures, preserve inner exceptions in `GracefulException`,
and leave unexpected runtime failures distinguishable from user errors.

## Acceptance

Focused coverage must establish:

- Command registration, help, input validation, and operation without a project.
- Dedicated template feed precedence and unchanged workload configuration.
- Search package-type filtering, prerelease behavior, JSON shape, empty results,
  and feed failures.
- Exact-version and latest-version resolution, invalid package rejection, and
  same-version no-op behavior.
- Install visibility through the existing discovery path and removal after
  uninstall.
- Package identity, package-type, `func.host.json` metadata, and nonempty-template
  preflight checks before mutation; engine-managed package storage and fresh
  discovery after temporary download cleanup; signed and unsigned installs
  without invoking an external SDK.
- Single-feed resolution without `NuGet.Config`, source-independent no-op
  installs, and protection of workload-owned content.
- Cancellation, failed replacement with restoration of the prior package,
  cache-refresh failure, recovery failure, and coordinated access to installed
  state.

For the implementation layers, run relevant tests and a strict Release build
before publishing. Failed or
unavailable validation must be reported accurately; it is not release evidence.
