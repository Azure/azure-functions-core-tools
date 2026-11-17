# test\Workloads\Stacks\Python.Tests\Workloads.Python.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 2
- **Dependants**: 0
- **Number of Files**: 5
- **Number of Files with Incidents**: 2
- **Lines of Code**: 833
- **Estimated LOC to modify**: 1+ (at least 0.1% of the project)

## Related Projects

**Depends on (2)** — projects this one references:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Abstractions\Abstractions.csproj](../projects/Abstractions.md)
- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Workloads\Stacks\Python\Workloads.Python.csproj](../projects/Workloads.Python.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["Workloads.Python.Tests.csproj"]
        MAIN["<b>📦&nbsp;Workloads.Python.Tests.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/Workloads.Python.Tests.md"
    end
    subgraph downstream["Dependencies (2)"]
        P11["<b>📦&nbsp;Workloads.Python.csproj</b><br/><small>net10.0</small>"]
        P1["<b>📦&nbsp;Abstractions.csproj</b><br/><small>net10.0</small>"]
        click P11 "../projects/Workloads.Python.md"
        click P1 "../projects/Abstractions.md"
    end
    MAIN --> P11
    MAIN --> P1

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 1 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 1445 |  |
| ***Total APIs Analyzed*** | ***1446*** |  |

## NuGet Package Issues

| Package | Current Version | Suggested Version | Severity | Issue |
| :--- | :---: | :---: | :---: | :--- |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | 11.0.0 | 🟡 Potential | NuGet package upgrade is recommended |

Every project affected by these packages, and the versions the repository settles on: [aggregate NuGet packages](../nuget/aggregate-packages.md).

