# src\Workloads\Stacks\PowerShell\Workloads.PowerShell.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 1
- **Dependants**: 1
- **Number of Files**: 11
- **Number of Files with Incidents**: 2
- **Lines of Code**: 396
- **Estimated LOC to modify**: 4+ (at least 1.0% of the project)

## Related Projects

**Depends on (1)** — projects this one references:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Abstractions\Abstractions.csproj](../projects/Abstractions.md)

**Depended on by (1)** — projects that reference this one:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\test\Workloads\Stacks\PowerShell.Tests\Workloads.PowerShell.Tests.csproj](../projects/Workloads.PowerShell.Tests.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph upstream["Dependants (1)"]
        P27["<b>📦&nbsp;Workloads.PowerShell.Tests.csproj</b><br/><small>net10.0</small>"]
        click P27 "../projects/Workloads.PowerShell.Tests.md"
    end
    subgraph current["Workloads.PowerShell.csproj"]
        MAIN["<b>📦&nbsp;Workloads.PowerShell.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/Workloads.PowerShell.md"
    end
    subgraph downstream["Dependencies (1)"]
        P1["<b>📦&nbsp;Abstractions.csproj</b><br/><small>net10.0</small>"]
        click P1 "../projects/Abstractions.md"
    end
    P27 --> MAIN
    MAIN --> P1

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 4 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 586 |  |
| ***Total APIs Analyzed*** | ***590*** |  |

