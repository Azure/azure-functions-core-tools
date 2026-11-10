# Workload Template Supply and Registration

**Status:** Proposed workload package contract. The accepted [component model](Templates-EndToEnd-Object-Model-Review.md) remains authoritative. PR #5873 is documentation-only; concrete integration is downstream work in PR #5878.

## Ownership

Each workload supplies the template content required for its supported CLI experience and owns its declaration, setup-time registration, and readiness. The workload and Stack interpret project and compatibility information; commands apply that information and orchestrate the experience.

PR #5872 owns the Template Engine discovery and selection foundation. `TemplatePackageManager` owns discovery, caching, and the installed inventory. The command-scoped `Templater` owns one session and its one inventory; `TemplateSelection` owns matching and structured results, and `TemplateEligibilityEvaluator` owns eligibility and rejection diagnostics. This contract does not add another scanner, cache, inventory, selection mechanism, or reconstructed engine candidate. See the [architecture](architecture.md) and [implementation](implementation.md).

## Workload declaration

For each required template package, the workload declares:

- The package ID.
- The package version.
- The workload-relative source containing the template content.

Resolve each relative source from the loaded workload's `RuntimeWorkloadInfo.ContentRoot`, not from a global template root or the command's working directory. A source may be a supported package or directory; supported content need not be repackaged solely for registration. This contract does not prescribe a universal directory layout or a workload-manifest schema. A workload may declare multiple packages, and a package may contain multiple templates or programming models.

The workload integration must also identify the templates required for its supported experiences so readiness can verify them. Package IDs and versions are distinct from template identities.

## Registration and readiness

Workload setup makes the declared content available and registers it in the existing CLI Template Engine hive. Registration is setup work, not a side effect of `func init` or `func new`. The commands consume registered content; they do not acquire or install the workload's required templates.

Registration alone does not make a workload template-ready. Readiness must verify through the existing Template Engine that each required template can be resolved by its exact identity, passes engine eligibility, has an available source, and can perform the engine operations needed for the supported experience. If the experience claims template creation, verification must establish that the required templates can be used to create content. A successful registration, a source path, or a listing alone is insufficient.

Report registration and readiness failures with actionable diagnostics. If workload deployment succeeded but registration or readiness fails, report the workload as **installed but not template-ready**. Do not claim that deployed files or successful registration effects were rolled back.

## Command and compatibility boundaries

Project templates serve `func init`; item templates serve `func new`. Quickstarts remain project templates.

Workloads and Stacks interpret project, Stack, language, and programming-model compatibility. Commands apply that context to the structured Template Engine results; PR #5872 does not implement these compatibility checks or grouped/contextual command UX. Missing required compatibility information must fail safely.

Shared aliases use Template Engine matching and ambiguity results, not a second selection path. A command may apply supported compatibility context to the returned candidates and explicitly resolve an engine-issued entry in the same live `Templater` session. Multiple viable candidates remain ambiguous until explicitly resolved. An unavailable or incompatible exact identity must not silently switch to a sibling template.

## Selected-source lifetime

The downstream integration must keep a selected template's registered source available and unchanged from the command's inventory loading through eligibility, selection, and all later use, including generation when supported. Servicing must not remove or alter content while a command still needs it, unless the exact selected content is otherwise preserved for that command.

A versioned directory name alone does not guarantee immutability. If the source or its required lifetime cannot be maintained, fail with a diagnostic rather than silently switching content or reconstructing the candidate.

## Representative content

The Node representative requirement includes a JavaScript project template and a JavaScript HTTP item template for the Node v4 programming model. Here v4 refers to the Functions programming model, not the Node.js or Functions Host version. Final Node package identities and versions remain to be determined.

The following .NET values are examples of the current downstream candidate, not production-owner approvals:

| Template package | Version | Example template identity |
| --- | --- | --- |
| `Microsoft.Azure.Functions.Worker.ProjectTemplates` | `4.0.5708` | `Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x` |
| `Microsoft.Azure.Functions.Worker.ItemTemplates` | `4.0.5708` | `Azure.Function.CSharp.Isolated.HttpTrigger.3.x` |

## Deferred lifecycle policy

Package update, replacement, repair, removal, servicing, rollback, and future acquisition experiences require separate decisions. This contract does not promise rollback or establish a general package-management policy.
