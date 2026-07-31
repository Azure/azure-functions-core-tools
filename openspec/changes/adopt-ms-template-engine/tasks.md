# Tasks — adopt-ms-template-engine

> §3 is the implementation checklist, derived from the five spec deltas.
> Tasks 0–2 record the design/spike history that produced it.

## 0. Gating spike — ✅ DONE 2026-07-30 (GO). Results: design.md §6B + D32; scratchpad `spike/RESULTS.md`

SP-6 (acquisition into relocated func hive), SP-2 (Python append flows +
staging), D28 (upstream .NET templates load verbatim, 36), and SP-1
(single-file publish; engine libs 0.84 MB) all validated. Refinements
folded into design: global-settings-dir isolation, host-side post-action
dispatch (no `IPostActionProcessor`), corrected size. Still open from the
spike scope: SP-3 (concurrency/locking), SP-4 adversarial security,
SP-7 (discovery), SP-8 (project-template/init), constraint evaluation.

- [x] 0.1 Walking-skeleton spike (SP-1 + SP-2 + slice of SP-5, design.md §5):
      scratch console harness (outside `src/`) that hosts
      `Microsoft.TemplateEngine.Edge` + `Orchestrator.RunnableProjects`
      single-file published; mounts one hand-converted Node template folder,
      one hand-converted Python append template (custom post-action GUID +
      processor), and the upstream `ItemTemplates` nupkg; runs
      list → dry-run → instantiate → append. Record: cold/warm timings,
      binary-size delta, scaffold-output diffs vs current engines.
      Exit: confirms design §2.5 (promote to decided) or triggers fallback
      decision with the user.

## 1. Design review (complete 2026-07-28 — user-journey walkthrough, design.md §6)

- [x] 1.1 Walk each journey stage S1–S9 with the user; resolve attached open
      questions in order; update design.md/context.md as decisions land.
      Outcome: all OQs resolved into decisions D7–D15; SP-2/SP-3 narrowed;
      OQ-17 (PowerShell templates) parked as follow-up-change scope.

## 2. Additional spikes from the 2026-07-30 pivot (D21–D27)

- [x] 2.1 SP-6: engine-managed acquisition spike — **DONE 2026-07-30** with
      Task 0 (design.md §6B): install/uninstall into the relocated func
      hive, isolation from `dotnet new` (global-settings-dir override),
      cache truthfulness, offline read paths. Still open from SP-6's
      scope: update-from-feed and concurrent-install locking → carried
      into §3.2 as tasks 3.2.5/3.2.6.
- [ ] 2.2 SP-7: discovery-service spike — run
      `Microsoft.TemplateSearch.TemplateDiscovery` with
      `packageType=FuncItemTemplates`/`FuncAppTemplates` queries against a
      local feed; produce `NuGetTemplateSearchInfoVer2.json`; consume it
      from a func host via the search coordinator with a local-override
      URI.
- [ ] 2.3 SP-8: `Empty` project-template spike — author one stack's Empty
      project template and drive it through an init-shaped flow (engine
      create + CLI-owned `.func/config.json` write).

## 3. Implementation

> Derived from the five spec deltas (`template-engine-host`,
> `template-packages`, `template-scaffolding`, `template-search`,
> `project-templates`) plus design.md §2A/§4. **§3.1 → §3.2 → §3.3 is a
> hard sequence** (each phase compiles on the previous); §3.4, §3.5 and
> §3.6 are parallel workstreams once §3.3 lands. Each item names the spec
> requirement it satisfies.
>
> **Definition of done for every item:** code + xUnit/NSubstitute coverage
> of success *and* failure paths, a clean `dotnet build -c Release`
> (CI sets `TreatWarningsAsErrors`), and the phase's targeted tests green.
>
> **Placement rulings taken at implementation time (2026-07-30, user):**
> template-package content projects live at `src/Templates/<Stack>/`
> (`Templates.<Stack>.csproj`, next to `src/Templates.Engine`); the
> discovery service lives in this repo at `tools/TemplateDiscovery/` with
> an `eng/ci` pipeline that publishes the index.

### 3.1 Phase 1 — Demolition (blocking)

