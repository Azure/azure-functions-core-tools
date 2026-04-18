# Local Workload Testing Guide

This guide covers how to build, install, and test workloads locally against the CLI.

## Prerequisites

- .NET 10 SDK (see `global.json`)
- `python3` (used by the helper script for manifest editing)

## Quick Start (Script)

A helper script automates the full workflow:

```bash
# Build CLI + workload, install workload locally
./eng/scripts/test-workload-local.sh

# Build, install, and run a smoke test (func init + func new)
./eng/scripts/test-workload-local.sh --smoke

# Remove the local workload install
./eng/scripts/test-workload-local.sh --clean
```

## Manual Steps

### 1. Publish the CLI

```bash
# Replace <RID> with your platform: osx-arm64, osx-x64, linux-x64, linux-arm64
dotnet publish src/Func.Cli/Func.Cli.csproj -c release -r <RID> --no-self-contained \
    -o out/pub/Func.Cli/release_<RID>
```

### 2. Build the dotnet workload

```bash
dotnet build src/Func.Cli.Workload.Dotnet/Func.Cli.Workload.Dotnet.csproj -c release
```

The output lands in `out/bin/Func.Cli.Workload.Dotnet/release/`.

### 3. Install the workload locally

The `WorkloadManager` loads workloads from `~/.azure-functions/workloads/{id}/{version}/`
and tracks them in a `workloads.json` manifest.

```bash
# Create the install directory
INSTALL_DIR=~/.azure-functions/workloads/dotnet/0.1.0-local
mkdir -p "$INSTALL_DIR"

# Copy the built workload assemblies
cp out/bin/Func.Cli.Workload.Dotnet/release/*.dll "$INSTALL_DIR/"
cp out/bin/Func.Cli.Workload.Dotnet/release/*.deps.json "$INSTALL_DIR/" 2>/dev/null || true
```

### 4. Register the workload in the manifest

Create or update `~/.azure-functions/workloads/workloads.json`:

```json
{
  "schemaVersion": 1,
  "workloads": [
    {
      "id": "dotnet",
      "packageId": "Azure.Functions.Cli.Workload.Dotnet",
      "version": "0.1.0-local",
      "installPath": "/Users/<you>/.azure-functions/workloads/dotnet/0.1.0-local",
      "assemblyName": "Azure.Functions.Cli.Workload.Dotnet.dll",
      "installedAt": "2026-04-17T00:00:00Z"
    }
  ]
}
```

> **Note:** `installPath` must be an absolute path — replace `/Users/<you>` with your actual home directory.

### 5. Test it

```bash
FUNC=./out/pub/Func.Cli/release_<RID>/func

# Create a new dotnet isolated project
$FUNC init MyApp --worker-runtime dotnet
cd MyApp

# Add an HTTP trigger function
$FUNC new --template HttpTrigger --name MyFunc
```

### 6. Clean up

```bash
rm -rf b/dotnet/0.1.0-local
```

Then remove the `"dotnet"` entry from `~/.azure-functions/workloads/workloads.json`.

## Running Unit Tests

```bash
# All tests
dotnet test

# CLI tests only
dotnet test test/Func.Cli.Tests/

# Dotnet workload tests only
dotnet test test/Func.Cli.Workload.Dotnet.Tests/
```
