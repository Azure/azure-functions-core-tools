## Project compatibility checks for function templates

Before a function template is added to an existing Functions project, its
required project capabilities must be checked. A function template is an item
template used to add a function to an existing project.

The template publisher declares requirements. The stack-specific
[`FunctionsProject`](../../src/Abstractions/Projects/FunctionsProject.cs)
implementation inspects the existing project and reports its capabilities.
The CLI's compatibility matcher compares those reports with the requirements.
It returns `Compatible`, `Incompatible`, or `Unknown`.

Here, **stack implementation** means the CLI's language-aware project code,
such as Python- or .NET-aware code supplied by a workload. It does not mean
the Azure Functions Runtime/host or a language-worker process.

This section owns the detailed project-compatibility contract. The
[specification](specs/template-constraints/spec.md) provides acceptance
scenarios; [the Templates overview](../templating-system/design.md) provides
navigation and historical context. Implementation progress and validation
evidence belong in [PR #5767](https://github.com/Azure/azure-functions-core-tools/pull/5767)
and follow-up issues, not in this contract.

### Ownership and API

The public workload contracts live beside
[`FunctionsProject`](../../src/Abstractions/Projects/FunctionsProject.cs):

- `FunctionsProject.StackName` supplies the canonical stack identifier used to
  bind the reports. See [authority](#authority).
- `GetProjectFactsAsync(ProjectFactsContext, CancellationToken)` is optional,
  read-only and [cancellable](#cancellation-and-failures). Its default returns
  no snapshot, so existing stack implementations remain source compatible.
- `ProjectFactsContext` identifies the expected
  [project root](#project-root), [authority](#authority), and
  [capture ID](#capture-id).
- `ProjectCapability` reports an opaque ID as Supported, Unsupported or Unknown.
- `ProjectFacts` is an immutable **capability-report snapshot** for one resolved
  project and capture. It contains the reports, their context, an
  `IsAuthoritative` assertion, and the stack's
  [inspected-input fingerprint](#inspected-input-fingerprint).

The types are public because separately shipped workloads implement the
resolved-project boundary. They contain no template identity, template selection,
package acquisition, generation policy or engine-native object.

`ProjectFacts` names the project observations supplied to the matcher; it is not
a separate framework pattern or a claim that every report is established truth.
The stack implementation produces it. The matcher consumes it alongside
`ItemTemplateRequirements`. The context identifies the capture, the evidence
fingerprint identifies inspected inputs, and the reports describe capability
support. None of those inputs alone is a template-compatibility result.

For example, after inspecting project inputs, a stack implementation could
return the following snapshot. The capability name is illustrative, not a
registered production capability:

```csharp
return new ProjectFacts(
    context,
    inspectedInputFingerprint,
    isAuthoritative: true,
    [new ProjectCapability("example.http-items", ProjectCapabilitySupport.Supported)]);
```

The shared CLI-owned types carry strings, identifiers and support states.
They do not expose Microsoft.TemplateEngine objects or stack-specific project
models. The matcher therefore needs no Python- or .NET-specific interpretation.

`ProjectCapabilitySupport` describes knowledge of **one capability**.
`ItemTemplateCompatibility` describes the result of combining the template's
requirements with snapshot validity and all relevant reports. The separate
enums represent different decisions; their mapping is defined under
[matching](#matching-and-compatibility-results).

### Capture identity and evidence

The matcher records the expected context from the resolved project before
calling the stack implementation. It compares the returned context with that
expectation and checks the resolved project again after capture. Passing the
context into the stack does not make the returned identity trustworthy.

#### Project root

Root normalization is a deterministic string operation over a fully qualified
native path. It does not call `Path.GetFullPath`, probe the filesystem, resolve
links, expand short/long names, interpret `~` as a home directory, or consult
process current directory. Components need not exist. An 8.3-style component
remains literal even when its corresponding long-name directory exists.

For ordinary Windows drive and UNC paths, alternate separators become `\`,
repeated separators below the native root collapse, `.` is removed and `..`
cannot move above the drive or share root. Trailing separators are removed
except for a root separator. Existing non-verbatim Windows terminal-space/dot
handling is retained lexically. DOS-device paths remain distinct namespaces,
with their device component as the traversal boundary; they are not converted
to ordinary drive or UNC identities.

Previously accepted verbatim `\\?\` and `\??\` paths retain their component
spelling, dot segments and separators, except for the existing removal of at
most one trailing separator beyond the root. Alternate-separator device
prefixes follow the non-verbatim rules. On Unix, `/` is the root boundary and
backslashes remain literal. No path form gains filesystem alias equivalence.

Root comparisons are ordinal and conservative. Aliases or differently cased
paths do not establish identity by themselves. The matcher starts with the
resolved project's `WorkingDirectory.Info.FullName`; the public context also
validates and normalizes independently supplied roots.

#### Authority

Authority is the resolved project's canonical `FunctionsProject.StackName`,
recorded in `ProjectFactsContext.Authority`. There is no separate producer-ID
property. The matcher compares the expected and returned identifiers ordinally
and checks that the resolved stack did not change during capture. Stack identity
binds the snapshot; it does not establish that a capability is supported.
Stack identifiers follow the existing stack-metadata contract: nonempty with
no leading or trailing whitespace. They are not capability IDs and do not use
the capability declaration's ASCII grammar. Their spelling is preserved.

#### Capture ID

The capture ID is a nonempty caller-supplied GUID identifying one operation.
Use a fresh ID for a new evaluation. The stack implementation must echo it
rather than return a snapshot from another operation. Revalidation retains
the operation's ID.

#### Inspected-input fingerprint

A stack implementation opting in must inspect the actual project on every
call and supply a SHA-256 fingerprint of **all** inputs used to establish its
reports, including relevant missing inputs. It decides which project evidence
establishes each capability and whether that evidence is authoritative.
Invalid IDs, enum values, fingerprints or null reports are contract errors.

`IsAuthoritative` asserts that the reports came from current project inspection,
not inference from stack, language or package labels alone. A false value
cannot establish either compatibility or incompatibility.

The fingerprint binds inspected evidence; it does not authenticate the producer
or prove complete inspection. The matcher cannot detect an edit the stack
implementation omits from its evidence. Stack implementations must not label
cached or heuristic data authoritative.

### Publisher declaration

[`ItemTemplateRequirements`](../../src/Func/Templates/Compatibility/ItemTemplateRequirements.cs)
accepts this standalone neutral declaration:

```json
{
  "type": "func-project-capabilities",
  "args": ["example.model-v1", "example.http-items"]
}
```

This is a **nonempty conjunction**, not an alternatives list. Every capability
must be supported. Type/property names and capability IDs are case-sensitive.
An ID starts with an ASCII letter or digit and contains only ASCII letters,
digits, `.`, `-` or `_`; IDs are not trimmed or interpreted. Unfamiliar valid
IDs need no generic code changes.

The parser distinguishes absence (null input, retained as an undeclared
requirements snapshot) from a malformed declared object. Empty arrays,
malformed/duplicate IDs, duplicate properties, missing fields, non-string IDs,
and invalid JSON fail with `FormatException`. Unknown vocabularies or extra
properties fail with `NotSupportedException`. There are no expressions,
scripts, version ranges, predicates, stack callbacks or alternative syntax.
Caller collections are copied, sorted ordinally and exposed read-only.

This foundation does **not** read `template.json`, register an engine constraint,
or change the existing raw engine declaration reader. A later private adapter
must extract the exact declaration and reject duplicate/unsupported raw syntax
before engine normalization can discard it. The neutral object is not an
engine-native product contract.

### Matching and compatibility results

[`ItemTemplateCompatibilityMatcher`](../../src/Func/Templates/Compatibility/ItemTemplateCompatibilityMatcher.cs)
obtains the snapshot through the resolved project's optional operation. It does
not infer support from stack name, display name, language, worker reference,
bundle version or package identity. It checks that the resolved root/authority
did not change while the stack implementation captured the reports.

| Outcome | Rule |
|---|---|
| Compatible | A declaration is present and every required capability is authoritatively Supported for this root, owner and capture. |
| Incompatible | At least one required capability is authoritatively Unsupported. This takes precedence over missing/Unknown evidence for other capabilities. |
| Unknown | The declaration is absent; facts or authority are missing/insufficient; root, owner or capture differs; the facts snapshot is contradictory; or a required capability is missing or Unknown without an authoritative negative establishing Incompatible. |

Contradictory reports for any ID invalidate the facts snapshot as Unknown,
including contradictions outside the required IDs. Identical repeated reports
are consistent. Negative precedence applies only after snapshot-wide authority,
identity, freshness and consistency checks pass; an invalid snapshot cannot
establish either compatibility or incompatibility.

The requirements fingerprint includes the vocabulary, declaration-presence
state, and ordinally sorted IDs. The facts fingerprint includes the normalized
root, authority, capture ID, uppercase inspected-input fingerprint,
`IsAuthoritative`, and every capability report sorted by ordinal ID and support
state. Repeated reports remain part of the snapshot and its fingerprint.
Both use SHA-256 over deterministic UTF-8 JSON.

[`ItemTemplateCompatibilityResult`](../../src/Func/Templates/Compatibility/ItemTemplateCompatibilityResult.cs)
retains the normalized root, authority, capture ID, immutable requirements and
facts, fingerprints, outcome and immutable diagnostics.
`PassesProjectCapabilityCheck` is true exactly when the outcome is Compatible.
It passes only this check: it does not establish preparation readiness or
authorize preparation, generation, execution, or publication.

### Read-only revalidation

`RevalidateAsync` recaptures facts without mutation for the same operation.
Changed root/authority, requirements, capability reports, authority quality,
inspected-input evidence or disappearance of facts invalidates the prior decision
as Unknown, even if the new inputs could independently match. An outcome
change during recapture also fails closed. Make a fresh compatibility evaluation
instead of promoting an old result.

### Cancellation and failures

The optional operation accepts a `CancellationToken`. Implementations must pass
it to child operations and honor cancellation without returning a successful
snapshot. The matcher forwards the token and checks it before and after
capture and while evaluating reports and requirements.

Unexpected stack failures propagate; they are not converted into
success-shaped missing-facts fallbacks. Project-facts acquisition is the only
boundary call: the matcher has no engine, package/store, Quickstart, transaction,
generation, publication or command collaborator.

### Diagnostics

`ItemTemplateCompatibilityDiagnostic` carries a code, message and optional
capability ID. The current consumers are the focused Foundation tests; no public
command exposes these matcher codes. Codes use local lower-case, hyphenated
names. No repository-wide diagnostic-code naming convention is established
here, and this design does not define an external machine-readable contract.

The matcher orders snapshot-validity diagnostics by check, conflicting IDs
ordinally, and required-capability diagnostics by ordinal requirement ID.

### Validation boundary

Focused unit tests cover legacy stack implementations, all support states, invalid/conflicting
evidence, strict/absent declarations, immutable snapshots, deterministic
matching, negative precedence, stale/changed evidence, cancellation and an
unfamiliar stack/capability. Tests with throwing display-name/language/worker
getters check that matching does not consult those labels. Canonical stack
identity is read only to bind the reports, never to infer support.

From an already-restored checkout, the focused validation path is:

```powershell
dotnet build .\test\Func.Tests\Func.Tests.csproj --no-restore --configuration Release -p:CI=true
dotnet test .\test\Func.Tests\Func.Tests.csproj --no-build --no-restore --configuration Release `
  --filter 'FullyQualifiedName~Azure.Functions.Cli.Tests.Projects.ProjectFactsTests|FullyQualifiedName~Azure.Functions.Cli.Tests.Templates.Compatibility|FullyQualifiedName~Azure.Functions.Cli.Tests.Projects.FunctionsProjectHostRunContextTests'
```

These tests establish contract behavior with synthetic stack implementations,
not production-stack, TemplateEngine-integration, cross-platform or end-to-end
qualification. Exact runs and historical normalization evidence are recorded
with their source revisions in
[PR #5767](https://github.com/Azure/azure-functions-core-tools/pull/5767)
and its Git history.

Production evidence collection, raw package declaration translation,
preparation, private engine integration and command wiring are outside this
read-only boundary. No new package dependency or generated content is needed.

## Historical engine constraint context

The remainder preserves the earlier workload/bundle constraint design.
Its eligibility states and valid empty engine-constraint objects are not
semantic project/item compatibility. Workload/version checks must not substitute
for the project-capability contract above. Its shared install/hive and Quickstart
assumptions are historical broader scope, not current P0 prerequisites.

See `proposal.md` for motivation and `specs/template-constraints/spec.md` for the behavior contract.

Microsoft.TemplateEngine reads a `constraints` section from `template.json` and evaluates each entry through the constraint factory registered for its type. An evaluation returns allowed, restricted with a message and an optional call to action, or not evaluated. The engine never blocks creation itself. Each host decides, and `dotnet new` refuses every result other than allowed unless `--force` is used.

The func host currently registers the engine's built-in `os`, `host`, `sdk-version`, and `workload` constraints. The last two describe the .NET SDK and depend on SDK and workload providers that func does not supply. The current Functions templates declare no constraints.

`template-engine-integration` defines the eligibility states and evaluates constraints before a template can be selected. `func-init-execution`, `func-init-quickstarts`, and `azure-samples-template-pipeline` require workload availability before scaffolding, and `func-new-execution` expects extension bundle checks to run as template constraints. This change supplies the func constraint types and the results those changes consume.

## Goals / Non-Goals

**Goals:**

- Let templates declare required func workloads and extension bundle versions in the standard TemplateEngine format.
- Keep evaluation offline, deterministic, and cheap enough to run for every listed template.
- Give every restriction a reason and, where one exists, a next step generated by the CLI.
- Tell apart restrictions users can fix, constraint types the CLI can't check, and constraints that are broken.
- Define the workload constraint form for Azure-Samples packages.

**Non-Goals:**

- Install, update, or acquire workloads or template packages.
- Decide how the `func init` picker presents templates that aren't available.
- Select templates or template versions by target framework.
- Add stack or language constraint types while `tags.language` already separates stacks.
- Support third-party constraint types.

## Decisions

### Constraints use the TemplateEngine declaration

Templates declare func requirements in the standard `constraints` section:

```json
"constraints": {
  "node": { "type": "func-workload", "args": "node" },
  "bundle": { "type": "func-bundle", "args": "[4.20,)" }
}
```

Every entry must be satisfied. When `args` is an array, its items are alternatives and one satisfying item is enough, as with the engine's built-in constraints. Func types use the `func-` prefix so they can't collide with engine or .NET SDK types, and they require `args`.

The engine matches type names case-sensitively and discards entry labels. It also skips an entry that isn't an object or has no `type`, and a repeated label replaces the earlier entry. Both silently drop a requirement, so authoring validation rejects them.

The same context-independent raw declaration validator runs in packaging, isolated install/update preflight, and installed-template listing and resolution. It reads the mounted `.template.config/template.json` before duplicate properties or malformed entries can be lost through normalization. It rejects a repeated top-level `constraints` member and duplicate property names within any object in the constraints subtree, including labels, `type`, `args`, and object-form argument properties such as `id` and `version`. Duplicate checks are local to each object, so separate alternative objects may use the same property names. An omitted or empty constraints object is valid. A present non-object section, a non-object entry, a missing, non-string, or empty type, or invalid func arguments fails validation. A well-formed unknown type remains a declaration and is evaluated as `NotEvaluated`, not silently dropped or rejected merely for being unknown.

Preflight rejects a package containing malformed declarations before live hive mutation. Listing and resolution validate current mounted content even for packages already installed or loaded from the engine cache, and report malformed declarations as `Failed`. Constraint evaluation uses the same validated content snapshot, not stale cached declarations. Before invocation, the session verifies that the selected template's configuration still matches that snapshot. Changed or unreadable configuration blocks invocation with guidance to rerun; it never triggers reference resolution or substitutes another template. These checks validate declarations, not workload availability on the packaging or installation machine.

**Alternative considered:** declare requirements in `func.host.json`, which other hosts ignore. Func would need a second evaluator beside the engine's constraint manager, and listing and resolution would read eligibility from two places. It is rejected while no shared template needs a func requirement.

### Func constraint types stay in func template packages

A host that doesn't know a constraint type reports it as not evaluated, so `dotnet new` refuses to create the template without `--force`. A template package that other hosts also install, such as one that also declares the `Template` package type, must not use func constraint types.

Project templates for `func init` carry func configuration actions, so they already live in func template packages. Item templates shared with `dotnet new` or Visual Studio rely on the command's own checks, such as stack resolution, rather than func constraints.

First-party templates that need func constraints ship in func-only packages. Packaging and isolated install/update preflight reject func constraint types when NuGet metadata also declares the standard `Template` package type or another host-sharing declaration supported by the CLI. Installed-template validation applies the same check to available package metadata, covering packages registered before the rule was introduced. A shared package without func constraint types remains supported. Folder installs have no NuGet package-type metadata and continue to use raw declaration validation without inventing a shared-package classification.

### `func-workload` requires loaded workloads

`func-workload` names a workload by alias or package ID and can add a version range:

```json
{ "type": "func-workload", "args": "python" }
{ "type": "func-workload", "args": { "id": "bundles", "version": "[4.20,)" } }
{ "type": "func-workload", "args": [ "node", { "id": "python", "version": "1.2" } ] }
```

The constraint checks the workloads the CLI loaded when the command started: the highest installed version of each runtime workload (kind `workload`) that loaded, and every installed version of each content workload (kind `content`), such as extension bundles, except packages built for another platform. A loaded workload matches when the name equals one of its aliases, its package ID, or the published package ID of a platform-specific workload, ignoring case. Checking loaded versions keeps eligibility aligned with the versions commands actually use.

The snapshot contains only workloads that passed the CLI's applicable compatibility and activation checks. Installed entries that were rejected remain available for diagnostics but cannot satisfy a constraint. The workload subsystem owns managed-contract, packaging, and activation checks. The template subsystem owns package classification and supported template formats and features. Template constraints are additional eligibility checks, not replacements for either boundary.

A name must start with an ASCII letter or digit, contain only ASCII letters, digits, `.`, `-`, and `_`, and not end in `.nupkg`. Any other name makes the constraint fail, so template text can't change a generated command. Evaluation never contacts a feed, so workload constraints behave the same offline.

### `func-bundle` requires the project's extension bundle

`func-bundle` reads `func:bundle-id` and `func:bundle-version` from the command context:

```json
{ "type": "func-bundle", "args": "[4.20,)" }
{ "type": "func-bundle", "args": { "id": "Microsoft.Azure.Functions.ExtensionBundle.Preview", "version": "[4.29,)" } }
```

A string argument is a version range for any bundle identity, and identities compare case-insensitively. When the command resolved no bundle, as in `func init` or a project without extension bundles, the constraint is restricted and can't be satisfied. For projects that use extension bundles, `func new` stops before template listing if a declared bundle cannot be resolved. Projects without extension bundles, including .NET projects, remain supported with absent bundle context. Project templates express bundle needs through `func-workload`, and packaging rejects `func-bundle` in project templates.

### Versions use NuGet ranges

Both func types use NuGet version range syntax, the same syntax as the `extensionBundle` range in `host.json`, including its floating form such as `[4.*, 5.0.0)`:

| Declared | Meaning |
|---|---|
| `1.2` | 1.2.0 or later |
| `[1.2]` | exactly 1.2.0 |
| `[1.2,2.0)` | 1.2.0 or later, below 2.0.0 |
| omitted | any version |

A bare version is a minimum. A version also satisfies a range when it's a prerelease whose release version is in the range, as the workload catalog already treats prerelease workloads, so `1.0` accepts `1.0.0-preview.2`. An invalid range makes the constraint fail rather than match nothing.

This prerelease rule applies only to `func-workload` and `func-bundle` eligibility. It does not select or install prerelease packages and must not be reused for CLI minimums or managed-contract compatibility. Those checks retain their own version ordering, so a CLI minimum of `5.2.0` does not accept `5.2.0-preview.1`.

The engine's `host` and `sdk-version` constraints compare differently. A bare version is an exact match, and a minimum such as `[5.0,)` excludes 5.0 prereleases. Func types follow NuGet so workload and bundle ranges read the same as `host.json`.

### Built-in constraints keep their engine behavior

`os` and `host` evaluate unchanged. `host` sees the CLI as `func` with the CLI version, so a template can require a minimum CLI. A lower bound such as `5.1.0-0` also admits preview builds:

```json
{ "type": "host", "args": [ { "hostname": "func", "version": "[5.1.0-0,)" } ] }
```

The session leaves out the .NET SDK `sdk-version` and `workload` factories. Templates that use them are treated like any other unknown type: not evaluated and blocked.

### Results map to the integration's eligibility states

Engine results become the four states `template-engine-integration` defines:

```text
template.json constraints
        |
        v
engine constraint manager (one per command session)
        |  allowed | restricted | not evaluated
        v
ConstraintEvaluation
|- Eligible        every constraint allowed
|- Restricted      a reason and optional next step for each unmet constraint
|- NotEvaluated    a constraint type this CLI doesn't register
`- Failed          invalid arguments or a constraint that couldn't run
```

The engine reports unknown types, factory failures, and evaluation exceptions all as not evaluated. Func separates them by type name, matched case-sensitively like the engine: a type with no registered factory is `NotEvaluated`, and any other not-evaluated result is `Failed`.

A template with several unmet constraints takes the most severe state, in the order `Failed`, `NotEvaluated`, `Restricted`, and keeps a diagnostic for each unmet constraint with its type, message, and optional next step. Every state except `Eligible` blocks selection and invocation, including when `--force` is used.

### Restrictions carry a reason and a CLI-generated next step

A func constraint builds its message and next step from the parsed requirement, and returns the next step as the engine's call to action. The message states the reason. Next steps are commands or fixed guidance, and the only template text they contain is a validated workload name.

| Result | When | Next step |
|---|---|---|
| Workload missing | No installed workload matches | `func setup --features <feature>` when a setup feature installs the workload, such as a stack's feature, `host`, or `runtime` for extension bundles, otherwise `func workload install <workload>` |
| Workload not usable | A matching workload is installed but was rejected by compatibility checks or didn't load | None; retain the CLI's compatibility or load diagnostic rather than treating the workload as missing |
| Workload outdated | Every matching loaded version is below the range | `func workload update <workload>` when the range has no upper bound, adding `--major` when the minimum is in a higher major version, otherwise none |
| Workload incompatible | Every matching loaded version is outside the range and at least one is above it | None |
| Bundle outdated | The resolved bundle version is below the range | Allow the required version in the `host.json` bundle range, or install a newer extension bundle |
| Bundle mismatch | The resolved bundle has another identity or is above the range | None |
| Bundle unavailable | The command resolved no bundle | None |
| `host` restricted | The CLI isn't a supported host or version | None, because the engine's message lists the supported hosts and versions |
| `NotEvaluated` | The type isn't registered | Update the Azure Functions CLI when the type starts with `func-`, otherwise none |
| `Failed` | The constraint is invalid or couldn't run | Check the template's constraint configuration |

Extension bundles are an exception to the update command. One package carries every bundle channel, and `func workload update` replaces the highest installed version regardless of channel, so an outdated `bundles` workload gets fixed guidance to install a newer extension bundle instead. For `NotEvaluated` and `Failed`, this guidance replaces any call to action from the engine.

Install and update guidance identifies an explicit acquisition action, not a guarantee that the selected package is compatible. The CLI names a specific compatible replacement only when it is known. Constraints neither search older package versions nor broaden a user's version or profile pin.

When a constraint lists alternatives and none is satisfied, the message gives each alternative's reason, and a next step appears only when the arguments name a single workload. Built-in and unknown types keep the engine's message. Commands render every constraint message as plain text with control characters removed.

### Summaries describe unavailable templates briefly

`func-init-quickstarts` consumes a projection of the same result. `Eligible` stays eligible, and every other state becomes restricted with a short summary, the diagnostic messages, and their next steps. The summary comes from the state and the first unmet constraint:

| State and constraint | Summary |
|---|---|
| `Restricted` by `func-workload` | requires workloads |
| `Restricted` by `func-bundle` | requires an extension bundle |
| `Restricted` by `os` | not supported on this OS |
| `Restricted` by `host` | not supported by this CLI |
| `NotEvaluated` | uses an unsupported constraint |
| `Failed` | has invalid constraints |

### Workload requirements give the same result before a project exists

`func-workload`, `os`, and `host` read no project context, so they give the same result before and after a template is chosen. `func init` lists templates from the session it later invokes them from, so its picker can show unavailable templates, as `func-init-quickstarts` requires, and that result is final. `func-bundle` depends on the resolved command context, which `func new` resolves before it lists templates.

### Azure-Samples packages declare their stack workloads

An authored quickstart declares one `func-workload` entry for every stack its configuration actions declare. A synthesized quickstart receives one entry per distinct declared stack, named by the canonical stack, with no version range:

```json
"constraints": {
  "node": { "type": "func-workload", "args": "node" },
  "python": { "type": "func-workload", "args": "python" }
}
```

Packaging checks the form. Every entry is an object with a `type` and a unique label, func arguments are valid, an entry requires every stack a configuration action declares, and project templates declare no `func-bundle`. Packaging doesn't evaluate the constraints against the build machine's workloads.

### Target-framework selection is not a constraint

Constraints decide whether a template can run in the current context. Choosing which template version or content fits a .NET project's target framework is selection, not eligibility, and a constraint could only hide the other versions. Templates read the project's framework or language version through `template-engine-bind-sources`, and choosing a templates package version belongs with template package acquisition, which has no owning change yet.

**Alternative considered:** a target-framework constraint on each template version. It would restrict every version except one without telling the user which package to install. It is rejected.

### Func constraint factories come from command services

The `func-workload` factory needs the loaded workload snapshot and the installed workload list, both captured when the command starts. The per-command engine session registers the func factories as built-in host components, and the templater factory receives them through dependency injection. Evaluation adds no network or file access beyond what the session already performs.

## Risks / Trade-offs

- **[Shared templates can't declare func requirements]** -> State the rule in the authoring guidance, and revisit host-specific requirements if a shared template needs one.
- **[Shared .NET templates could adopt `sdk-version`]** -> Func would then need an SDK provider so it evaluates those templates instead of blocking them.
- **[The engine drops malformed or repeated entries while loading]** -> Shared raw validation rejects them in packaging and package preflight and blocks malformed installed templates, even when engine cache data omits the declaration.
- **[Engine results carry text rather than structure]** -> Diagnostics keep the constraint type beside the text, func constraints generate both their message and next step, and summaries come from the state and type.
- **[Func types and `host` compare versions differently]** -> Document both readings and test minimum and prerelease cases.
- **[`func-workload` can't select an extension bundle channel]** -> Add a channel argument if a project template needs one.
- **[Generated commands can drift from the workload commands]** -> Build next steps in one place and test them against the workload command options.
- **[Evaluating every listed template adds time]** -> Evaluate against in-memory snapshots with one constraint manager per command.

## Migration Plan

1. Register the func constraint factories with the engine session and map results to eligibility states.
2. Evaluate constraints during listing and resolution as `template-engine-integration` specifies.
3. Declare workload constraints in func project templates and Azure-Samples packages.
4. When `func new` moves to the new runtime, replace the templates workload's single minimum bundle version check with `func-bundle` constraints on the templates that need them.

Rollback removes the func factories. Templates that declare func types then evaluate as not evaluated and stay blocked rather than running without their checks.