- [ ] 3.1.1 **Preserve the Node V2 corpus first**: copy
      `src/Workloads/Templates/Node/content/v2/**` (`templates.json`,
      `bindings/userPrompts.json`, `resources/Resources.json`,
      `templates/_bindings.json`) to the conversion input location used by
      3.4.2 — it is the only source for the 33 hand-curated Node templates.
- [ ] 3.1.2 Delete `src/Templates.V2/` and `test/Func.Tests/Templates/V2/`;
      drop the `ProjectReference` from `src/Func/Func.csproj` and the entry
      from `Azure.Functions.Cli.slnx`.
- [ ] 3.1.3 Delete `src/Templates.DotNet/` and
      `test/Func.Tests/Templates/DotNet/` (hive provisioner, path provider,
      `dotnet new` shell-out, payload/source readers); drop the
      `ProjectReference` + slnx entry. Removes the dotnet-on-PATH
      requirement. (scaffolding: *Failure mapping and isolation*)
- [ ] 3.1.4 Delete the provider seam (D20):
      `src/Abstractions/Templates/{ITemplateEngineProvider,EngineIds,
      IInstalledTemplatesWorkloads}.cs`,
      `src/Func/Templates/{ITemplateEngineProviderRegistry,
      TemplateEngineProviderRegistry,InstalledTemplatesWorkloads}.cs`,
      `test/Func.Tests/Templates/TemplateEngineProviderRegistryTests.cs`;
      remove `EngineId` from `FunctionTemplateInfo`; drop the registry
      registration from `TemplatesServiceCollectionExtensions`.
      (host: *CLI-internal engine hosting*)
- [ ] 3.1.5 Delete channel + sidecar machinery (D16):
      `src/Func/Templates/{TemplatesChannelMapper,
      TemplatesWorkloadManifestReader,TemplatesWorkloadConstants}.cs` and
      their tests; remove pipeline step 4 (`SelectTemplatesWorkload`) and
      step 11b (`ValidateMinBundleVersion`) from `NewCommandRunner`; remove
      `NoTemplatesWorkloadForChannel` and `MinBundleVersionTooOld` from
      `TemplateApplicationFailure`.
- [ ] 3.1.6 Delete templates-workload packaging (D21):
      `src/Workloads/Templates/**` (three csprojs, content,
      `Directory.Version.props`) + slnx entries;
      `eng/build/Workloads/{Workloads.Templates.targets,
      Workloads.Templates.DotNet.targets,
      Workloads.Templates.SourceBundleVersion.props}`;
      `eng/scripts/{fetch-bundle-extensions-json,
      filter-node-templates-by-bundle,filter-templates,
      hydrate-dotnet-templates}.ps1`;
      `eng/ci/release/official-release.workload.templates.{dotnet,node,
      python}.yml`; and the templates rows in the stack→workload map in
      `eng/scripts/debug-workloads.ps1` (~L169–171).
- [ ] 3.1.7 Trim `TemplateMetadata` and the `--list` JSON envelope to the
      surviving fields. **Flag for the user:** `engineId`,
      `requiresExtensionBundle`, `minBundleVersion`
      (`NewCommandRenderer.cs` ~L165) describe concepts deleted by
      D16/D20 — confirm dropping them vs. emitting constants, since the
      envelope is a documented surface.
- [ ] 3.1.8 Gate: `dotnet build -c Release` and
      `dotnet test --filter "FullyQualifiedName~Templates"` green with
      `func new` temporarily engine-less (typed "no templates engine"
      failure) — the landing zone for §3.2.

### 3.2 Phase 2 — `src/Templates.Engine` host

- [ ] 3.2.1 Scaffold `src/Templates.Engine/Templates.Engine.csproj`
      (net10, internal by default, `InternalsVisibleTo` Func.Tests); add to
      slnx + `Func.csproj`; pin `Microsoft.TemplateEngine.{Abstractions,
      Edge,Orchestrator.RunnableProjects,Utils}` in
      `eng/build/Packages.props`; tests under
      `test/Func.Tests/Templates/Engine/` (mirrors the retired V2/DotNet
      layout). (host: *CLI-internal engine hosting*)
