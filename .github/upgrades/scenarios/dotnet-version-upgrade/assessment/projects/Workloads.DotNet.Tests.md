# test\Workloads\Stacks\DotNet.Tests\Workloads.DotNet.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 2
- **Dependants**: 0
- **Number of Files**: 6
- **Number of Files with Incidents**: 2
- **Lines of Code**: 1033
- **Estimated LOC to modify**: 4+ (at least 0.4% of the project)

## Related Projects

**Depends on (2)** — projects this one references:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Abstractions\Abstractions.csproj](../projects/Abstractions.md)
- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Workloads\Stacks\DotNet\Workloads.DotNet.csproj](../projects/Workloads.DotNet.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["Workloads.DotNet.Tests.csproj"]
        MAIN["<b>📦&nbsp;Workloads.DotNet.Tests.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/Workloads.DotNet.Tests.md"
    end
    subgraph downstream["Dependencies (2)"]
        P6["<b>📦&nbsp;Workloads.DotNet.csproj</b><br/><small>net10.0</small>"]
        P1["<b>📦&nbsp;Abstractions.csproj</b><br/><small>net10.0</small>"]
        click P6 "../projects/Workloads.DotNet.md"
        click P1 "../projects/Abstractions.md"
    end
    MAIN --> P6
    MAIN --> P1

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 4 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 0 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 1402 |  |
| ***Total APIs Analyzed*** | ***1406*** |  |

## NuGet Package Issues

| Package | Current Version | Suggested Version | Severity | Issue |
| :--- | :---: | :---: | :---: | :--- |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | 11.0.0 | 🟡 Potential | NuGet package upgrade is recommended |

Every project affected by these packages, and the versions the repository settles on: [aggregate NuGet packages](../nuget/aggregate-packages.md).

