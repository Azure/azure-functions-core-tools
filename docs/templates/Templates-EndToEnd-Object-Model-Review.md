# CLI v5 Templates — Minimum End-to-End Object Model

**Purpose:** Explain the smallest architecture for one template-backed `func init` and `func new` journey. This is a discussion aid, not implementation approval or a release-readiness claim.

## Status key

- **Committed baseline:** Present at accepted Core Tools commit `883b02ee9bc10c431a67c044a7f059a082b2fcad`.
- **Local candidate:** Modified or untracked in the Lane 3 worktree; not represented by baseline GitHub links.
- **Proposed:** Intended behavior not yet implemented.

## 1. High-level component architecture

```mermaid
flowchart LR
    user([User]) --> cli["CLI Commands<br/>func init / func new"]
    cli --> context["Project and Stack Information<br/>target project, language, bundle"]
    context --> engine
    content["Installed Templates<br/>workload/package-owned<br/>installed in the dedicated CLI hive"] -->|metadata and content| engine
    subgraph engineBoundary["Template Engine boundary"]
        direction TB
        engine["Template Engine<br/>discovery, filtering, exact selection"]
        render["Generate Files Privately<br/>internal engine responsibility"]
        engine -->|selected template and evaluated inputs| render
    end
    render -->|bounded planned output| cli
    cli -->|authorized files| write["Write Files to Project<br/>CLI-owned"]
    write -->|actual changes and failures| finalize["Command and Stack Finalization"]
    finalize --> result([Result])
```

`func init` and `func new` remain command-owned workflows. Workload/package owners supply the installed templates. The Template Engine discovers installed content, selects one exact template, and generates files privately. The CLI decides which planned files may be written, applies command-specific rules, completes finalization, and reports the actual result.

### Walkthrough: `func init`

1. The user runs `func init` with a target folder, Stack, language, and options.
2. **CLI Commands** parses the request and coordinates the operation.
3. **Project and Stack Information** determines the requested Stack and language and whether the target needs new scaffolding, adoption, or repair.
4. **Installed Templates** supplies project-template metadata and content from the dedicated CLI hive.
5. The **Template Engine** filters the installed project templates and selects the exact eligible template.
6. **Generate Files Privately** invokes the selected template without changing the user's destination.
7. The CLI validates the planned files, combines them with CLI-owned configuration, and applies collision and `--force` policy.
8. **Write Files to Project** writes only the authorized files.
9. **Command and Stack Finalization** performs remaining work, such as Node.js package installation, and reports what was created, preserved, skipped, or failed.

> **Short version:** `func init` determines the requested Stack and language, selects the matching installed project template, generates the project privately, writes the authorized files, completes Stack-specific setup, and reports the actual result.

### Walkthrough: `func new`

1. The user runs `func new` with a target project, template choice, function name, and template-specific arguments.
2. **CLI Commands** parses the request and starts the operation.
3. **Project and Stack Information** resolves the existing Functions project and obtains its Stack and language.
4. If the project cannot be resolved, the CLI stops with `Functions project not initialized. Run func init to get started.`
5. The owning workload checks whether the project is compatible with the requested item template.
6. **Installed Templates** supplies item-template metadata and content from the dedicated CLI hive.
7. The **Template Engine** filters candidates, checks the supplied arguments, and selects one exact eligible template.
8. **Generate Files Privately** invokes that template without changing the project.
9. The CLI validates paths, collisions, and authorization.
10. **Write Files to Project** writes the authorized function files.
11. **Command and Stack Finalization** reports the actual outcome.

> **Short version:** `func new` resolves the existing Functions project, checks Stack compatibility, selects and generates the exact item template privately, then writes the authorized function files and reports the result.

## 2. Minimum object model

