# Template Package Management

`func templates` manages template NuGet packages independently of function
generation. `func new` remains the command for adding a function, and `func init`
remains the command for initializing a project. Package management does not
require an initialized project, resolve project compatibility, or acquire
workloads automatically.

## Command surface

```text
func templates search [query] [--source <feed>] [--prerelease] [--json]
func templates install <package-id> [--version <version>] [--source <feed>] [--prerelease]
func templates uninstall <package-id>
```

The initial surface excludes update, folder and local archive installation,
batch operations, package aliases, and advanced search. Package IDs identify
NuGet packages, not individual template identities or short names.

## Feed configuration

An optional additional feed is selected in this order:

1. The command's `--source` option.
2. `FUNC_CLI_TEMPLATES_SOURCE`.

Installation uses enabled sources from ambient `NuGet.Config` for the current
directory, with NuGet package source mappings, plus the optional additional
feed. Neither option replaces or isolates the configured feeds. With no
additional feed, installation uses only ambient configuration; it does not
inject nuget.org. Additional feeds follow the engine's source-mapping semantics.

Search retains its bounded single-feed behavior: search the additional feed
directly when supplied, otherwise search the nuget.org V3 service index,
`https://api.nuget.org/v3/index.json`. It does not aggregate ambient feeds.

An explicit source must be an absolute HTTP(S) V3 feed URL. Unsupported sources
fail with guidance rather than falling back to nuget.org.

For installation, HTTP feeds are filtered out like `dotnet new install`;
use HTTPS instead. The CLI does not set the engine's force flag to bypass
filtering, including during replacement.

Reuse the existing NuGet transport and source-validation mechanisms, not the
workload catalog's configuration values. Do not add an application configuration
file or a layered configuration pipeline.

Uninstall uses installed package identity and recorded state; it does not
contact or resolve a feed.

## Search

Keep the initial search similar to the basic workload search experience:

- Accept an optional free-text query; omission searches the package catalog.
- Return up to 20 packages from one request. When the page is full, explain that
  more packages may exist and suggest refining the query.
- Render package cards by default, with title, package ID, latest matching
  version, and description. Use the package ID when the title is absent.
- With `--json`, emit an array containing `packageId`, `latestVersion`, `title`,
  and `description`, without presentation hints in the JSON output.
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

Version resolution stays in `TemplateCatalog` over the ambient and additional
install feeds. The CLI downloads that exact version to a temporary directory,
verifies the staged file in-process, holds it open read-only, and passes the
local `.nupkg` to the built-in engine NuGet installer. Package integrity and any
configured `trustedSigners` are always enforced. Certificate-chain trust follows
NuGet's restore policy for the configured `signatureValidationMode`: `accept`
tolerates roots the operating system does not trust, because Linux and macOS
system stores lack code-signing roots outside the .NET SDK's bundled
certificates; `require` rejects them unless a trusted signer sets
`allowUntrustedRoot`. Unsigned template packages are accepted unless
`NuGet.Config` requires signing; the official workload publisher allowlist does
not apply. This requires no .NET SDK. If a package has no
stable release, install fails with an error suggesting `--prerelease`; there is
no warning followed by an implicit preview install. Use `--prerelease` to
select the latest preview, or `--version` to request a specific preview.

Inside the rollback-protected install transaction:

1. Snapshot the hive, remove the previous version if replacing, and install the
   verified download through Microsoft.TemplateEngine's built-in NuGet installer.
   For a local `.nupkg`, the engine copies the archive into the hive; unlike
   folder installation, it does not retain the original path. Delete the
   temporary download after success, failure, or cancellation.
2. Open the installed `.nupkg` at its managed package mount point and verify its
   package ID and version match the resolved request.
3. Inspect its NuGet metadata. Require `FuncTemplate`, and reject packages that
   also declare a workload package type. A standard `Template` package type
   alone is not sufficient for this initial catalog.
4. Validate template declarations and content, including unsafe package paths
   and malformed declared func host metadata.
5. Require at least one unique template declaration.
6. Refresh discovery and verify that Microsoft.TemplateEngine exposes exactly
   the validated template identities before reporting success. There is no
   separate pre-install engine scan; failed discovery restores the previous hive.

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
| Same ID and version | Report already installed without replacing it, regardless of the additional feed. |
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
primitives; package orchestration and identity validation belong outside it.
The factory supplies the shared hive path and coordinated discovery entry point.
Keep managed providers, installers, and engine
package objects internal. Do not maintain a second package registry or directly
rewrite Microsoft.TemplateEngine's private persistence schema.

Qualify the pinned engine's install, replacement, uninstall, and cache behavior
with focused integration tests. In particular, do not assume that replacing a
managed package preserves the old installation if acquisition fails.

The temporary download and signature verification complete before hive mutation.
There is no separate installer. Validation inspects the installed archive before
transaction commit; a validation or discovery failure restores the previous hive.
Acquisition or signature failure leaves the hive untouched. Template operations do not
coordinate concurrent CLI processes that share a func home, matching workload
package operations. Avoid running template installs or uninstalls concurrently
against the same home.

If replacement or discovery refresh fails, preserve or restore the previous
installation. Report incomplete effects and recovery failures explicitly;
never report success after a failed registration, refresh, or recovery.
Cancellation follows the same recovery requirements once mutation has begun.

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
- Archive ownership in the hive and fresh discovery after temporary download
  cleanup; signed installs without invoking an external SDK; chain trust under
  `accept` and `require` signature validation modes.
- Ambient plus additional feed behavior, source-independent no-op installs, and protection of workload-owned content.
- Cancellation, failed replacement, cache-refresh failure, recovery failure,
  and coordinated access to installed state.

Run relevant tests and a strict Release build before publishing. Failed or
unavailable validation must be reported accurately; it is not release evidence.
