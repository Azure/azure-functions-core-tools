# Handoff prompt — adopt-ms-template-engine (updated 2026-07-30, post-spike)

Copy-paste the section below to the next agent.

---

You are picking up an in-flight OpenSpec change in the Azure Functions Core
Tools v5 repo. Work happens in
`C:\root\worktreeRepos\core-tools\TemplatesV3` (branch `nasoni/templatesV3`).
The design is complete and the de-risking spike has passed (GO); the next
work is turning specs into an implementation checklist and starting to build.

## Read these first, in order

1. `openspec/changes/adopt-ms-template-engine/context.md` — background,
   history, ground-truth facts. **Start with the ⚠️ state banner and §10
   (pivot architecture summary)**; §11 has search-machinery facts; sections
   marked HISTORICAL describe the pre-pivot workload model still present in
   branch code (what the deletions remove).
2. `openspec/changes/adopt-ms-template-engine/design.md` — decisions
   (**D1–D32** in the §7 decision log — authoritative), architecture
   (§2 + §2A), **§6B = spike results**, §5 open questions (all resolved
   except OQ-17 PowerShell, parked), §6 jviau reconciliation table.
3. `openspec/changes/adopt-ms-template-engine/proposal.md` and the five spec
   deltas under `specs/`: template-engine-host, template-packages,
   template-scaffolding, template-search, project-templates.
4. `openspec/changes/adopt-ms-template-engine/tasks.md` — Task 0 (spike, ✅
   DONE) + remaining spikes.
5. Spike code + findings: scratchpad
   `…/5b9cb287-…/scratchpad/spike/` (`Program.cs`, `templates-src/`,
   `RESULTS.md`). Re-run with `dotnet run` (core) or `dotnet run -- upstream`
   (real NuGet package). This is throwaway scratch, NOT part of `src/`.

Confirm state: `openspec status --change adopt-ms-template-engine` (4/4
artifacts complete) and `openspec validate adopt-ms-template-engine`.

## What this change is (current, post-pivot)

Replace the v5 CLI's two bespoke templating engines (`src/Templates.V2`
jobs/actions DSL; `src/Templates.DotNet` dotnet-new shell-out + hive) with
the Microsoft templating engine (`Microsoft.TemplateEngine.Edge` +
`Orchestrator.RunnableProjects`) hosted CLI-internally. **Templates are NOT
workloads** (pivot D21): they are standard engine packages identified by
NuGet package types `FuncItemTemplates` / `FuncAppTemplates`, acquired via
the engine's `TemplatePackageManager` into an isolated func hive (engine
cache = installed truth). Gating is per-template `template.json`
`constraints` (custom `func-extension-bundle` + host/os) — no channels, no
sidecar manifest (D16). Template **search** = a func-owned discovery service
(based on `Microsoft.TemplateSearch.TemplateDiscovery`) publishing an index;
CLI consumes via `func new --search` (D22/D29). **Project templates**
(per-stack `Empty`) power `func init`, which gains a project-template step
and first-run auto-install (D23/D27); `IProjectInitializer` becomes a thin
stack-metadata contract (D31). Command surface folds into `func new`
(`--search/--install/--uninstall/--update`) and `func init` — no
`func templates` tree (D26). `func new` stays the item-template path (D25).

## Spike outcome (design.md §6B, D32) — what's proven

GO. In-proc hosting + engine-managed acquisition into a **relocated func
hive** works; **isolation from `dotnet new` requires overriding the engine's
global settings dir** (`DefaultPathInfo.globalSettingsDir`), not just the
host id. All Python append flows work via **host-side** post-action dispatch
(there is NO engine `IPostActionProcessor` — the host runs code keyed by
`IPostAction.ActionId`; §2.6). Real upstream `Worker.ItemTemplates` loads 36
templates verbatim (D28 path). Engine works under single-file publish;
engine libs = 0.84 MB.

## Working norms (keep these)

- Decisions go ONLY to design.md §7 (numbered, dated, rationale + supersedes).
  Facts/history → context.md; checkpoints → context.md §12.
- Ask the user before ruling on genuinely open questions — every D-number so
  far was decided by explicit user question.
- Don't re-litigate settled decisions without new info — especially the
  acquisition history (D5 → D17 → D21) and the jviau fork rulings.
- After editing artifacts, run `openspec validate adopt-ms-template-engine`.

## External resources referenced by the docs

- MS templating engine source: `C:\root\worktreeRepos\templatingEngine\code`
  (+ `\documentation`); discovery tool `code\Tools\
  Microsoft.TemplateSearch.TemplateDiscovery`; search consumer
  `code\Microsoft.TemplateSearch.Common`.
- Functions templates corpus (we own it): `C:\root\repos\templates\
  Functions.Templates\Templates` (also publishes `Worker.ProjectTemplates`).
- Legacy v5 specs to revise later (design §4): `C:\root\worktreeRepos\
  core-tools\docs\proposed\{workload-spec,templates-workload-spec,
  func-new.spec}.md`.
- jviau's parallel change to reconcile/merge with:
  `docs/specs/func-universal-template-engine` on branch
  `u/jviau/vnext/templates-spec` of Azure/azure-functions-core-tools
  (design §6 table is the agenda; the two now agree on engine, format,
  gating, AND acquisition).

## Next steps (in order)

1. **Expand `tasks.md` §3 into the implementation checklist** from the five
   specs. Suggested sequencing: deletions first (`src/Templates.V2`,
   `src/Templates.DotNet`, channel-filter scripts + targets, the
   workload-enumeration/channel-mapper/manifest-reader classes,
   `ITemplateEngineProvider`+registry+`EngineIds`), then the new
   `src/Templates.Engine` host (port the proven spike patterns:
   relocated-hive `DefaultPathInfo`, host-side post-action dispatcher,
   `func-extension-bundle` constraint, `msbuild:` bind source), then corpus
   conversion (Node/Python → template.json; .NET = add func package types +
   `func.host.json` upstream), then search service (SP-7) and the `func init`
   redesign (SP-8) as parallel workstreams. Use `/opsx:apply` to drive it.
2. **Remaining spikes** (lower risk, can run alongside impl): SP-3
   (concurrent-install locking), SP-4 (adversarial: malicious component pack
   / assembly in a payload can't execute), SP-7 (discovery pipeline end to
   end), SP-8 (project-template `type: project` through an init flow),
   constraint evaluation (`func-extension-bundle` custom `ITemplateConstraint`).
3. **Parallel human-track (don't block):** jviau merge conversation; Python
   stack owners sign-off on D13 + templates-update-flow (§2.7).
4. **Before archiving:** revise the legacy `docs/proposed/*.md` specs (§4);
   decide OQ-17 (PowerShell) as a follow-up change.

---