- [ ] 3.2.2 `FuncTemplateEngineHost : ITemplateEngineHost` — identifier
      `func`, version = CLI version, `ILogger` bridge, component set fixed
      at construction (Edge defaults + RunnableProjects generator + func
      components), host params for `HostIdentifier`.
      (host: *CLI-internal engine hosting*)
- [ ] 3.2.3 Relocated hive: `IFuncTemplateEnginePaths` overriding
      `DefaultPathInfo.globalSettingsDir` → `<func-home>/template-engine/
      func/<cli-version>` (spike §6B: host id alone is **not** isolation),
      resolved via the existing `FuncHomeResolver`. Test asserts
      `~/.templateengine` is untouched by install/list/scaffold.
      (host: *Engine-managed template acquisition*)
- [ ] 3.2.4 Engine session: lazily-constructed singleton holding
      `EngineEnvironmentSettings` + `TemplatePackageManager` for the
      process lifetime; disposal on shutdown; single-file-safe path
      resolution (no `AppContext.BaseDirectory` assumptions).
- [ ] 3.2.5 Acquisition service (`IFuncTemplatePackageService`):
      install (`pkg[::ver]`, `--source`), uninstall, update (installed vs.
      source), list-installed — over the engine's managed provider + NuGet
      installer, writing nothing to the workload registry.
      (host: *Engine-managed template acquisition*)
- [ ] 3.2.6 **SP-3**: cross-process locking around hive writes; tests for
      two simultaneous installs and an install landing between reconcile
      and query. (host: *Engine-managed template acquisition*)
- [ ] 3.2.7 Cache behavior: per-CLI-version scoping, transparent rebuild on
      unparsable/format-mismatched cache, opportunistic cleanup of stale
      sibling version dirs, all read paths offline.
      (host: *Template cache behavior*)
- [ ] 3.2.8 `func-extension-bundle` constraint component
      (`ITemplateConstraintFactory`, `{ id, version-range }`) with bundle
      context supplied by the CLI (project-resolved for `func new`,
      latest-available for init per D30); evaluation results surfaced to
      the orchestrator so restricted templates keep their call-to-action.
      (host: *Constraint components*)
- [ ] 3.2.9 Host-side post-action dispatcher keyed by `IPostAction.ActionId`
      (there is no engine `IPostActionProcessor` — §2.6): func append,
      manual instructions `AC1156F7…`, add package/project reference
      `B17581D1…` (idempotent, targeted csproj XML edit); unknown ids fall
      through to `manualInstructions` (silent when empty); `continueOnError`
      semantics honored. (host: *Post-action allowlist*)
- [ ] 3.2.10 Append processor per D13/§2.5: staging-directory
      instantiation, target resolution from `targetFileParam`/
      `appObjectParam`, create-with-header vs. append, blueprint create +
      printed registration instructions, duplicate `def <name>(` guard (no
      `--force` override), staged-file cleanup, target reported as
      `Modified:`. (scaffolding: *Python append flows*)
- [ ] 3.2.11 `msbuild:` bind-symbol source reading properties (e.g.
      `TargetFramework`) from the project file in the working directory.
      (host: *msbuild bind-symbol source*)
- [ ] 3.2.12 `func.host.json` reader + model: `symbolInfo[] { id, longName,
      isHidden, validator { expression, errorText } }` plus top-level
      `functionName.validator`; engine-inert, consumed by the hydrator.
      (packages: *func.host.json contract*)
- [ ] 3.2.13 Catalog service: cache query → `FunctionTemplateInfo` (stack
      from `azfunc-stack`, language from `tags.language`, trigger from
      `azfunc-trigger`, prompts from `ParameterDefinitions` + host file),
      `groupIdentity` dedupe, constraint filtering that retains the
      restricted set, per-template scan isolation with `[packageId]`-
      prefixed warnings, zero-template package → reinstall hint.
      (scaffolding: *Catalog sourced from the engine cache*,
      *Failure mapping and isolation*)
