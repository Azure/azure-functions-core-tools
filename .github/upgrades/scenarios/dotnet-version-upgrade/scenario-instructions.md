# .NET Version Upgrade

## Preferences
- **Flow Mode**: Automatic
- **Target Framework**: .NET 11 (net11.0)

## Source Control
- **Source Branch**: fabiocav/fuzzy-robot
- **Working Branch**: fabiocav/fuzzy-robot
- **Commit Strategy**: After Each Task
- **Branch Sync**: Disabled

## User Preferences

### Technical Preferences
- Upgrade all applicable projects from .NET 10 to .NET 11.
- Also upgrade pipelines and supporting files: Azure Pipelines YAML under eng/, .github workflows, global.json, Docker files, eng build props/targets, TFM/SDK references in scripts, docs, and skills/AGENTS.md mentions of .NET 10 where they describe the target framework.
- Use the scenario's Current Facts for SDK/package versions (.NET 11 is GA per Current Facts); keep packages at versions compatible with net11.
- Fix warnings at the source; never suppress them.
- Follow repo conventions in AGENTS.md and .github/instructions.

### Execution Style
- Automatic: don't stop at assessment or planning; carry through execution and validation.
- Validation: `dotnet restore`, clean Release build with `CI=true` (TreatWarningsAsErrors applies), and the relevant tests.

## Decisions
- Stay on current branch fabiocav/fuzzy-robot (already vnext-based). Do NOT check out other branches, rename, or create branches. Do not work in C:\Users\facaval\src\azure-functions-core-tools.
- Commit after each task; every commit message must include the trailer: `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`
- Do NOT push and do NOT create a PR.
- Branch sync disabled (source and working branch are the same).
- Accepted (task 02): NuGet.Packaging/Protocol/Versioning 6.13.2 → 8.0.0 and new central pin NuGet.Credentials 8.0.0 — forced by Microsoft.TemplateEngine.* 11.0.100 dependency.
- Accepted (task 02): removed Microsoft.Extensions.DependencyInjection.Abstractions PackageReference from Abstractions.csproj (NU1510: now in-box with net11).
- System.CommandLine: stay on 2.0.x (3.0.0 is a major bump affecting the public Abstractions package and is not required for net11).
- Workloads.Sdk stays netstandard2.0 (MSBuild task library). Do not bump packages it consumes (System.Text.Json, System.Reflection.MetadataLoadContext) beyond what the MSBuild host provides; if central pins are shared, keep Workloads.Sdk's effective versions unchanged.
- LangVersion: bump 14 → 15 in eng/build/Engineering.props (the C# version paired with .NET 11); fix any resulting warnings at source.
- `func init` default/generated framework for user projects (DotNetProjectInitializer) is product behavior — leave unchanged.
- Upgrade Terminal.Native.Probe.csproj (not in slnx) together with Func.Tests.
- Keep the .NET 9 install required by ESRP signing in pipelines; only update outdated "net10" comments.
- Set global.json SDK directly to 11.0.100 (keep existing rollForward policy); don't rerun validate_dotnet_sdk_in_globaljson.

## Upgrade Options

### Strategy
- Upgrade Strategy: All-at-Once

### Reliability
- Test Coverage: Skip

## Strategy
**Selected**: All-at-Once
**Rationale**: All 28 projects are SDK-style on modern .NET (27 on net10.0, 1 netstandard2.0), every project is rated Low difficulty, there are no incompatible packages or binary-incompatible APIs, and all package versions are centrally pinned in eng/build/Packages.props — a mechanical TFM/package bump best done atomically (selected on the user's delegated behalf over the size-based Top-Down default).

### Execution Constraints
- Single atomic upgrade: all project TFMs (plus Terminal.Native.Probe.csproj) and central package pins change together in one task; no tier ordering.
- global.json must point at SDK 11.0.100 before any net11.0 build is attempted.
- After the atomic change: restore, build the whole solution and fix all compilation errors/warnings in one bounded pass (CI=true, warnings fixed at source, never suppressed), then run tests before completing the task.
- Commit strategy stays "After Each Task" (explicit user decision overrides the All-at-Once single-commit recommendation).
- Supporting files/docs follow the code upgrade; finish with a full clean Release validation.
