# Design feedback — team review 2026-07-31

> **What this is:** open questions and requested design changes raised in the
> team review of `adopt-ms-template-engine`, captured verbatim in intent and
> annotated with implementation impact. **Nothing here is ratified.** Items
> marked *Decided (team)* still need to land in `design.md` §7 as numbered
> decisions before implementation; items marked *Open* need a ruling.
>
> **Numbering:** open questions continue the `design.md` §5 sequence from
> **OQ-24** (OQ-23 was the last used); decision numbers are *proposed* and
> continue from **D34** (D33 was the last used).
>
> **Why a separate file:** the working norms put decisions only in
> `design.md` §7 and facts in `context.md`. These are neither yet — they are
> a review queue. Fold each item into `design.md` as it is ruled, then delete
> it from here.
>
> **State when this was written:** implementation is 42/67 tasks complete;
> `func new` runs end to end on the engine (install/list/scaffold/search),
> `func init` project templates (phase 3.6) are **not** built yet. Several
> items below are therefore cheap *now* and expensive later — flagged per item.

---

## 1. Collapse the two package types into one — `FuncAppTemplates`

**Asked:** "We don't need two separate package type for item and project
templates. Can just have `FuncAppTemplates` as one type for filtering."

**Status:** Decided (team) — needs ratifying as **D34**, superseding the
package-type half of **D26** and the type assignments in **D28**.

**Impact — cheaper than it looks.** The CLI does **not** use the NuGet package
type to tell item templates from project templates. That discrimination
already happens on the per-template `tags.type` (`item` / `project`) inside
`template.json` — see `src/Templates.Engine/Catalog/FuncTemplateTags.cs`
(`Type`, `ItemType`). The package type is only a **feed-level discovery
filter**: it decides which packages the discovery service scans and which
packages `--source` search returns. So dropping `FuncItemTemplates` loses
nothing at the CLI layer.

**Blast radius:** ~34 references across 14 files.

| Area | Files |
|---|---|
| Sample packages | `src/Templates/{Node,Python}/Templates.*.csproj` (`<PackageType>`) |
| Discovery tool | `tools/TemplateDiscovery/Program.cs`, `eng/scripts/build-template-index.ps1` |
| Feed search | `src/Func/Templates/Search/{FuncTemplateFeedSearch,IFuncTemplateFeedSearch}.cs` |
| Feed script | `eng/scripts/build-local-template-feed.ps1` |
| Specs | `specs/template-packages/spec.md` (6 refs — the *Packages identified by func package types* requirement is written around the pair) |
| Docs | `proposal.md`, `design.md`, `context.md`, `tasks.md`, `HANDOFF.md`, `tools/TemplateDiscovery/README.md` |

**Do it now, not later.** Only two packages exist and neither is published;
after `Functions.Templates` ships the upstream `.NET` packages with func
package types (task 3.4.7, cross-repo), changing the type is a breaking
re-publish.

**Open sub-question (OQ-24) — the name.** With one type covering both, a
package containing *only* item templates is still discovered under a type
named "**App**Templates", which reads as project/app templates. Options:
(a) keep `FuncAppTemplates` as asked and accept the naming oddity;
(b) use a neutral `FuncTemplates`. This is cosmetic and reversible only
before publish, so it is worth 30 seconds of ruling now. Recommend (b) unless
"app" is deliberate product language.

**Second-order consequence to accept:** a consumer can no longer tell from
the package type alone whether a package contains project templates without
scanning it. In practice the search index already carries per-template
`TagsCollection` (verified in the generated index), so search-side filtering
by `type: project` still works — but any future "list app-template packages"
feed query becomes a scan rather than a query.

---

## 2. Project-template discovery / catalog experience

**Asked:** "For project template search experience we can have some sort of
online catalog or search capability that allows user to filter based on what
they are looking for."

**Status:** Open — **OQ-25**.

**What already exists** (so this is a UX question, not a data question): the
search index (`D22`/`D29`) carries, per template, `Identity`, `ShortNameList`,
`Name`, `Description`, `Classifications` and `TagsCollection` — including our
`azfunc-stack` and `type: project` tags. A project-template catalog can be
built from today's index with no format change. Confirmed against the real
generated index during the 2026-07-31 playground run.

**To decide:**
- **Surface.** Is this the `func init` wizard's project-template step filtered
  against the index (per `D23`), a dedicated `func init --search`, or a
  browsable catalog (web) that the CLI links to?
- **Filter axes.** Stack and language are obvious. What else — scenario
  ("http api", "durable", "event-driven"), trigger type, publisher/verified
  status, popularity/downloads (the index carries `TotalDownloads` and
  `Reserved`)?
- **Ranking and trust.** Once third-party project templates are installable,
  first-party vs community needs a visible distinction. `Reserved` (NuGet
  prefix reservation) is the cheapest available trust signal.
- **Relationship to item-template search.** `func new --search` exists and
  works today. Is project-template search the same surface filtered by
  `type: project`, or a genuinely different experience?