- [ ] 3.2.14 Scaffolding service: `GetCreationEffectsAsync` dry run →
      `AlreadyExists` unless `--force` (engine overwrite mode);
      `InstantiateAsync`; status mapping (`MissingMandatoryParam`/
      `InvalidParamValues` → `InvalidPrompt`, `CreateFailed` →
      `WriteFailed`/`ProviderError`); result = engine-created ∪
      post-action-modified. (scaffolding: *Create-flow scaffolding*)
- [ ] 3.2.15 **SP-4** adversarial tests: package carrying an assembly, a
      template referencing a non-built-in component, and a run-script post
      action → nothing loads or executes; failures are diagnostics.
      (host: *No code execution from template content*)

### 3.3 Phase 3 — `func new` on the engine

- [ ] 3.3.1 Rewire `NewCommandRunner`: keep steps 1–3, 5, 7–10, 13; steps 6
      (list) and 12 (apply) call the engine services directly (no
      registry); constraint context = the project's resolved bundle;
      `MissingExtensionBundle` stays a hard error.
      (scaffolding: *Constraint gating with call-to-action*)
- [ ] 3.3.2 `TemplateOptionHydrator` source switch to
      `ITemplateInfo.ParameterDefinitions` + `func.host.json` (aliases,
      hidden flags, regex validators) with the stack default function-name
      validator from `IProjectInitializer`; choice symbols constrain
      values; hidden symbols hydrate nothing but stay CLI-settable.
      (scaffolding: *Option hydration from live template metadata*)
- [ ] 3.3.3 `--template` resolution against every declared `shortName`
      (case-insensitive, incl. legacy suffixed aliases); unmatched id keeps
      the existing unknown-template error + catalog hint.
      (scaffolding: *Template resolution by shortName*;
      packages: *Unified template identity scheme*)
- [ ] 3.3.4 Constraint-gating UX: restricted templates hidden from
      `--list`, picker and completion; a requested id whose only match is
      bundle-restricted → call-to-action naming the bundle change, exit
      non-zero. (scaffolding: *Constraint gating with call-to-action*)
- [ ] 3.3.5 `NewCommandRenderer`: `Created:` ∪ `Modified:` in plain and
      json output; warning channel for degraded post actions and skipped
      templates. (scaffolding: *Create-flow scaffolding*)
- [ ] 3.3.6 Lifecycle/search flags on `NewCommand`: `--search [term]`,
      `--install <pkg[::ver]>`, `--uninstall <pkg>`,
      `--update [<pkg> | --all]`, `--source <feed>`; mutual-exclusion
      validation, help text, and **gate bypass** (no project/profile
      requirement) per D30.
      (scaffolding: *Template package management folds into func new*)
- [ ] 3.3.7 `TemplatePicker`, tab completion and the `--help` "Available
      templates" section fed from the new catalog service; `--list` JSON
      envelope per the 3.1.7 ruling.
- [ ] 3.3.8 Tests: rework `test/Func.Tests/Templates/*` and
      `Commands/NewCommandTests.cs` onto the new seams; add an end-to-end
      test over a fixture hive (install a local folder package → list →
      scaffold → uninstall).

### 3.4 Phase 4 — Template packages & corpus conversion (parallel)

- [ ] 3.4.1 Package projects `src/Templates/Node/Templates.Node.csproj` and
      `src/Templates/Python/Templates.Python.csproj` producing
      `Microsoft.Azure.Functions.Templates.<Stack>` with **both**
      `FuncItemTemplates` and `FuncAppTemplates` package types, plain
      semver, content at package root (`<TemplateName>/.template.config/`);
      shared `eng/build/Templates/TemplatePackage.targets` replacing the
      deleted workload targets; slnx entries; release pipelines
      `eng/ci/release/official-release.templates.{node,python}.yml`.
      (packages: *Packages identified by func package types*,
      *Official packages*)
- [ ] 3.4.2 One-time V2 → template.json converter (PowerShell or a .NET
      file-based app under `eng/scripts/`): inline `files` → real files,
      `$(FUNCTION_NAME_INPUT)` → `sourceName`, `userPrompts` → symbols +
      `func.host.json`, `Resources.json` →
      `localize/templatestrings.*.json`; run over the corpus preserved in
      3.1.1, then hand-finish per template (tokenise per-binding literals).
