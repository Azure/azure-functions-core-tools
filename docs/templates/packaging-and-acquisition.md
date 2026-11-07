# Template packaging and acquisition contract

**Status:** Proposed for review. Package and template identities, compatibility
policy, versioning, and delivery choices require owner approval. This document
does not implement packages, registration, or template-backed commands. The
[architecture](architecture.md) describes the Template Engine boundary; the
[implementation guide](implementation.md) describes current infrastructure
and remaining work.

## Established boundary

Stack and content owners provide project and item templates independently of the
Template Engine. Workload installation or setup is responsible for making its
declared content available and coordinating registration in the dedicated CLI
template Hive. It may delegate installation to existing Template Engine
facilities; which component acquires a separately published package is an
implementation decision. Discovery reads installed content and does not
download or install it. Production workload-to-Hive registration is not
implemented.

Commands resolve project and Stack context and own destination authorization,
file publication, finalization, and reporting. The owning Stack interprets its
projects and supplies compatibility information. The one Template Engine
selects installed content, processes supported inputs, and generates files
privately. The CLI separately validates and writes those files. Template-backed
command paths are not implemented.

In the template-backed architecture, `func init` selects project templates and
`func new` selects item templates. Quickstarts are project templates, not a
third template kind. Existing Stack initializers remain in place until their
replacements are qualified. The existing .NET initializer's separate
`dotnet new` Hive is not the CLI Hive.

## Version vocabulary

| Term | Meaning |
| --- | --- |
| Language Stack | The workload ecosystem, such as `node`, `python`, or `dotnet`. |
| Language and language version | The source language and, when relevant, its dialect or compiler version, such as TypeScript and its compiler version. |
| Stack version | The project's target language platform, runtime, or toolchain version, such as Node.js 20, Python 3.11, or a .NET target framework. It is not necessarily the tool installed on the current machine. |
| Programming model | The function authoring/API model, such as Node.js model v4, Python model v2, or .NET isolated. |
| Functions Host runtime | The Functions Host version, independent of the language Stack and its programming model. |
| Template package version | The version of a separately packaged NuGet template package, distinct from the workload package version. A bundled directory has no independent package version. |

For example, the `v4` in the Node.js programming model v4 refers only to the
Functions programming model. It does not mean Node.js version 4 or Functions
Host version 4.

## Template identity and discovery

A package or installed directory can contain several templates, including
templates for different programming models. Choose package boundaries based
on installation, ownership, versioning, and servicing needs, not a rule that
requires a separate package for each model. Each template needs a distinct,
stable identity and enough supported metadata to select the right variant.

For the first Node.js slice, the following are previous proposals, not
approved names or a decision that this package is limited to one model:

| Artifact | Proposed identity |
| --- | --- |
| Package | `Azure.Functions.Cli.Templates.Node.V4` |
| JavaScript project | `Azure.Functions.Templates.Node.JavaScript.V4.Project` |
| JavaScript HTTP item | `Azure.Functions.Templates.Node.JavaScript.V4.HttpTrigger` |

`V4` in these examples means the Node.js programming model. If the owners
choose a package that spans models, they should reconsider the package suffix.

Each template has a root containing `.template.config\template.json` and its
assets. `tags.type` identifies a `project` or `item`; `tags.language` names the
actual source language, such as `JavaScript`, `C#`, or `F#`. Language is
independent of Stack and programming model; `Default` is not a language.

Each eligible template must declare at least one nonempty `shortName` in
`template.json` so it can be discovered and selected through the CLI. Retain
established names where they exist; content owners should choose new names.
This requirement does not make `groupIdentity` mandatory or require globally
unique short names; the engine can report ambiguity. Use the Template Engine's
[`groupIdentity`][template-reference] for explicitly related variants, and
coordinate common short names and groups across Stacks where the templates
represent the same choice. For example, a shared `httptrigger` short name could allow
`func new --template httptrigger` to select an eligible item using the project
context. This is a proposed selection behavior, not a new positional
command syntax or a claim that it works today. Grouped listings can show
available Stack/language variants. Existing .NET HTTP templates already use a
shared `groupIdentity` and the `http` short name.

For a request known to be an explicit full identity, selection must use that
exact template; an unavailable or incompatible identity must not fall back to
a sibling. The current general string resolver checks identities first but
also tries short names when no identity matches, so it is not by itself an
exact-only guarantee. A short name can match multiple candidates: use project
context and supported constraints, then prompt or report ambiguity rather than
selecting arbitrarily. Group only explicitly related variants, not the
unrelated project and item templates. The current catalog exposes kind,
language, short names, and group identity; cross-Stack filtering and grouped
command output still need qualification.