**Dependency:** phase 3.6 (`func init` project templates) is unimplemented, so
this can be designed into it rather than retrofitted — but it should be ruled
before 3.6 starts.

---

## 3. Document that init's stack/language options are *installed workloads*

**Asked:** "Need to document that the stack and language options presented in
init are pre-installed workloads."

**Status:** Documentation gap — no design change.

**Current state:** the behavior is specified (`specs/project-templates/spec.md`,
*Thin stack contract and init orchestration*: stack resolution comes "from
installed stack workloads' metadata"), and `D31` makes `IProjectInitializer`
the metadata carrier. What is **not** documented is the **user-facing
consequence**: the stack and language lists a user sees are bounded by what is
installed on that machine, not by what Azure Functions supports.

**Add to:** `specs/project-templates/spec.md` (an explicit scenario), and the
user-facing init help text.

**Open sub-question (OQ-26):** when a user's desired stack is *not* installed,
does init say so and tell them how to get it (`func setup` / workload install),
or silently show a short list? Silently showing three stacks on a machine that
supports six is a bad first-run experience — the empty/short-list case needs a
call to action. This mirrors the "stack workload without an official `Empty`
package" error already specified.

---

## 4. `func new` completion message overstates what was created

**Asked:** "The function created message at the end of the func new creation
should not say function x created as it could one or more function or may not
be function at all just a regular class or a file."

**Status:** Change request — small, concrete, ready to implement.

**Where:** `src/Func/Templates/NewCommandRenderer.cs:158`

```csharp
_interaction.WriteSuccess($"Created function '{functionName}' from template '{template.Id}'.");
```

**Why it is wrong:** the message asserts a singular *function* was created.
A template may legitimately produce several functions, or none at all — a
class, a helper module, a config file. The renderer already prints accurate
`Created:` / `Modified:` file lists immediately above this line, so the
summary is both redundant and less truthful than the detail it summarizes.

**Options (product wording call — not mine to make):**
1. Neutral summary, let the file lists carry the detail:
   `Template 'http' applied.`
2. Count-based, derived from the actual result:
   `Created 2 files from template 'http'.`
3. Name-based without the "function" noun:
   `Created 'GetOrders' from template 'http'.`

**Recommendation:** (1) or (2). The `--name` value is not necessarily a
function name, so (3) still implies a single named artifact.

**Note:** the append flow (Python) already reports `Modified:` rather than
`Created:`, so whatever wording is chosen must read correctly when *nothing*
was created and an existing file was edited — today it says "Created function
'x'" after only modifying `function_app.py`, which is doubly wrong.

**Also check:** `--output json` uses the same result shape; keep the machine
surface consistent with whatever the human wording becomes.

---

## 5. How does `func init` template selection interact with profiles?

**Asked:** "Need to have an open design question on how the func init template
experience work with profiles as well."

**Status:** Open — **OQ-27**.

**What exists:** the CLI has a profile system (`IProfileResolver`,
`func profile`, `.func/config.json` project profile binding), and `D27`
already says `func setup` **may** pre-install template packages as part of
profile-driven setup. That is the only defined intersection today.

**To decide:**
- Can a profile **pin** a project template (e.g. an enterprise profile that
  always scaffolds from an internal `Contoso.Standard.Api` template)?
- Can a profile **constrain** the selectable set (only approved templates /
  only an internal feed), and is that advisory or enforced?
- Can a profile supply **default symbol values** for init options (the
  `Empty` template's symbols, per `D31`)?
- Can a profile pin a template **package version**, for reproducible init
  across a team?
- Precedence when a profile and an explicit `--template` disagree.
- Does the profile travel with the *project* (`.func/config.json`) or the
  *user*, and which wins?

**Why it matters now:** phase 3.6 builds the init pipeline. Whether profiles
can inject template selection/defaults changes the shape of the init
orchestration, so this should be ruled before 3.6, not bolted on after.

---

## 6. Stack and language version evolution — filter templates by compatibility

**Asked:** "We also need a story on how the infrastructure will handle stack
and languages as they evolve and upgrade. Can potentially have that baked into
templating engine based on the stack like information that the stack workload
installed or selected is node 12 or 14 and filter templates based on
compatible templates."

**Status:** Open — **OQ-28**. Strong proposed direction below.

**Proposed mechanism — a `func-stack` constraint, mirroring the proven
`func-extension-bundle` one.** This is the same shape that already works:

- `template.json` declares `constraints: { stack: { type: "func-stack",
  args: { id: "node", version: "[18.0.0, )" } } }`.
- The func host registers an `ITemplateConstraint` evaluating it against the
  resolved stack context, exactly like
  `src/Templates.Engine/Constraints/ExtensionBundleConstraint.cs` does for
  bundles, fed by a context accessor like
  `IFuncExtensionBundleContextAccessor`.
- Unmet ⇒ template hidden from the catalog, with a call-to-action on explicit
  request — the mechanism built and verified for bundle gating (`D16`), now
  including `FindRestrictedAsync` so the CTA renders.

The expensive machinery already exists and is tested; this is one more
constraint component plus a context source. That is the argument for doing it
this way rather than inventing a parallel filter.

**To decide:**
- **Which axes.** These are *different* and probably both needed:
  - *worker/stack version* (Node 18 vs 20, the workload that is installed),
  - *language version* (Python 3.11 vs 3.12),
  - *programming model version* (Node v3 vs v4, Python v1 vs v2) — note the
    v5 CLI deliberately dropped the programming-model knob; reintroducing it
    as a constraint axis is a product decision, not a mechanical one.
- **Where the version comes from.** Installed stack workload metadata is the
  obvious source at `func new` time. At `func init` time there is no project
  yet — `D30` already had to solve exactly this for bundles by evaluating
  against "latest available"; the same rule likely applies.
- **Upgrade story.** What happens to a project pinned to an older stack when
  templates move on: are old templates retained (older package versions
  installable), or does the constraint simply hide new templates with an
  explanatory CTA? The latter is nearly free given the current design.
- **Authoring burden.** Every template gaining a stack constraint is a real
  cost to the corpus (33 Node templates and counting). Consider a
  package-level default that individual templates can override.

---

## 7. Authoring experience for `func init` project templates

**Asked:** "Need to document the authoring experience for func init project
templates. The idea is that there will be a pipeline that will scan a set of
GitHub repositories used mainly the ones used for quickstarts, go through
their releases and package those releases as functionAppProject templates and
publish them if necessary."

**Status:** Open — **OQ-29**. Needs a documented design; substantially new
scope.

**Relationship to what exists:** this is a **second, different** discovery
pipeline. The one built (`tools/TemplateDiscovery`, vendored 2026-07-31)
scans **NuGet feeds** for packages that are *already* template packages. This
proposal scans **GitHub repositories** that are *not* template packages and
**manufactures** template packages from their releases. Different source
adapter, different trust model — but the packaging and publishing half is
shareable.

```
existing:  nuget feed ──► filter by package type ──► scan ──► index
proposed:  github repos ──► walk releases ──► synthesize template package ──► publish ──► (then indexed as above)
```

**To decide:**
- **Repo selection.** An explicit allow-list, a topic/tag convention, or an
  org-wide scan? Quickstart repos are the stated seed set.
- **Template metadata source.** Does a repo have to contain a
  `.template.config/template.json` to be packaged (opt-in, authored by the
  repo owner), or does the pipeline **synthesize** one from repo metadata
  (opt-out, zero work for repo owners)? This is the central question — it
  determines whether this is a *publishing* pipeline or an *authoring* one.
  Synthesized templates cannot express symbols, constraints or post-actions
  without a convention for declaring them.
- **`sourceName` / parameterization.** A quickstart repo is a fixed project;
  a template needs a rename token so `func init --name` works. Synthesizing
  that reliably from arbitrary repos is the hard part (project names appear
  in file names, namespaces, config, CI files).
- **Release → version mapping.** Which releases are packaged (latest only,
  every tag, semver-tagged only)? How do repo release versions map to package
  versions? What about repos with no releases?
- **Trust, provenance and support.** Publishing third-party repo content
  under a Microsoft-owned package id implies an endorsement and a support
  burden. Who owns a broken scaffold? Note the upstream discovery tool has a
  prefilter specifically to stop packages *claiming* Microsoft authorship —
  this pipeline would be deliberately doing the reverse.
- **Naming and package ids** for generated packages.
- **Cadence and cost** — release-triggered or scheduled; and the CI budget for
  scanning/packaging N repos.
- **Removal.** What happens when a quickstart repo is archived or a release is
  yanked.

**Suggested next step:** treat this as its own OpenSpec change rather than
scope creep here. It is comparable in size to the search work, and this change
is already at 42/67 with two feature phases outstanding.

---

## Summary

| # | Item | Status | Proposed # | Rule before |
|---|---|---|---|---|
| 1 | Single `FuncAppTemplates` package type | Decided (team) | D34 (+ OQ-24 naming) | 3.4.7 upstream PR / any publish |
| 2 | Project-template catalog & filtering UX | Open | OQ-25 | phase 3.6 |
| 3 | Init stack/language = installed workloads | Doc gap | (+ OQ-26 missing-stack CTA) | phase 3.6 |
| 4 | `func new` completion message wording | Change request | — | anytime (small) |
| 5 | Init templates × profiles | Open | OQ-27 | phase 3.6 |
| 6 | Stack/language version compatibility filtering | Open | OQ-28 | before corpus conversion (3.4.2–3.4.4) |
| 7 | GitHub-quickstart → project-template pipeline | Open | OQ-29 | separate change |

**Two are time-sensitive:**
- **#1** must land before the upstream `Functions.Templates` package-type PR
  (task 3.4.7) or it becomes a breaking re-publish.
- **#6** should be ruled before the 33-template Node corpus conversion
  (3.4.2–3.4.4), because retrofitting a constraint into every template
  afterwards is far more expensive than authoring it in.
