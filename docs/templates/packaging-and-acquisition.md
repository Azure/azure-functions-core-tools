# Template packaging and acquisition contract

**Status:** Proposed for review. Package and template identities, metadata
fields, version policies, and delivery choices below require owner approval.
This document does not implement packages, registration, or template-backed
commands. The [architecture](architecture.md) describes the Template Engine
boundary; the [implementation guide](implementation.md) describes current
infrastructure and remaining work.

## Established boundary

Stack and workload owners and their content producers supply project and item
templates independently of the Template Engine. Workload installation or setup
owns package acquisition and registration in the dedicated CLI template Hive.
The Template Engine discovers installed content from that Hive; discovery does
not download or install packages. Bundled and downloaded packages use the
same validation, registration, discovery, and invocation contract. Production
workload-to-Hive registration is not implemented.

Commands resolve project and Stack context and own destination authorization,
file publication, finalization, and reporting. In the target architecture, the
Template Engine selects installed content, processes supported inputs, generates
privately, and coordinates approved template-defined tasks. The owning workload
supplies Stack-specific compatibility and task behavior. Template-backed
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
| Template package version | The independently versioned NuGet content package; it is also distinct from the workload package version. |

For example, Node.js programming model v4 names neither Node.js version 4 nor
the Functions Host version, even when a supported pairing uses Host v4.

## Proposed package and template identities

The default is one package per Stack and programming model, with multiple
project and item templates where appropriate. The existing Node workload
package is `Azure.Functions.Cli.Workloads.Node`; the proposed content package
has a separate identity:

```text
Azure.Functions.Cli.Templates.<Stack>[.<ProgrammingModel>]
Azure.Functions.Templates.<Stack>.<Language>.<ProgrammingModel>.<Kind>[.<Variant>]
```

Omit the package's model suffix only when the owner confirms one unambiguous
model. Keep an existing published package identity stable if another model is
later added. Template identities always include language and model, even for
single-language Stacks. `Kind` is `Project` or an item name such as
`HttpTrigger`. A stable variant suffix distinguishes different content for
otherwise identical dimensions, including genuinely version-specific content.
An identity is not a substitute for compatibility metadata.

The following examples exercise the currently declared Stack and language
names. They are naming examples, not claims that every model, trigger, or
template is supported or included in the initial contribution. Package
suffixes follow `Azure.Functions.Cli.Templates`; identity suffixes follow
`Azure.Functions.Templates`.

| Canonical Stack | Canonical languages | Example package suffix | Example identity suffix |
| --- | --- | --- | --- |
| `node` | `JavaScript`, `TypeScript` | `Node.V4` | `Node.JavaScript.V4.Project`; `Node.TypeScript.V4.TimerTrigger` |
| `python` | `Python` | `Python.V2` | `Python.Python.V2.QueueTrigger` |
| `dotnet` | `C#`, `F#` | `DotNet.Isolated` | `DotNet.CSharp.Isolated.BlobTrigger`; `DotNet.FSharp.Isolated.Project` |
| `java` | `Java` | `Java` | `Java.Java.Default.Project` |
| `powershell` | `PowerShell` | `PowerShell` | `PowerShell.PowerShell.Default.Project` |
| `go` | `Go` | `Go` | `Go.Go.Default.Project` |

`CSharp` and `FSharp` are identity tokens; `tags.language` remains `C#` or
`F#`. `Default` is a proposed stable token for one owner-confirmed, unnamed
model, not a wildcard or a claim that the model is compatible everywhere.
Owners must approve each model token and the supported matrix; the Stack
declarations alone do not establish the complete model inventory.

For the first Node.js slice, propose:

| Artifact | Proposed identity |
| --- | --- |
| Package | `Azure.Functions.Cli.Templates.Node.V4` |
| JavaScript project | `Azure.Functions.Templates.Node.JavaScript.V4.Project` |
| JavaScript HTTP item | `Azure.Functions.Templates.Node.JavaScript.V4.HttpTrigger` |

Identities are globally unique, case-insensitively, and remain stable after
publication. Exact identity is the initial selection key; an unavailable exact
match must not fall back to another variant. Omit `groupIdentity` for the
unrelated starter pair. Prefer no `shortName` aliases, subject to qualifying
an identity-only package with the pinned Template Engine; if an alias is
required, approve a distinct one rather than silently introducing a shared
alias.

## Metadata and Stack-version compatibility

Each template has its own package-relative root containing
`.template.config\template.json` and all participating assets. The existing
engine reads `tags.type` as `project` or `item` and `tags.language` as the
canonical language. Starter templates are visible: omit `isHidden` from
`.template.config\func.host.json` or set it to `false`.

An illustrative package layout, following existing `content` conventions, is:

```text
content\node-javascript-v4-project\.template.config\template.json
content\node-javascript-v4-project\package.json
content\node-javascript-v4-http\.template.config\template.json
content\node-javascript-v4-http\src\functions\HttpTrigger.js
```

Current CLI workload and template metadata do **not** provide an enforced
language-Stack target-version range. `IProjectStack` exposes Stack and language
names, not versions; `TemplateEngineContext` has no Stack-version or
programming-model field. `workload.json` has no template-package pin or
Stack-version range.
The existing `func-workload` range describes a **workload package version**, not
a Node.js, Python, or other target version. Selection cannot currently evaluate
that constraint because it has no workload inventory; do not add it to the
starter templates. `NodeProjectFactory` does not verify the version of
`@azure/functions` or establish model-v4 compatibility.

