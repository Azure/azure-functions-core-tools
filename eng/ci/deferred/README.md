# Deferred live-language testing improvement

The user narrowed this migration's testing scope. Comparable migration
confidence does not introduce a mandatory toolchain for every language.

`LanguageSemanticContractTests.cs.proposed` preserves the exact source previously
compiled at `test/Func.Tests/Templates/LanguageSemanticContractTests.cs` in
`95af65bc` and the staged `259fa003` snapshot. Its extension keeps it outside the
test project. This is an explicit deferral, not a skipped or passing test.

| Method | Variants | Separate improvement prerequisites |
|---|---:|---|
| `PythonParser_IdentifierOracle_ReportsActualNormalizedAstName` | 17 | Python 3.13 |
| `PythonParser_ScalarBoundary_ReportsActualInterpreterAcceptance` | 6 | Python 3.13 |
| `GoParser_ActualNativeProject_AcceptsSourceAndRejectsSyntaxDamage` | 2 | Go 1.24 or newer and gofmt |
| `JavaParser_ActualNativeProject_AcceptsSourceAndRejectsSyntaxDamage` | 2 | JDK 21 or newer |
| `PowerShellParser_ActualNativeProfile_AcceptsSourceAndRejectsSyntaxDamage` | 2 | PowerShell 7.4 or 7.6 |

All 29 cases were added by this CI scope work, not inherited from the frozen
`38199abc` migration candidate. No specific migration regression requiring one
of these new checks was demonstrated. Historical executions, including the two
Java process-launch failures, remain historical evidence. They do not become
passes or a current migration gate through deferral.

The preserved file includes its original `LanguageTestProcess` helper so the
proposal is complete. The later CI simplification removes that helper from the
active test graph together with the custom auditor it served. Nothing here
grants execution or tool acquisition.

Existing Python, Go, Java and PowerShell package, generation, parity and runtime
requirements remain unchanged. Any tools genuinely needed by retained runtime
or E2E cases remain prerequisites of those independent cases. The deferred
parser/compiler checks neither replace nor qualify SDK, worker, Host or backend
execution.

## Separately approved Node VM assertion deferral

The user also approved deferring only the 12 migration-added Node VM assertion
variants. `node-vm/inventory.json` identifies every variant, expected effective
value, registration sequence and route/output property, including the
unexpected-converter-success witness. Three complete pre-deferral test files
are preserved byte-for-byte as noncompiled `.cs.proposed` snapshots there.
Their portable context is historical context, not a deferral of all 23 mixed
test journeys. All 23 remain active.

Blocking CI no longer dynamically evaluates JavaScript decoy validity,
effective values or Dapr registrations/routes. This is an explicit accepted
assurance loss, not a claim that filenames, source strings or converter refusal
are equivalent to executing JavaScript. No missing-runtime skip or automatic
fallback is used. The previous Node-enabled pass remains historical.