Keep `.template.config\func.host.json` for CLI-specific behavior, such as
visibility and option/argument mappings. Starter templates are visible by
default; omit `isHidden` or set it to `false`.

## Compatibility and versions

Prefer the existing `template.json` [constraints][constraints] and tags over a
parallel compatibility schema. Evaluate supported constraints with the
Template Engine and use Stack-owned project information for facts about the
target project. The CLI may apply constraints during candidate selection;
the ordering used by `dotnet new` need not determine the CLI's selection
policy. Required constraints that are invalid, unsupported, or cannot be
evaluated must not silently become compatible selections, including when
`--force` is used.

Existing constraints have specific meanings. For example, `host` identifies
the application hosting the Template Engine and its version; it is not the
Functions Host runtime version. The standard `sdk-version` and `workload`
constraints refer to the .NET SDK and its workloads. The existing
`func-workload` helper describes a CLI **workload package version**, not the
project's Node.js version; the current selection context lacks a workload
inventory to evaluate it. A standard constraint is usable by the CLI only
when the corresponding evaluator is available in its Template Engine host;
the existence of the definition does not establish that support.

The current catalog does not expose a Stack or programming-model tag, and
`TemplateEngineContext` does not carry a target Stack version or programming
model. `NodeProjectFactory` recognizes Node projects but does not verify the
`@azure/functions` major version or establish model-v4 compatibility.
Consequently, neither tags nor the available constraints currently establish
all Node project compatibility. Owners must agree on the project evidence and
how supported engine metadata is evaluated before enabling content that
depends on those checks. Add another constraint or metadata field only for a
demonstrated requirement with a real consumer; no new schema is proposed here.

Where an existing constraint supports the relevant version type, prefer one
range in that constraint's supported format. No requirement for multiple
disjoint ranges has been established. If one arises, evaluate the engine's
existing alternative semantics or distinct variants before considering a new
representation or parser. Do not equate .NET target frameworks, Node.js
versions, programming models, and Functions Host versions.

Shared `Template` packages must also work in their other intended hosts.
The current constraint reader can reject `func-*` constraints when package
type information identifies a shared package, but current resolution does not
provide that information to the reader. Do not assume CLI-specific constraints
or this conditional check make a shared package compatible with other hosts.

[constraints]: https://github.com/dotnet/templating/wiki/Constraints
[template-reference]: https://github.com/dotnet/templating/wiki/Reference-for-template.json

## Package, acquisition, and replacement

Content format, workload availability, local registration, and discovery are
separate concerns:

1. **Content format:** Template roots may be delivered in an approved package
   or a bundled directory. Neither NuGet packaging nor a directory is required
   by this proposal.
2. **Availability:** The workload may carry bundled content or make an
   approved package available. The production delivery format, workload
   declaration, acquisition responsibility, versioning, and servicing policy
   remain owner decisions.
3. **Registration:** Once content is available locally, register it through
   the supported Template Engine installation mechanism in the dedicated CLI
   Hive. The existing `TemplateFixtures.InstallAsync` demonstrates folder
   registration into an isolated Hive; it does not establish a production
   workload registration path or remote package acquisition.
4. **Discovery:** Regardless of delivery format, the Template Engine discovers
   content from the same CLI Hive. Discovery does not acquire or install
   content.

The existing isolated folder-registration seam accepts an `InstallRequest`
for a directory containing multiple template roots, for example:

```text
tools\any\templates\project\.template.config\template.json
tools\any\templates\http\.template.config\template.json
```

The current `workload.json` does not declare template directories or template
package dependencies; its `packages` map selects RID-specific workload
implementations. A workload therefore needs a way to declare its template
source, but its representation and registration hook need owner agreement.
Resolve a declared relative path within the installed workload rather than
scanning arbitrary directories. A bundled directory has no independent
template-package version; its provenance includes the containing workload
version.

Alternatively, a workload may depend on a separately published template
package. Reuse an existing package/provider installation mechanism where it
meets the need; whether workload installation or the Template Engine provider
performs acquisition, and how the package is referenced, remain decisions.
`dotnet new install` supports installing from a folder or NuGet package ID,
but that does not by itself prove the embedded CLI provider can acquire from
a feed. The proposed workload meta-package design groups workload/content
packages. The current workload package inspector expects `FuncCliWorkload`
package metadata and `workload.json`, so an ordinary `Template` package does
not by itself meet that workload contract. Support for such a package as a
meta-package member has not been established. Do not add a separate
installation framework before evaluating these options.

