## 1. Engine hosting foundation

- [ ] 1.1 Add `Microsoft.TemplateEngine.Edge`, `Orchestrator.RunnableProjects`, and abstractions package references
- [ ] 1.2 Implement a custom `ITemplateEngineHost` (host identifier `func`) with a func-owned settings location isolated from `~/.templateengine/dotnetcli`
- [ ] 1.3 Wire a per-invocation engine bootstrapper that accepts resolved project context (stack, language, bundle) as host params
- [ ] 1.4 Register default RunnableProjects components plus the func custom-constraint component set

## 2. Constraints (gating source of truth)

- [ ] 2.1 Implement the `func-extension-bundle` `ITemplateConstraintFactory`/`ITemplateConstraint` evaluating `{ id, version-range }` against the host-param bundle context
- [ ] 2.2 Emit `CreateRestricted` results with a call-to-action message for bundle-gated templates
- [ ] 2.3 Implement the latest-stable synthetic bundle context (via `IExtensionBundleResolver`) used when no `host.json`/bundle resolves
- [ ] 2.4 Confirm the built-in `host` constraint (`hostname: func`) hides func templates under plain `dotnet new`

## 3. Selection and resolution

- [ ] 3.1 Implement stack resolution order: explicit `--language` → ambient directory inference → hard error to `func init`/`--language`
- [ ] 3.2 Implement `groupIdentity`/`shortName` group resolution and derive stack from the winning variant (no hardcoded language→stack table)
- [ ] 3.3 Implement language tiebreak: ambient signal (tsconfig/`package.json`) → `--language` → prompt only when still ambiguous
- [ ] 3.4 Implement `--language` override across ambient stack with a non-blocking advisory note; ensure no `--stack` flag exists
- [ ] 3.5 Implement failure ranking: swallow soft stack mismatch, surface hard bundle-gate call-to-action

## 4. Install front-door

- [ ] 4.1 Route template packages to `TemplatePackageManager.InstallAsync` into the func hive by package type
- [ ] 4.2 Route func workload packages to the existing workload installer from the same front-door
- [ ] 4.3 Expose list / update / uninstall for template packages against the func hive
- [ ] 4.4 Verify installs never touch the `dotnet new` cache

## 5. Orchestrator slimming and removals

- [ ] 5.1 Reduce `NewCommandRunner` to context-resolver + host-param injector + result presenter
- [ ] 5.2 Remove `ITemplateEngineProvider`, its registry, and the `Templates.V2` and `Templates.DotNet` projects
- [ ] 5.3 Remove `IInstalledTemplatesWorkloads`, the `templates-workload.json` sidecar generation, min-bundle gate, and channel-match logic
- [ ] 5.4 Remove the DotNet item-template hive provisioner and `dotnet new` shell-out path

## 6. Template packaging migration

- [ ] 6.1 Re-author Node/Python templates from the V2 DSL to `.template.config/template.json` with `groupIdentity`/`shortName`/tags/constraints
- [ ] 6.2 Re-package .NET item templates as standard template packages carrying func constraints (drop `dotnet-templates.json` projection)
- [ ] 6.3 Mark template packages with the template package type consumed by the install front-door
- [ ] 6.4 Retire per-channel pack-time subsetting and prerelease-label→bundle-id mapping from the build

## 7. Verification

- [ ] 7.1 Validate `func new <id>` cross-stack selection in Node, Python, and .NET projects
- [ ] 7.2 Validate empty-directory scaffolding via `--language` and the no-stack error path
- [ ] 7.3 Validate bundle gating: eligible, hidden-too-low (with call-to-action), and latest-stable-assumed cases
- [ ] 7.4 Validate isolation: func templates invisible to `dotnet new` and vice versa
- [ ] 7.5 Run `openspec validate func-universal-template-engine --strict`