- [ ] 3.4.3 Node corpus finish: unified `shortName` primaries (`http`,
      `timer`, …) with `HttpTrigger` and legacy suffixed aliases;
      `tags { language, type: item, azfunc-stack, azfunc-trigger }`;
      per-template `func-extension-bundle` constraints derived from the
      retired `_bindings.json` map (replaces pack-time subsetting).
      (packages: *Per-template gating declared as constraints*)
- [ ] 3.4.4 Python corpus: author from the bundle's `StaticContent/v2`
      Python entries as one template + one `__snippet__.py` per trigger
      with the func append post action; `AppFile`/`AppObject` symbols and
      the `func.host.json` mapping (`--file`, hidden `AppObject`).
      (scaffolding: *Python append flows*)
- [ ] 3.4.5 `Empty` project templates (`tags.type: "project"`) for Node and
      Python reproducing today's `func init` output (host.json, ignore
      files, stack project files, Python's `function_app.py`); no bundle
      constraints (D30).
      (project-templates: *Per-stack Empty project templates*)
- [ ] 3.4.6 Parity harness (completes SP-5): diff `--list`,
      `--template <id> --help`, and scaffold output against the pre-change
      engines for a representative template set; record intentional deltas
      (D8/D19 ids) in the change.
- [ ] 3.4.7 **EXTERNAL** (`C:\root\repos\templates\Functions.Templates`):
      add `FuncItemTemplates` to
      `Microsoft.Azure.Functions.Worker.ItemTemplates` and
      `FuncAppTemplates` to `…Worker.ProjectTemplates` (retaining
      `Template`), and add `func.host.json` per template (`--namespace`,
      `--access-rights`, …). Cross-repo PR; func-side work consumes the
      published packages. (packages: *Official packages*; D10/D28)

### 3.5 Phase 5 — Search (parallel)

- [ ] 3.5.1 **SP-7** (§2.2) as this phase's first slice: TemplateDiscovery
      with `packageType=FuncItemTemplates|FuncAppTemplates` against a local
      feed → `NuGetTemplateSearchInfoVer2.json` → consumed by a func host
      via a local-override URI.
- [ ] 3.5.2 `tools/TemplateDiscovery/` — func discovery service based on
      `Microsoft.TemplateSearch.TemplateDiscovery`: package-type queries,
      `template.json` prefilter, engine scan, `--diff` incremental runs +
      `nonTemplatePacks.json` skip-list, index output.
      **Decide first (needs a user ruling):** the tool's query set is a
      hard-coded dictionary keyed by an internal enum and its feed URL is a
      constant, and the consumer's index-URI override is an `internal` test
      constructor (context.md §11.1) — so func either contributes
      configurability upstream or forks the tool. Same call covers naming
      the func equivalents of `DOTNET_NEW_SEARCH_FILE_OVERRIDE` /
      `DOTNET_NEW_LOCAL_SEARCH_FILE_ONLY`.
      (search: *Discovery service builds the func template index*)
- [ ] 3.5.3 `eng/ci/templates-search-index.yml`: daily incremental run plus
      on-demand trigger, publishing to Functions CDN blob storage behind
      the `aka.ms` vanity URI (D29); document the URI and how to repoint it.
- [ ] 3.5.4 CLI search consumer: search coordinator/provider pointed at the
      func index URI, local cache, local-file override toggle, actionable
      error when the index is unreachable and no cached/local copy exists.
      (search: *CLI search over the published index*)
- [ ] 3.5.5 `--source <feed>` direct NuGet search-API query at invocation
      time, filtered to the func package types.
      (search: *Direct feed search via --source*)
- [ ] 3.5.6 Search rendering: package id, version, template names,
      stack/language tags, plus installed-state annotation with an
      update-available marker.
      (search: *Search results distinguish installed state*)

### 3.6 Phase 6 — `func init` & project templates (parallel)

- [ ] 3.6.1 **SP-8** (§2.3) as this phase's first slice: drive one stack's
      `Empty` template through an init-shaped flow (engine create +
      CLI-owned `.func/config.json` write) before refactoring init.
