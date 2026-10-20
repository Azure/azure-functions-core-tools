# Migration regression checks in normal CI

The public and official pipelines use their established Linux build job.
Its order is SDK/feed setup, restore, build, Node setup, the whole solution's
tests, test-result publication, then normal signing/package/publish steps.
No separate Windows semantic job, test filter, custom tool manifest, runtime
version allowlist or source/case-hash auditor is required.

## Retained tests and meaningful assertions

The Node converter, source qualifier and Dapr tests were added during the
native-template migration, before the later CI lane experiment. They are not
pre-migration language-runtime requirements. They remain part of normal CI
because they catch concrete conversion errors.

| Test family | Preserved regression assertions |
|---|---|
| Converter, 14 original variants | Reviewed declaration/property/default association and refusal of changed sources, including JavaScript and TypeScript controls |
| Three converter decoys | Actual JavaScript VM evaluation proves the operative connection or schedule is changed despite block-comment, template-literal or unrelated-object decoys. Unexpected converter success still evaluates emitted JavaScript before the failure assertion |
| Three source-qualifier decoys | The same independent VM oracle and fail-closed source qualification prevent unrelated text from authorizing an operative property |
| Four Dapr cross-role cases | Actual native generation followed by exact registration sequences and case-insensitive uniqueness catches primary/helper name-domain collisions |
| Two Dapr associated-helper cases | Actual preview/create parity, registration/route/output properties, rendered CLI/curl text, sentinel preservation and exact file topology catch non-associated helper names or routes |
| Existing C# and F# entry cases | Actual init/new, configuration, source, MSBuild Compile membership/order, build and Functions metadata remain asserted |

The four split Node companion methods introduced by the CI experiment are
merged back into their original tests. No semantic oracle is replaced with
source text or a test count. The original Node producers and all VM assertions
remain together on Linux. The two extra exact-file topology assertions added
during the split are retained as well.

The VM substitutes Functions SDK imports. It is not a real Node worker, Host,
Dapr sidecar, TypeScript compiler or backend check. The existing .NET tests
exercise actual compiler/SDK metadata but are not Host/backend E2E.

Every other inherited solution test remains selected. Linux statx, hardlink,
symlink, SelectedFile, no-peer FIFO and native generation/refusal/preservation
tests are unchanged. Their real Linux outcomes must be inspected during final
qualification. Existing platform-specific attributes remain unchanged. No
missing-runtime catch, availability skip, new platform exclusion or optional
replacement is introduced.

## Setup requirements versus the removed harness

`NodeTool@0` with `22.x` was already the repository's npm packaging baseline.
Commit `38710c84` moved that same step before tests when the migration's real
JavaScript oracles made Node an actual test dependency. It remains the normal
setup mechanism, not a new acquisition route or proof of image/cache access.

The tested VM helpers use `fs`, `vm`, JSON, nullish coalescing and
`Object.fromEntries`. They have no Linux-only or Node-22-only assertion.
Node 22 is retained as the existing CI baseline, not a measured minimum
supported by these tests. No older runtime is substituted without evidence.

The converter tests invoke `pwsh`, and the repository already uses PowerShell
for its template build scripts and CI tasks. That executable is a real
dependency. The removed harness's restriction to PowerShell 7.4 or 7.6 was
introduced for the deferred profile-parser checks, not justified as the
converter's measured compatibility range. There is no new PowerShell installer,
version check or per-executable hash manifest.

The managed .NET SDK setup, `global.json` roll-forward, NuGet authentication,
CFS configuration, isolation and pool/image selections are unchanged.
The new Windows job had no intrinsic platform requirement and no proven Node
route. Returning all assertions to the normal Linux job removes that job and
its dependency without losing an assertion or introducing an equivalent.

The twelve custom result-auditor tests verified only the newly added
source/case-hash auditor and lane wiring. They did not exist in `38199abc` and
did not exercise product or conversion behavior. They are removed with the
machinery they served, not counted as deferred or passing product coverage.
Their earlier source, reviews and results remain historical evidence.

The newly added 29 live Python/Go/Java/PowerShell checks remain explicitly
deferred with their exact source in `deferred/`. No migration regression
requiring that blanket floor was demonstrated. This does not remove a tool
needed by an independent retained runtime case.

## Normal result and audit enforcement

The unfiltered `DotNetCoreCLI@2` task propagates test failure.
VSTest's built-in `RunConfiguration.TreatNoTestsAsError=true` makes no discovered
tests fail rather than pass. Standard `PublishTestResults@2` publishes the TRX
files and fails on failed tests or missing result files. These steps precede
package outputs. No second case inventory, expected-count file, payload
manifest, custom parser or hash gate is used.

These standard tasks do not prove every intended method executed merely from
a green summary. Final-source qualification still examines actual test runs,
Linux-only safety outcomes and required artifacts. Do not turn a skipped
Linux-required case into success or infer cross-platform execution from
cross-RID publishing.

The .NET entry tests retain explicit parent restore/build
`NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=moderate` and
`TreatWarningsAsErrors=true`, including actual WorkerExtensions graph
assertions and captured successful/failed streams. No audit disable, dataset
receipt, custom freshness deadline or audited-project-counter gate is added.
NuGet's documented package-source fallback suppresses NU1905 for missing
vulnerability resources. Zero warnings are therefore not a claim of data
availability, database completeness or absence of vulnerabilities.

## Remaining qualification

The completed Node research established a cache hit on a different worker
image, not an approved route on this pipeline's Linux image. The earlier
Core Tools installer failure remains historical evidence. Simplification
does not make the unchanged NodeTool step's acquisition work or permit new
egress, a mirror, image/pool change or a private task-host runtime.

The remaining execution prerequisite is a functioning approved Node route
under the existing policy. No custom manifest is demanded. If the existing
setup cannot supply Node, return that precise installer/route blocker or a
specific proposed equivalent to the user. Do not silently replace the VM.

This source simplification has not been built, tested, committed or queued.
The smallest proposed validation is a strict Func.Tests Release build followed
by the four original mixed Node methods and two .NET entry variants after
their prerequisites and execution are separately admitted. Current-source
Linux full-suite tests and pipeline package/artifact publication remain
required. Runtime/E2E, interactive and .NET compatibility work stays separate.

The built-in no-tests setting and inline argument syntax are documented in
[VSTest run settings](https://learn.microsoft.com/visualstudio/test/configure-unit-tests-by-using-a-dot-runsettings-file).