**Proposed, not implemented:** add versioned, per-template compatibility
metadata, for example in `func.host.json`:

```json
{
  "compatibility": {
    "schemaVersion": 1,
    "stack": "node",
    "programmingModel": "v4",
    "stackVersionRanges": [ "[20.0.0,23.0.0)" ]
  }
}
```

The range is illustrative, not a Node.js support claim. Proposed optional
`languageVersionRanges` and `hostRuntimeVersionRanges` would be separate from
`stackVersionRanges`. Field names, schema placement, range syntax, and
supported version sources require approval. Numeric NuGet-style ranges could
be used where conversion from the Stack's native version scheme is lossless;
qualified .NET target frameworks and other non-equivalent version schemes need
an owner-defined representation or comparison rule.

For `func init`, the Stack supplies the requested or resolved target; for
`func new`, it supplies the existing project's target and model. Compare a
specific target against the template's range. If a project declares a range
instead, require all intended targets to be supported, not merely an
intersection. Missing, invalid, ambiguous, or unsupported required
compatibility information blocks selection with an actionable diagnostic;
`--force` does not bypass it. Owners must specify normalization, prerelease
handling, and how project declarations and explicit targets are resolved.

The current `func.host.json` reader ignores unknown properties. Merely adding
the proposed fields would therefore be unsafe: do not make range-dependent
content available until strict validation, Stack-owned evaluation, and an
older-CLI capability gate are implemented and qualified. Template metadata
alone does not provide model or version enforcement.

## Package, acquisition, and replacement

**Proposed:** use independent semantic versions and pin the exact template
package ID and version in each workload release on the corresponding release
channel; numeric workload and template versions need not match. Published
package ID/version bytes are immutable. Include author, description, license,
repository, package type, and integrity metadata. Existing
`Microsoft.Azure.Functions.Worker.ProjectTemplates` and
`Microsoft.Azure.Functions.Worker.ItemTemplates` are separate packages marked
with NuGet package type `Template`. Whether the proposed packages are shared
with other hosts or use a CLI-specific marker requires approval and
qualification. A shared package cannot rely on CLI-only compatibility checks
to protect another host; the existing constraint reader rejects `func-*`
constraints when it is given package types identifying a shared `Template`
package.

**Proposed initial delivery:** bundle the pinned Node package with the Node
workload, for example under the installed workload's `tools\any\templates`
directory. A separately acquired package is an alternative; an approved feed
could supply that exact package. Proposed workload release metadata identifies
the package ID, exact version, relative path or approved source, and SHA-256
from trusted release metadata. A possible new `templatePackage` entry would
contain `id`, `version`, `relativePath` or `source`, and `sha256`, plus the
owning Stack and supported languages and models. These names and fields require
approval. Package-level summaries must agree with the per-template compatibility
declarations; they do not replace those declarations. The existing
`workload.json` `packages` map serves RID selection, not template-package
references.

Both delivery paths stage a local package, validate its identity, version,
provenance, integrity, metadata, and assets, then use the same managed Template
Engine package-to-Hive registration operation. Registration completes
before reporting template content ready and runs on install, reuse, repair,
and update paths. It is idempotent and reports missing, corrupt, unsupported,
or incomplete content with a repair path. The CLI Hive is under the configured
func home, for example `%FUNC_CLI_HOME%\templates` on Windows; workload storage
may instead use `FUNC_CLI_WORKLOADS_HOME`.

Stage and validate a replacement before changing the active registration;
retain the previous usable registration where possible and report failure
honestly. Register the package pinned by the active workload, not simply the
highest template package version. Installing or repairing an older workload
must not silently displace the active workload's package. A failed replacement
must not make stale content appear ready for an incompatible active workload.

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

Before reporting the initial Node registration ready, install a real package
through the workload path into isolated workload and CLI Hive homes; reopen
the Hive and find both exact identities once. Repeat install and repair,
check missing or corrupt replacements and prior-registration preservation,
and ensure an older workload cannot displace the active package. Qualify
version boundaries, out-of-range and unknown targets, model mismatches, and
older clients.

Command qualification must verify private generation, authorized file
publication, and a representative `func init` / `func new` / Functions Host
journey. The proposed Node journey creates a function named `hello`, invokes
`GET /api/hello?name=Foo` with any required authorization, and expects
`Hello, Foo!`. GA qualification also checks the approved coverage matrix;
listing or test-only folder registration alone is not proof of package or
command readiness.

Fabio and Sarah, with the workload owners, should approve or revise:

1. Package and template identities, model boundaries and tokens, package type,
   and whether an alias is needed by the pinned engine.
2. The compatibility schema, range semantics, authoritative target/version
   sources, model checks, and older-client failure behavior.
3. Independent version pins, release channels, integrity requirements, and
   replacement, retry, and rollback policy.
4. Initial bundled versus separately acquired delivery, and the proposed
   workload metadata and relative package path.
5. The GA support matrix and which content is included by default versus
   obtainable separately.
6. The Node starter's supported Stack versions, parameter subset, and HTTP
   authorization default.