```mermaid
classDiagram
    direction TB
    class InitCommand
    class NewCommand
    class IFunctionsProjectResolver {
        <<interface>>
    }
    class FunctionsProject
    class TemplateEngineContext
    class ITemplaterFactory {
        <<interface>>
    }
    class Templater
    class TemplateEngineSession
    class TemplateCatalogEntry
    class TemplateParameterDefinition
    class TemplateResolutionResult
    class ResolvedTemplate
    class TemplateRenderResult
    class PrivateTemplateFileSystem

    InitCommand ..> IFunctionsProjectResolver : existing project/adoption handling
    NewCommand ..> IFunctionsProjectResolver : proposed admission gate
    InitCommand ..> ITemplaterFactory : proposed project-template path
    NewCommand ..> ITemplaterFactory : proposed item-template path
    ITemplaterFactory ..> TemplateEngineContext : accepts
    ITemplaterFactory ..> Templater : creates
    IFunctionsProjectResolver ..> FunctionsProject : resolves
    Templater *-- TemplateEngineSession : owns per command
    Templater --> TemplateCatalogEntry : lists
    TemplateCatalogEntry o-- TemplateParameterDefinition : describes
    Templater --> TemplateResolutionResult : resolves
    TemplateResolutionResult o-- ResolvedTemplate : selected candidate
    ResolvedTemplate --> TemplateCatalogEntry : describes candidate
    Templater ..> PrivateTemplateFileSystem : generates files privately
    Templater --> TemplateRenderResult : planned output

    style InitCommand fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style NewCommand fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style IFunctionsProjectResolver fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style FunctionsProject fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style TemplateEngineContext fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style ITemplaterFactory fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style Templater fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style TemplateEngineSession fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style TemplateCatalogEntry fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style TemplateParameterDefinition fill:#e8f5e9,stroke:#2e7d32,color:#1b5e20
    style TemplateResolutionResult fill:#fff3e0,stroke:#ef6c00,color:#4e342e
    style ResolvedTemplate fill:#fff3e0,stroke:#ef6c00,color:#4e342e
    style TemplateRenderResult fill:#fff3e0,stroke:#ef6c00,color:#4e342e
    style PrivateTemplateFileSystem fill:#fff3e0,stroke:#ef6c00,color:#4e342e
```

Green indicates committed types; orange indicates local candidate types. Some committed types also have local edits. The command-to-factory edges are proposed, not current calls. Writing files and Stack-owned compatibility are responsibilities, not mandatory new classes. The model does not require `TemplateGroup` or a request/result wrapper for every operation.

### Object journey: `func init`

1. **`InitCommand`** receives the command and owns the public `func init` experience.
2. Existing Stack and initialization infrastructure determines the requested Stack and language and whether the target needs scaffolding, adoption, or repair.
3. For a new project-template path, `InitCommand` creates a **`TemplateEngineContext`** containing the requested command, Stack, language, target, and relevant bundle information.
4. `InitCommand` passes that context to **`ITemplaterFactory`**.
5. `ITemplaterFactory` creates one command-scoped **`Templater`**.
6. `Templater` owns one **`TemplateEngineSession`**, which manages the Microsoft.TemplateEngine environment and access to installed templates.
7. `Templater` discovers **`TemplateCatalogEntry`** values and uses their metadata and **`TemplateParameterDefinition`** values to identify eligible project templates.
8. `Templater` returns a **`TemplateResolutionResult`** containing either an exact **`ResolvedTemplate`** or structured diagnostics explaining why selection failed.
9. The resolved template remains tied to the session that created it.
10. `Templater` uses **`PrivateTemplateFileSystem`** to generate files without modifying the destination.
11. **`TemplateRenderResult`** returns immutable planned files and directories.
12. `InitCommand` validates and writes the authorized files, then invokes existing Stack-specific finalization.

> **Short version:** `InitCommand` creates a context and gets a command-scoped `Templater`. The `Templater` owns the session, discovers project templates, returns one exact selection, and generates it privately. `InitCommand` decides which planned files may be written and completes project setup.

### Object journey: `func new`

1. **`NewCommand`** receives the project path, template, function name, and remaining template arguments.
2. `NewCommand` calls **`IFunctionsProjectResolver`** with the exact requested target.
3. If resolution fails, `NewCommand` returns the initialization guidance before discovery or file changes.
4. If resolution succeeds, the resolver returns a **`FunctionsProject`** with Stack and language information.
5. The owning workload evaluates item-template compatibility for that project.
6. `NewCommand` creates a **`TemplateEngineContext`** from the command and resolved project information.
7. **`ITemplaterFactory`** creates one command-scoped **`Templater`**.
8. `Templater` uses its **`TemplateEngineSession`** to discover installed item-template metadata.
9. `Templater` returns a **`TemplateResolutionResult`** containing eligible candidates, diagnostics, or one exact **`ResolvedTemplate`**.
10. The original command arguments are checked against that candidate's **`TemplateParameterDefinition`** values.
11. `Templater` invokes the exact resolved template through **`PrivateTemplateFileSystem`**.
12. **`TemplateRenderResult`** returns immutable planned function files and directories.
13. `NewCommand` validates and writes the authorized files and reports the actual result.

> **Short version:** `NewCommand` resolves the project, creates a context, and gets a command-scoped `Templater`. The `Templater` selects one exact item template tied to its session and generates it privately. `NewCommand` validates the planned files, writes the authorized changes, and reports the outcome.

