# test\Workloads\Sdk.Tests\Workloads.Sdk.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 1
- **Dependants**: 0
- **Number of Files**: 6
- **Number of Files with Incidents**: 2
- **Lines of Code**: 653
- **Estimated LOC to modify**: 9+ (at least 1.4% of the project)

## Related Projects

**Depends on (1)** — projects this one references:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Workloads\Sdk\Workloads.Sdk.csproj](../projects/Workloads.Sdk.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["Workloads.Sdk.Tests.csproj"]
        MAIN["<b>📦&nbsp;Workloads.Sdk.Tests.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/Workloads.Sdk.Tests.md"
    end
    subgraph downstream["Dependencies (1)"]
        P5["<b>📦&nbsp;Workloads.Sdk.csproj</b><br/><small>netstandard2.0</small>"]
        click P5 "../projects/Workloads.Sdk.md"
    end
    MAIN --> P5

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 9 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 692 |  |
| ***Total APIs Analyzed*** | ***701*** |  |

