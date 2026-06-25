# Azure Functions CLI – PowerShell worker workload (dev notes)

Repo-only notes for contributors. Not packaged into the nupkg.

## Version scheme

The workload pkg version maps 1:1 to the PowerShell worker payload it carries.
One prop in `Directory.Version.props` drives `$(VersionPrefix)`:

| Prop                   | Meaning                                                                            |
|------------------------|------------------------------------------------------------------------------------|
| `$(WorkerVersionPS74)` | `Microsoft.Azure.Functions.PowerShellWorker.PS7.4` version.                        |
| `$(WorkerVersionPS76)` | `Microsoft.Azure.Functions.PowerShellWorker.PS7.6` version.                        |
| `$(WorkerVersion)`     | Workload package version. Set to the highest of the per-PS-version pins.           |

The per-version packages may not always share the same version number, so each
is pinned independently. Bump both whenever the upstream worker packages are
bumped. `VersionOverride` ensures each reference stays pinned.

Unlike the bundles workload, there is no channel axis: the upstream
PowerShellWorker NuGets don't ship preview/experimental SKUs.

## Pack details

Worker assets are pulled at pack time from the restored per-version NuGet
packages and merged into a single `tools/any/` layout:

```
tools/any/
├── worker.config.json        (shared, from either package)
├── 7.4/                      (from PS7.4 package)
│   └── Microsoft.Azure.Functions.PowerShellWorker.dll + deps
└── 7.6/                      (from PS7.6 package)
    └── Microsoft.Azure.Functions.PowerShellWorker.dll + deps
```

The host resolves the correct subdirectory at runtime via
`%FUNCTIONS_WORKER_RUNTIME_VERSION%`.