## 3. `func init` flow

```mermaid
sequenceDiagram
    actor User
    participant Init as InitCommand
    participant Stack as Project and Stack services
    participant Factory as ITemplaterFactory
    participant Engine as Templater
    participant Hive as Installed template hive
    participant Render as Generate files privately
    participant Write as Write files to project

    Note over Init,Engine: PROPOSED - NOT YET IMPLEMENTED
    User->>Init: func init [path] --stack node --language JavaScript
    Init->>Stack: Parse options and inspect target
    alt Existing project adoption or repair
        Init->>Init: Preserve adoption/repair without project-template generation
    else New project scaffold
        Init->>Factory: Create command-scoped templater(context)
        Factory-->>Init: Templater
        Init->>Engine: Discover and resolve project template
        Engine->>Hive: Read installed metadata and content
        Hive-->>Engine: Project candidates
        Engine-->>Init: Exact eligible selection
        Init->>Engine: Supply supported evaluated inputs
        Engine->>Render: Invoke selected template
        Render-->>Init: Planned files, directories, diagnostics
        Init->>Write: Validate and write authorized files
        Write-->>Init: Actual changes, failures, or partial effects
        Init->>Stack: Complete Stack and CLI finalization
    end
    Init-->>User: Report actual result
```

Existing Stack initializers continue serving unmigrated Stacks. Adoption and repair remain separate from new-project generation. A replacement must validate before destructive changes, write only authorized files, and report actual effects.

## 4. `func new` flow

```mermaid
sequenceDiagram
    actor User
    participant New as NewCommand
    participant Resolver as IFunctionsProjectResolver
    participant Stack as Owning workload / Stack
    participant Engine as Templater
    participant Hive as Installed template hive
    participant Render as Generate files privately
    participant Write as Write files to project

    Note over New,Write: PROPOSED - NOT YET IMPLEMENTED
    User->>New: func new [path] --template TEMPLATE_ID --name hello
    New->>Resolver: Resolve target Functions project
    alt Project cannot be resolved
        Resolver-->>New: Not resolved
        New-->>User: Functions project not initialized. Run func init to get started.
    else Project is resolved
        Resolver-->>New: FunctionsProject with Stack and language
        New->>Stack: Determine item-template compatibility
        New->>Engine: Discover item templates for resolved context
        Engine->>Hive: Read installed metadata and content
        Hive-->>Engine: Item candidates
        Engine-->>New: Eligible candidates and reasons
        New->>New: Parse original arguments and select exact candidate
        New->>Engine: Evaluate supported parameters
        Engine->>Render: Invoke selected template
        Render-->>New: Planned files, directories, diagnostics
        New->>Write: Validate and write authorized files
        Write-->>New: Actual changes, failures, or partial effects
        New-->>User: Report actual result
    end
```

`func new` does not automatically initialize a project. The resolver supplies project facts; the owning Stack determines item-template compatibility. Candidate-specific parsing precedes final selection. Exact-ID failure never widens to a sibling. The current post-cleanup handler does not execute this proposed path.

## 5. Current implementation status

```mermaid
flowchart TD
    subgraph implemented["LOCAL CANDIDATE - INTERNAL PATH"]
        catalog["Internal template discovery / selection"]
        resolved["Session-bound exact resolution"]
        fixture["Pinned Node HTTP fixture invocation"]
        memory["Generate files privately in memory"]
        output["Immutable planned output"]
        catalog --> resolved --> fixture --> memory --> output
    end
    subgraph pending["PENDING - PRODUCT INTEGRATION / QUALIFICATION"]
        registration["Production workload registration"]
        snapshot["Installed hive to verified private snapshot"]
        wiring["Production render-session / factory wiring"]
        write["Write files to project"]
        newcmd["func new integration"]
        initcmd["Template-backed func init"]
        host["Functions Host discovery and invocation"]
        registration -.-> snapshot
        snapshot -.-> wiring
        wiring -.-> write
        write -.-> newcmd
        write -.-> initcmd
        newcmd -.-> host
        initcmd -.-> host
    end
    output -. "planned output is not written" .-> write
```

The renderer is fixture-specific, not a general installed-template API or working command journey. The regular factory does not yet create the private filesystem session. Latest Principal verdict: **PASS WITH NONBLOCKING FINDING** for the bounded fixture; write-time quotas, bounded source reads, cancellation checks, and exact-byte assertions were inspected statically. Inventory enumeration does not establish general resource isolation. Lane 3 build/test and Host execution are not established by this document. Lane 4's Windows probe is separate evidence and is not Lane 3 runtime qualification.

