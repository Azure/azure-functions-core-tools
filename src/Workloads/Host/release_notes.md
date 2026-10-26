# Azure.Functions.Cli.Workloads.Host

## 4.1054.200-preview.3

- Updated the host payload to `Microsoft.Azure.WebJobs.Script.WebHost` 4.1054.200, matching [Azure/azure-functions-host v4.1054.200](https://github.com/Azure/azure-functions-host/releases/tag/v4.1054.200).

## 4.1048.200-preview.3

- Fixed RID-specific packages to place the host payload under `tools/<rid>` so the CLI can launch the selected implementation.

## 4.1048.200-preview.2

- Updated package layout to RID pointer format for per-OS/arch workload resolution. Compatible with CLI `5.0.0-preview.3`+; not compatible with earlier CLI releases.

## 4.1048.200-preview.1
