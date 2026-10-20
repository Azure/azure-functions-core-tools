# Migration checks without a new test-time Node prerequisite

The user explicitly approved the narrow Node VM assertion deferral and
npm-only Node setup. The public and official pipelines keep their established
Linux build and unfiltered solution tests. NodeTool 22.x is restored to the
existing `artifacts` contains `npm` branch after tests. Default `pack` and
`publish` artifacts do not request Node for the deferred VM assertions.

## Actual historical boundary

`23e8c50d` is the exact parent of the native TemplateEngine foundation
`1f847977` (#5531). Its shared CI had no NodeTool/npm setup. Legacy template
workloads and func-new tests already existed and remain required.
`13e8fcbe`, the immediate parent of the native Node package/catalog slice
`b86680ae`, had NodeTool only in the npm-artifact conditional after tests.
The affected converter, qualifier and Dapr test files did not exist at that
Node-package boundary. `38199abc` was already a migrated hardening candidate,
not the pre-template-package baseline.

Commit `626f9433` introduced npm packaging's NodeTool 22.x step. Commit
`38710c84` later moved it before all tests for the migration-added JavaScript
VM oracles. The approved change removes that new default test-time dependency
without removing Node setup needed by actual npm artifact jobs.

## Retained and deferred assertions

All 23 portable mixed journeys remain active:

| Family | Active variants | Retained behavior |
|---|---:|---|
| Converter | 14 | Actual PowerShell conversion, reviewed input transformations, success/failure exits and association diagnostics, canonical JS/TS source checks |
| Source qualifier decoys | 3 | Authored/native decoy inputs and fail-closed qualification |
| Dapr cross-role native generation | 4 | Installed template pair creation, success statuses and exact file topology |
| Dapr associated native generation | 2 | Dry-run no-write, preview/create parity, creation status, rendered source/CLI/curl checks, sentinel preservation and exact topology |

Only independent VM execution is deferred. The three converter and three
qualifier cases no longer interpreter-verify that their decoys are valid
JavaScript with the expected operative connection or schedule. The six Dapr
cases no longer execute generated JavaScript to assert registration names,
case-insensitive uniqueness, routes or output option values.

For the converter's unexpected-success branch, the active test still fails if
the converter exits zero when failure is expected. What is deferred is the
additional execution of that emitted JavaScript and its changed effective
value assertion. This distinction is recorded explicitly.

`deferred/node-vm/inventory.json` preserves all 12 variants, expectations and
both process-helper identities. The three complete pre-deferral test files are
preserved there as noncompiled `.cs.proposed` snapshots. They include portable
context but do not move the portable journeys out of the active graph.
No fixture, variant or historical failure is reported as newly passed or
skipped. Reactivation is a separately scoped testing improvement.

This is an accepted loss of automatic JavaScript validity/effective-value and
registration/route assurance. Source text, file topology and converter refusal
are narrower claims, not equivalent replacements. The VM itself used mocked
Functions SDK imports, not a real worker, Host, Dapr sidecar or backend.

Other Dapr controls remain unchanged, including reserved names, derived-name
lengths, old-encoding refusal, source provenance/association and no-write
workflow checks. All other package, generation, parity, force/atomic
preservation, configuration ownership and refusal tests remain selected.
Linux statx/identity/hardlink/symlink/SelectedFile/no-peer FIFO tests and
inherited platform attributes are unchanged. Genuine runtime/E2E tool needs,
interactive behavior and .NET compatibility remain separate requirements.

## Existing setup and failure handling

Managed SDK/global.json behavior, NuGet authentication, CFS, pool/image and
isolation settings are unchanged. PowerShell remains an actual converter and
template-build dependency. There is no new runtime family allowlist, Windows
job, executable manifest, custom case/hash auditor or alternate parser.

The existing C# and F# compiler cases retain explicit parent restore/build
`NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=moderate` and
`TreatWarningsAsErrors=true`. Actual parent/WorkerExtensions graph assertions,
successful and failed streams, configuration, Compile-order and metadata
checks remain. No dataset/freshness/counter gate is introduced.

Unfiltered `DotNetCoreCLI@2` tests propagate failure. Built-in
`RunConfiguration.TreatNoTestsAsError=true` rejects no discovered tests.
Standard `PublishTestResults@2` fails on failed tests or no TRX files.
Package steps follow successful test/result steps. These controls do not
prove that every intended row ran or forbid every skip. They also do not make
every result-service upload error fatal. Final qualification must examine
actual inherited/safety outcomes and required published artifacts.

NodeTool 22.x remains the unchanged npm packaging baseline, not a measured
minimum or a new migration-wide runtime policy. Its acquisition uncertainty
is no longer a blocker for default public tests solely because of the deferred
VM assertions. An actual npm artifact job or retained Node runtime job still
needs its approved Node execution prerequisites. No support contact,
provisioning, mirror, egress, image/pool change or private task-host runtime is
authorized by this scope decision.

## Qualification boundary

The Node-enabled `84526cce`/`cf9edc61` integration and earlier local 25-case
results remain historical. They are not relabeled as execution of this
successor. The prior 29 live Python/Go/Java/PowerShell checks also remain
explicitly deferred with byte-preserved source.

This change has source/static verification only. The smallest proposed
validation is a strict Func.Tests Release build and the four retained portable
mixed methods, exactly 23 variants, after Main separately admits execution.
No old .NET or full-suite replay is part of that proposal. Future integration
and final Linux tests/package publication must bind the actual successor
commit/tree, not the pre-deferral source.