## Object-model review

| Type or responsibility | Status and immediate consumer | Lifetime | Why it is needed / design check |
|---|---|---|---|
| `InitCommand`, `NewCommand` | Committed CLI entry points | One command | Own parsing, orchestration, finalization, and user-facing results; template paths proposed. |
| `IFunctionsProjectResolver`, `FunctionsProject` | Committed; project resolution and future `func new` admission | One resolution / command | Reuse project recognition; recognition is not full template compatibility. |
| `TemplateEngineContext` | Committed; factory/session | One command | Captures project/Stack/language/bundle context; immutable snapshot is the intended contract. |
| `ITemplaterFactory`, `Templater` | Committed types with local edits; future command consumers | One engine session per command | Factory hides construction; Templater keeps Microsoft.TemplateEngine types internal. |
| `TemplateEngineSession` | Committed with local edits; Templater-owned | One command | Owns host, environment, and package manager; no second lifecycle owner. |
| `TemplateCatalogEntry`, `TemplateParameterDefinition` | Committed metadata | Catalog/session | Describe choices and inputs; not conditional/default evaluation or invocation authority. |
| `TemplateResolutionResult`, `ResolvedTemplate` | Local candidate; Templater and future command | Originating session | Preserve reasons and exact candidate; installed-content snapshot bridge pending. |
| `PrivateTemplateFileSystem`, `TemplateRenderResult` | Local candidate; internal renderer | Render/session | Generate fixture files privately and return immutable planned output; not general sandbox or disk writing. |
| Stack-owned compatibility | Proposed workload responsibility | Per decision | Keep project interpretation outside engine; no mandatory new class. |
| Write Files to Project | Proposed CLI responsibility | Per operation | Authorize filesystem changes and report partial outcomes; concrete type only when a real consumer justifies it. |

The historical Initial Proposal also sketches `TemplateGroup`, `TemplateSelectionCriteria`, `TemplateInvocationRequest`, and `TemplateInvocationResult`. Their names and signatures are not approved API. Use existing descriptors and ordinary method arguments first.

## Discussion points and open decisions

- Align representative HTTP content: Lane 3 fixture is query-aware; Lane 4 package has a fixed greeting and historical metadata. Neither establishes approved production content.
- Confirm initial parameter semantics and candidate-specific parsing before final variant selection.
- Assign production package ownership, workload registration into the CLI hive, and selected-content integrity policy.
- Confirm resolver admission, Stack-owned item compatibility, writing files to the project, and partial-outcome reporting.
- Preserve the current Quickstart route; defer broader Template Engine reuse and physical extraction (#5330) until after Ignite.

## Code and evidence references

Committed references point to accepted baseline `883b02ee9bc10c431a67c044a7f059a082b2fcad`. Local candidate paths are not published GitHub links.

| Responsibility | Primary source/reference |
|---|---|
| Templates status | `docs/templates/README.md` |
| Accepted design | `docs/templates/architecture.md` |
| Commands | `src/Func/Commands/InitCommand.cs`; `src/Func/Commands/NewCommand.cs` |
| Project/Stack | `IFunctionsProjectResolver`; `FunctionsProject`; `NodeWorkload`; `NodeProjectStack`; `NodeProjectFactory` |
| Initializer/Quickstart | `NodeProjectInitializer`; `QuickstartCommand` |
| Committed engine models | `src/Func/Templates/Engine/ITemplaterFactory.cs`; `TemplateEngineContext.cs`; `TemplateCatalogEntry.cs`; `TemplateParameterDefinition.cs` |
| Local renderer/tests | **LOCAL / UNCOMMITTED:** `src/Func/Templates/Engine/Templater.cs`, `TemplaterFactory.cs`, `TemplateEngineSession.cs`, `PrivateTemplateFileSystem.cs`, `TemplateResolutionResult.cs`, `TemplateRenderResult.cs`; `test/Func.Tests/Templates/Engine/TemplaterInvocationTests.cs` |
| Local implementation plan | **LOCAL / MODIFIED:** `docs/templates/implementation.md` |
| Initial Proposal | **LOCAL / HISTORICAL:** `context/source-evidence/initial-proposal.md` |
| Lane 4 content | **SEPARATE LOCAL CANDIDATE:** `Templates-NodeHttp-Qualification/azure-functions-templates/Docs/node-http-qualification.md` and `Functions.Templates/NodeHttpQualification/content/` |

The Template Engine remains inside Core Tools for this milestone. Physical extraction under issue #5330 is deferred until after Ignite.
