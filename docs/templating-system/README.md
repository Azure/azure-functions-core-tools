# Templating integration branch

| Shared branch | Final target | Created from |
|---|---|---|
| `feature/vnext-templating` | `vnext` | [31e0df32](https://github.com/Azure/azure-functions-core-tools/commit/31e0df3259a2a018b7abc5354ff6ebda5826ae54), 18 September 2026 |

This branch brings the templating workstreams together without requiring every intermediate change to land directly in `vnext`. The initial commit contains planning documents and diagrams only. It does not mark the plan, staffing or unresolved design decisions as approved.

## Start here

- [Delivery overview](implementation-plan.md) explains the parallel workstreams and integration points.
- [Work-package guide](work-packages.md) gives the assignable work, dependencies and acceptance checks.
- [Coordination design](design.md) and [coordination tasks](tasks.md) map the focused specifications. Reconcile their known drift as part of the design work, rather than treating this landing page as a new specification.

## Contribution workflow

1. Create a task branch from the current `feature/vnext-templating` head.
2. Open a draft PR with **base `feature/vnext-templating`**, not `vnext`. Keep the PR scoped to a reviewed deliverable and include its tests and documentation.
3. Review and validate the exact changes before merging into the shared branch. A working fixture is not a substitute for the required real integration checks.
4. Bring changes from `vnext` into this branch regularly using merge commits. Do not rebase or force-push the shared branch after contributors depend on it.
5. When the affected capability is ready, review the complete accumulated diff against current `vnext`. The eventual integration PR must include migration and release evidence, not just individual PR results.

The existing [public build configuration](../../eng/ci/public-build.yml) includes `feature/*` in its PR target filters. That is not a guarantee that branch protections are configured or that every check runs successfully. The pipeline has no push trigger, so the initial branch push alone does not establish a passing build. Check validation on each contributor PR.

## Temporary planning files

Before merging the completed integration into `vnext`:

- [ ] Preserve any planning record the team wants outside the final product tree.
- [ ] Remove this branch landing page, the [delivery overview](implementation-plan.md), the [work-package guide](work-packages.md), and their [architecture](assets/templating-architecture.svg) and [roadmap](assets/templating-roadmap.svg) diagrams.
- [ ] Remove or update any new links to those temporary files and check for orphaned assets.
- [ ] Retain the authoritative designs, implementation tests and permanent user/authoring documentation. Do not delete the whole templating documentation directory.
- [ ] Review and validate the final diff against `vnext` after cleanup.

Removing these files makes them absent from the final tree. It does not, by itself, remove their earlier commits from branch history. Decide the final merge strategy separately if that distinction matters.