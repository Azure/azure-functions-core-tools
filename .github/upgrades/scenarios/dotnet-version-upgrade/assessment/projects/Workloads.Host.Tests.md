# test\Workloads\Host.Tests\Workloads.Host.Tests.csproj

[← Back to the assessment index](../../assessment.md)

## Project Info

- **Current Target Framework:** net10.0
- **Proposed Target Framework:** net11.0
- **SDK-style**: True
- **Project Kind:** ClassLibrary
- **Dependencies**: 1
- **Dependants**: 0
- **Number of Files**: 7
- **Number of Files with Incidents**: 3
- **Lines of Code**: 680
- **Estimated LOC to modify**: 9+ (at least 1.3% of the project)

## Related Projects

**Depends on (1)** — projects this one references:

- [Q:\src\copilot-worktrees\azure-functions-core-tools\fabiocav-fuzzy-robot\src\Workloads\Host\Workloads.Host.csproj](../projects/Workloads.Host.md)

## Dependency Graph

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart TB
    subgraph current["Workloads.Host.Tests.csproj"]
        MAIN["<b>📦&nbsp;Workloads.Host.Tests.csproj</b><br/><small>net10.0</small>"]
        click MAIN "../projects/Workloads.Host.Tests.md"
    end
    subgraph downstream["Dependencies (1)"]
        P4["<b>📦&nbsp;Workloads.Host.csproj</b><br/><small>net10.0</small>"]
        click P4 "../projects/Workloads.Host.md"
    end
    MAIN --> P4

```

## API Compatibility

| Category | Count | Impact |
| :--- | :---: | :--- |
| 🔴 Binary Incompatible | 0 | High - Require code changes |
| 🟡 Source Incompatible | 0 | Medium - Needs re-compilation and potential conflicting API error fixing |
| 🔵 Behavioral change | 9 | Low - Behavioral changes that may require testing at runtime |
| ✅ Compatible | 810 |  |
| ***Total APIs Analyzed*** | ***819*** |  |

## NuGet Package Issues

| Package | Current Version | Suggested Version | Severity | Issue |
| :--- | :---: | :---: | :---: | :--- |
| Microsoft.Extensions.Logging | 10.0.0 | 11.0.0 | 🟡 Potential | NuGet package upgrade is recommended |

Every project affected by these packages, and the versions the repository settles on: [aggregate NuGet packages](../nuget/aggregate-packages.md).

