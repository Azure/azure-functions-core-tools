# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

## Contract boundaries

[Project compatibility checks for function templates](../template-engine-constraints/design.md)
are a separate read-only boundary. Their public stack-reporting contracts and
internal compatibility results do not expose Microsoft.TemplateEngine types.

## Internal package-backed implementation

The internal package template application joins local package intake, exact
selection and parameter parsing, private native rendering, actual-output
capture, and a retained Prepared Operation. Callers explicitly compose it with
`AddNativePackageTemplates`. Public startup registers the shared services with
no package binding enabled in normal builds. The candidate graph's
package-selection options remain internal inputs, not public command arguments.

Item operations request capability inspection through the existing project
resolution contexts. This intent defaults to false. Node inspects the bounded
direct-source JavaScript CommonJS profile only when requested, preserving
ordinary classification, adoption, and Host preparation. Inspection errors
propagate; unrecognized profiles provide no capability report. Reports are
captured once into the resolved `FunctionsProject` and evaluated by the
synchronous Foundation matcher. There are no separate facts envelopes,
fingerprints, capture IDs, or project-freshness callbacks.

Original native metadata is validated before rendering, including retained
requirement-parser failures. Internal-role items cannot execute through the
ordinary path; hidden ordinary items remain explicitly invocable. Parameter
provenance and exact package ownership remain bound to retained output.

Preview does not consume publication. Commit consumes one attempt, publishes
immutable captured bytes without replacing existing destinations, and reports
cancellation, partial effects, and cleanup failures independently. Publication
path and destination safety remain separate from project compatibility.

This composition does not approve a production package binding or establish
full Node catalog or cross-platform Host/HTTP qualification.
Unix atomic no-replace and platform-specific filesystem runtime validation
remain separate gates.

## Public qualification cohort

An explicitly opt-in development build connects the existing public commands
to the shared application. Normal builds retain the existing initialization,
catalog, parameter, and force behavior. Enabling this cohort is not production
package approval or a complete Node cutover.

The Preview5 qualification split is temporary, not a permanent dual-engine
architecture. It will be removed when the remaining Templates functionality
has migrated to the package-backed engine. Until then, existing functionality
remains available through the existing route, selected before generation;
package-backed failures never fall back. Guarded `func new --force` replacement
remains a separate obligation for full compatibility.

The development binding embeds `Azure.Functions.Templates.Node.Basic`
`0.1.0-qualification.1`, requires SHA-256
`3af83804e4fa033a96530c952d0110aebe0c3ade0c171c4c2e0d06de79eec6cc`,
and selects the exact project default
`Azure.Functions.Templates.Node.Basic.Project.JavaScript`. It is compiled only
when `EnableTemplatesQualification=true`; missing archive input fails the
build. The embedded resource is opened directly into bounded Package Intake,
not copied beside the executable. Every acquisition validates identity,
version, digest, native metadata, and the bound project default.

```powershell
$archive = '<absolute path to the original verified qualification archive>'
dotnet publish src\Func\Func.csproj -c Release -r win-x64 `
  -p:EnableTemplatesQualification=true `
  "-p:TemplatesQualificationPackage=$archive" `
  --output '<private validation directory>'
```

In that build:

- Fresh Node JavaScript `func init` uses the bound native project default,
  before directory creation, config writes, or force cleanup. Adoption and
  missing-language healing retain the existing path and configuration merge.
  TypeScript and other stacks retain their existing initializer.
- The qualification project path publishes only authored template output.
  Dependency restore is deferred to `func start`; `--skip-npm-install` remains
  accepted. The fixed project template does not declare project-name
  substitution. `--name`, `--force`, and explicit `--bundles-channel`
  values are unsupported on this path. Its authored `host.json` contains
  only `version`; it adds no extension bundle, so `--no-bundles` is accepted.
- Node JavaScript `func new -t HttpTrigger-JavaScript` uses the native item
  `Azure.Functions.Templates.Node.Basic.Http.JavaScript`. Its full identity
  and authored short name `func-http-js` also select it. Native metadata
  supplies the default name `httpTrigger`; `--name` / `-n` overrides it.
- Other Node item templates and TypeScript projects select the existing
  implementation upfront. A failed package-backed request never retries
  through that implementation.
- Listing combines the eligible installed catalog with the bound item,
  replacing only the JavaScript HTTP entry. Hidden ordinary items stay
  hidden from listings but remain explicitly invocable. Internal-role
  items cannot run through the ordinary path. The native HTTP item can be
  listed in the authored bundle-free project without an installed legacy
  catalog; incompatible items are not advertised.
- The package-backed item path rejects `--force` before acquisition or
  publication. Existing force behavior remains available on the explicitly
  selected existing routes. This is not full public force compatibility.
- Existing `[path]`, `--template` / `-t`, `--name` / `-n`, `--list` / `-l`,
  `--non-interactive`, and `--output plain|json` syntax remains. Raw template
  tokens retain duplicate occurrences and explicit values equal to defaults.
  The qualified HTTP metadata declares no authorization-level, methods, or
  route parameters; those inputs are rejected rather than invented or ignored.
- Basic help performs no acquisition. Selected HTTP help validates the bound
  metadata and shows its authored default name. Missing or tampered content
  fails closed rather than reverting to legacy help or generation.

The final release binding remains a separate approval: approved package ID,
exact version, independently approved digest, embedded resource identity, and
deterministic project default. It must not be inferred from this qualification
binding.

The proposal, design and task list in this directory preserve the earlier
engine-integration plan. They are not evidence of completed resolution,
preparation or command integration. See the
[Templates overview](../templating-system/design.md) for contract navigation
and historical scope, and
[PR #5767](https://github.com/Azure/azure-functions-core-tools/pull/5767)
for Foundation implementation and validation evidence.
