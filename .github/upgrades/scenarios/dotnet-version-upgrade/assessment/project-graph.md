# Projects Relationship Graph

[← Back to the assessment index](../assessment.md)

Legend:
📦 SDK-style project
⚙️ Classic project

```mermaid
flowchart LR
    P1["<b>📦&nbsp;Abstractions.csproj</b><br/><small>net10.0</small>"]
    P2["<b>📦&nbsp;Func.csproj</b><br/><small>net10.0</small>"]
    P3["<b>📦&nbsp;Workloads.ExtensionBundles.csproj</b><br/><small>net10.0</small>"]
    P4["<b>📦&nbsp;Workloads.Host.csproj</b><br/><small>net10.0</small>"]
    P5["<b>📦&nbsp;Workloads.Sdk.csproj</b><br/><small>netstandard2.0</small>"]
    P6["<b>📦&nbsp;Workloads.DotNet.csproj</b><br/><small>net10.0</small>"]
    P7["<b>📦&nbsp;Workloads.Go.csproj</b><br/><small>net10.0</small>"]
    P8["<b>📦&nbsp;Workloads.Java.csproj</b><br/><small>net10.0</small>"]
    P9["<b>📦&nbsp;Workloads.Node.csproj</b><br/><small>net10.0</small>"]
    P10["<b>📦&nbsp;Workloads.PowerShell.csproj</b><br/><small>net10.0</small>"]
    P11["<b>📦&nbsp;Workloads.Python.csproj</b><br/><small>net10.0</small>"]
    P12["<b>📦&nbsp;Workloads.Workers.Go.csproj</b><br/><small>net10.0</small>"]
    P13["<b>📦&nbsp;Workloads.Workers.Java.csproj</b><br/><small>net10.0</small>"]
    P14["<b>📦&nbsp;Workloads.Workers.Node.csproj</b><br/><small>net10.0</small>"]
    P15["<b>📦&nbsp;Workloads.Workers.PowerShell.csproj</b><br/><small>net10.0</small>"]
    P16["<b>📦&nbsp;Workloads.Workers.Python.csproj</b><br/><small>net10.0</small>"]
    P17["<b>📦&nbsp;Workloads.Tests.Fixtures.Default.csproj</b><br/><small>net10.0</small>"]
    P18["<b>📦&nbsp;Workloads.Tests.Fixtures.Sdk.csproj</b><br/><small>net10.0</small>"]
    P19["<b>📦&nbsp;Workloads.Tests.Fixtures.WithCommand.csproj</b><br/><small>net10.0</small>"]
    P20["<b>📦&nbsp;Func.Tests.csproj</b><br/><small>net10.0</small>"]
    P21["<b>📦&nbsp;Workloads.Host.Tests.csproj</b><br/><small>net10.0</small>"]
    P22["<b>📦&nbsp;Workloads.Sdk.Tests.csproj</b><br/><small>net10.0</small>"]
    P23["<b>📦&nbsp;Workloads.DotNet.Tests.csproj</b><br/><small>net10.0</small>"]
    P24["<b>📦&nbsp;Workloads.Go.Tests.csproj</b><br/><small>net10.0</small>"]
    P25["<b>📦&nbsp;Workloads.Java.Tests.csproj</b><br/><small>net10.0</small>"]
    P26["<b>📦&nbsp;Workloads.Node.Tests.csproj</b><br/><small>net10.0</small>"]
    P27["<b>📦&nbsp;Workloads.PowerShell.Tests.csproj</b><br/><small>net10.0</small>"]
    P28["<b>📦&nbsp;Workloads.Python.Tests.csproj</b><br/><small>net10.0</small>"]
    P2 --> P1
    P6 --> P1
    P7 --> P1
    P8 --> P1
    P9 --> P1
    P10 --> P1
    P11 --> P1
    P17 --> P1
    P18 --> P1
    P19 --> P1
    P20 --> P2
    P21 --> P4
    P22 --> P5
    P23 --> P6
    P23 --> P1
    P24 --> P7
    P24 --> P1
    P25 --> P8
    P25 --> P1
    P26 --> P9
    P26 --> P1
    P27 --> P10
    P27 --> P1
    P28 --> P11
    P28 --> P1
    click P1 "projects/Abstractions.md"
    click P2 "projects/Func.md"
    click P3 "projects/Workloads.ExtensionBundles.md"
    click P4 "projects/Workloads.Host.md"
    click P5 "projects/Workloads.Sdk.md"
    click P6 "projects/Workloads.DotNet.md"
    click P7 "projects/Workloads.Go.md"
    click P8 "projects/Workloads.Java.md"
    click P9 "projects/Workloads.Node.md"
    click P10 "projects/Workloads.PowerShell.md"
    click P11 "projects/Workloads.Python.md"
    click P12 "projects/Workloads.Workers.Go.md"
    click P13 "projects/Workloads.Workers.Java.md"
    click P14 "projects/Workloads.Workers.Node.md"
    click P15 "projects/Workloads.Workers.PowerShell.md"
    click P16 "projects/Workloads.Workers.Python.md"
    click P17 "projects/Workloads.Tests.Fixtures.Default.md"
    click P18 "projects/Workloads.Tests.Fixtures.Sdk.md"
    click P19 "projects/Workloads.Tests.Fixtures.WithCommand.md"
    click P20 "projects/Func.Tests.md"
    click P21 "projects/Workloads.Host.Tests.md"
    click P22 "projects/Workloads.Sdk.Tests.md"
    click P23 "projects/Workloads.DotNet.Tests.md"
    click P24 "projects/Workloads.Go.Tests.md"
    click P25 "projects/Workloads.Java.Tests.md"
    click P26 "projects/Workloads.Node.Tests.md"
    click P27 "projects/Workloads.PowerShell.Tests.md"
    click P28 "projects/Workloads.Python.Tests.md"

```

