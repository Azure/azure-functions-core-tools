# Azure.Functions.Cli.Workloads.Workers.Python

## 4.46.2-preview

- Updated the Python worker payload to 4.46.2, adding support for Python 3.15.
- Added Windows ARM64 (`win-arm64`) support with a Python 3.15-only payload. Other platforms retain all Python versions supplied by the upstream worker.

## 4.46.0-preview.3

- Updated the Python worker payload to 4.46.0, matching host release 4.1054.200.

## 4.43.0-preview.3

- Updated package layout to RID pointer format for per-OS/arch workload resolution. Compatible with CLI `5.0.0-preview.3`+; not compatible with earlier CLI releases.

## 4.43.0-preview.2

- fix: address per-RID content files missing in nupkg (#5285)
