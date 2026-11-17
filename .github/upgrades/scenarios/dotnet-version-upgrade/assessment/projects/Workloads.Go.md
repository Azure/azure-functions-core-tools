# src\Workloads\Stacks\Go\Workloads.Go.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 1
- **Dependants**: 1
- **Number of Files**: 12
- **Number of Files with Incidents**: 1
- **Lines of Code**: 580
- **Estimated LOC to modify**: 0+ (at least 0.0% of the project)

## Related Projects

**Depends on (1)** — projects this one references:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Abstractions\Abstractions.csproj](../projects/Abstractions.md)

**Depended on by (1)** — projects that reference this one:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\test\Workloads\Stacks\Go.Tests\Workloads.Go.Tests.csproj](../projects/Workloads.Go.Tests.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph upstream["Dependants (1)"]
        P24["<b>📦&nbsp;Workloads.Go.Tests.csproj</b><br/><small>net10.0</small>"]
        click P24 "../projects/Workloads.Go.Tests.md"
    end
    subgraph current["Workloads.Go.csproj"]
        MAIN["<b>📦&nbsp;Workloads.Go.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/Workloads.Go.md"
    end
    subgraph downstream["Dependencies (1)"]
        P1["<b>📦&nbsp;Abstractions.csproj</b><br/><small>net10.0</small>"]
        click P1 "../projects/Abstractions.md"
    end
    P24 --> MAIN
    MAIN --> P1

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 904 |  |
| ***Total APIs Analyzed*** | ***904*** |  |