- [ ] 3.6.2 Shrink `IProjectInitializer` (D31): remove `InitializeAsync`
      and `GetInitOptions`; keep `Stack`, `WorkerRuntimeAliases`,
      `DisplayName`, `SupportedLanguages`/`SupportedLanguageAliases`,
      `DefaultFunctionNameValidator`; add the stack's official template
      package ids. Retire `IInitOptionRegistry`/`InitOptionRegistry`/
      `CommonInitOptions` if no customer survives; update all six stack
      workloads and `test/Workloads/Stacks/*ProjectInitializerTests`.
      (project-templates: *Thin stack contract and init orchestration*)
- [ ] 3.6.3 Delete `DotNetProjectInitializer`'s scaffolding, the
      `dotnet new func` shell-out, and its hive path provider.
      (project-templates: *No dotnet shell-out at init*)
- [ ] 3.6.4 Init core orchestration (`InitCommand` + a runner mirroring
      `NewCommandRunner`): stack resolution → language → ensure official
      packages (auto-install with a clear message; offline → actionable
      error naming the packages) → project-template selection → engine
      scaffold → CLI-owned `.func/config.json` write (templates never
      author CLI config).
      (project-templates: *First-run auto-install of official packages*,
      *CLI-owned post steps*)
- [ ] 3.6.5 `--template <id>` plus the wizard project-template step
      filtered to the resolved stack (installed templates + index results;
      selecting an uninstalled index entry installs it with a message;
      offline degrades silently to installed-only; non-interactive without
      `--template` uses `Empty`).
      (project-templates: *Init wizard gains a project-template step*)
- [ ] 3.6.6 Init-scope constraint context = latest available extension
      bundle, resolved offline from installed bundle content where
      possible (D30); `func new`'s posture unchanged.
      (project-templates: *Constraint context at init time*)
- [ ] 3.6.7 Init options derived from the selected project template's
      symbols through the shared `TemplateOptionHydrator`; a stack workload
      with no official `Empty` package → actionable error.
      (project-templates: *Thin stack contract and init orchestration*)
- [ ] 3.6.8 `func setup` (`SetupFeatureCatalog`/`SetupRunner`): optional
      profile-driven pre-install of template packages (D27).
- [ ] 3.6.9 Tests: `test/Func.Tests/Commands/InitCommandTests.cs` and the
      stack workload test projects updated to the thin contract + template
      flow.

### 3.7 Phase 7 — Validation, docs, release

- [ ] 3.7.1 Full `dotnet build -c Release` + `dotnet test`; single-file
      publish check (`VerifySingleFilePublish`) and measured binary delta
      against design §3 (engine libs 0.84 MB).
- [ ] 3.7.2 Re-measure performance at realistic corpus size (cold/warm
      list, install) against the §3 budgets; escalate if the cold scan
      exceeds ~500 ms.
- [ ] 3.7.3 Telemetry: drop the engine-id axis; keep `cli.new.*`; add
      install/update/search outcome events if warranted.
- [ ] 3.7.4 Release notes: templates are no longer workloads (migration
      note for `func workload install *-templates`), new `func new`
      lifecycle flags, unified ids + aliases, no dotnet-on-PATH, orphaned
      dotnet hive left on disk (D14).
- [ ] 3.7.5 Repo docs: update `AGENTS.md`, READMEs and skills that
      reference templates workloads; append phase checkpoints to
      `context.md` §12 as they land.

## 4. Cross-repo & human track (non-blocking)

- [ ] 4.1 jviau merge conversation on `func-universal-template-engine`
      (design §6 table is the agenda).
- [ ] 4.2 Python stack owners sign-off on D13 and the §2.7 update-flow
      change (Python template updates leave the bundle republish path).
- [ ] 4.3 `Functions.Templates` PR (3.4.7): review, publish cadence, and
      the version func pins for first-run install.

## 5. Before archiving

- [ ] 5.1 Revise the legacy specs per design §4:
      `docs/proposed/{templates-workload-spec,func-new.spec,workload-spec}.md`.
- [ ] 5.2 Decide OQ-17 (PowerShell templates) — expected outcome is a
      follow-up change, not scope creep here.
- [ ] 5.3 `openspec validate adopt-ms-template-engine` clean, then archive.