**Proposed production contract, not implemented:** whichever delivery path is
approved, make validated content available and register it in the CLI Hive
before reporting it ready. Registration should be repeatable and report
missing, corrupt, unsupported, or incomplete content rather than reporting
success. The operation should run on the relevant install, reuse, repair, and
update paths. The CLI Hive is under the configured func home, for example
`%FUNC_CLI_HOME%\templates` on Windows; workload storage may instead use
`FUNC_CLI_WORKLOADS_HOME`. This proposal does not implement production
registration, remote acquisition, update, or repair.

**Replacement remains an owner decision.** A bundled directory follows its
workload release; a separately versioned template package may have its own
version. Owners must decide version coordination, pinning, source and integrity
validation, and replacement behavior. Do not assume workload and template
package version numbers match or that selecting the highest installed package
corresponds to the active workload. Any approved replacement path should
validate new content before switching registrations, retain prior usable
content where possible, and report failures without presenting incompatible
stale content as ready.

## Existing .NET templates

The existing `Microsoft.Azure.Functions.Worker.ProjectTemplates` and
`Microsoft.Azure.Functions.Worker.ItemTemplates` NuGet packages use package
type `Template` and contain multiple template roots. The project package
already groups C# and F# variants under the `func` short name. The existing
.NET initializer installs the project package with `dotnet new install` into
a separate .NET template Hive and invokes `dotnet new`; it does not register
the package in the CLI Hive. Node initialization currently uses its own
scaffold instead.

Reuse the established package format and engine mechanisms where appropriate,
but do not assume a .NET Hive installation is visible to the CLI Hive.
Existing .NET templates also use template features and post-actions that need
separate qualification before the proposed private CLI invocation can run
them.

## Content coverage and first contribution

**Proposed GA expectation:** Stack owners maintain an approved matrix of
supported Stack, language and language version where relevant, Stack version,
programming model, Functions Host runtime, and trigger combinations. For every
compatible matrix entry, provide at least one `func init` project template and
a `func new` item template for every supported trigger. One template may cover
multiple entries. Missing content is a GA coverage gap, not a reason to
customize the engine.

The default installation may remain a bounded starter set. Remaining GA
templates must be obtainable through a supported, documented acquisition path;
if optional acquisition is not ready, the required content needs another
supported delivery path.

The first implementation is limited to the Node.js JavaScript model-v4
project and HTTP item. The project follows the existing Node scaffold and
`@azure/functions` v4 API model, with a portable `package.json` `main` glob
matching the JavaScript items under `src\functions`; the item uses `app.http()`.
Bind the built-in generation-time `name` to the function name using `sourceName`;
do not add a second function-name option. The HTTP query parameter `name` is
runtime input. Keep initial parameters unconditional; a fixed HTTP `authLevel`
of `function` is proposed, not approved. Do not use authored post-actions,
additional configuration files, or unsupported custom operations.

## Qualification and decisions

Before reporting the initial Node registration ready, install the chosen
workload content into isolated workload and CLI Hive homes; reopen the Hive and
find both exact template identities once. Repeat install and repair, check
missing or corrupt content and replacement behavior, and ensure the active
workload does not silently use incompatible stale content or lose its
registration when an older workload is installed. Qualify the selected
constraints and project-compatibility checks, including unknown and
unsupported targets.

Command qualification must verify private generation, authorized file
publication, and a representative `func init` / `func new` / Functions Host
journey. The proposed Node journey creates a function named `hello`, invokes
`GET /api/hello?name=Foo` with any required authorization, and expects
`Hello, Foo!`. GA qualification also checks the approved coverage matrix;
listing or test-only folder registration alone is not proof of package or
command readiness.

CLI, workload, and template content owners should approve or revise:

1. The Node template identities, shared short names/groups, and whether a
   separate content package is needed.
2. The project evidence, supported engine constraints/tags, and behavior for
   unknown or incompatible model and version information.
3. Bundled directory versus separately acquired package, workload source
   declaration, acquisition responsibility, and whether meta-package reuse
   satisfies the requirement.
4. Version coordination, integrity, replacement, retry, and repair policy.
5. The GA support matrix and which content is included by default versus
   obtainable separately.
6. The Node starter's supported targets, parameters, and HTTP authorization
   default.
