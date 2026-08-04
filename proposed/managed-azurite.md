# Design Document: Managed Azurite for `func start`

**Status:** Draft

## 1. Problem Statement

Many Azure Functions projects use local Azure Storage during development. The most common configuration is:

```text
AzureWebJobsStorage=UseDevelopmentStorage=true
```

This points the Functions host at the Azurite storage emulator, but Core Tools currently assumes the developer has already installed and started Azurite. When Azurite is missing or not running, `func start` eventually fails inside the host with storage-related errors that are not actionable for new users.

The CLI can provide a better local-development experience by detecting when the host storage account is configured for Azurite, starting Azurite when needed, and failing early with clear guidance when the CLI cannot manage the emulator.

### Impact

- **Getting-started friction:** Developers often learn they need Azurite only after `func start` fails.
- **Opaque failures:** Host storage failures do not clearly explain whether Azurite is missing, unavailable, or blocked by a port conflict.
- **Inconsistent local setup:** Some users install Azurite globally, some use Docker, and some rely on IDE-integrated Azurite. The CLI should honor existing setups before falling back to managed startup.
- **Manual coordination:** Developers must remember to start and stop Azurite separately from the Functions host.

---

## 2. Goals

1. **Detect local host storage:** During `func start`, inspect the effective `AzureWebJobsStorage` value and determine whether it references local development storage.
2. **Honor existing Azurite:** If the configured Azurite endpoints are already running, reuse them and do not start another emulator.
3. **Prefer local Azurite installs:** If Azurite must be started and an Azurite executable is available locally, use it before Docker.
4. **Docker fallback:** If no Azurite executable is available but Docker is installed and running, start a pinned Azurite container.
5. **Actionable failure:** If neither Azurite nor Docker is available, fail before launching the host with clear installation guidance.
6. **No Azure Storage SDK dependency:** Readiness checks must use `HttpClient` and storage-shaped HTTP responses rather than Azure Storage client libraries.
7. **Safe ownership:** Stop only Azurite instances that Core Tools started, and never stop a user-managed Azurite process.
8. **Predictable data and logs:** Use deterministic locations for managed Azurite data and logs.

## 3. Non-goals and Scope Boundary

### In scope

- `func start` behavior only.
- Detection based on `AzureWebJobsStorage` only.
- Azurite startup using either:
  - an existing Azurite executable, or
  - a Docker container.
- HTTP probing of Azurite blob, queue, and table endpoints.
- Port conflict detection and clear error messages.
- Cleanup of CLI-owned Azurite processes or containers.

### Out of scope

- Installing Azurite automatically via npm. Core Tools cannot assume that every project or development environment accepts an npm dependency, and `func start` should not impose one.
- Installing Docker automatically.
- Managing storage connection settings other than `AzureWebJobsStorage`.
- Rewriting arbitrary user-defined storage connection strings.
- Emulating Azure Storage with anything other than Azurite.
- Supporting Azure Storage Emulator legacy binaries.
- Guaranteeing cloud storage feature parity.
- Adding a new long-running Azurite daemon service.

> NOTE: Automatic npm installation can be considered later only if there is an explicit user opt-in and a well-defined install location. v1 should avoid modifying user projects, global npm state, or a CLI-managed npm cache because an npm dependency may be inappropriate for a given project, organization, or environment.

---

## 4. Key Concept: Host Storage Dependency

This feature is scoped to the storage account used by the Functions host itself: `AzureWebJobsStorage`.

`func start` resolves the effective value of `AzureWebJobsStorage` using the same configuration precedence that is used when launching the host. If that value references local development storage, the CLI ensures the referenced emulator is available before the host starts.

The CLI does not scan every setting in `local.settings.json` in v1. Trigger-specific connection settings can independently reference Azurite, but they are not part of the host storage dependency contract for this feature.

---

## 5. User-facing Requirements

### 5.1 Default `func start`

```
func start
```

When `AzureWebJobsStorage` references Azurite:

1. Probe the configured endpoints.
2. If Azurite is already running, reuse it.
3. If not running, look for a local Azurite executable.
4. If found, start Azurite as a child process.
5. If not found, check Docker.
6. If Docker is available, start a pinned Azurite container.
7. If Docker is unavailable, fail with installation guidance.
8. Once Azurite is ready, launch the Functions host.

When `AzureWebJobsStorage` references Azure Storage or another non-local endpoint, `func start` does not attempt to manage Azurite.

### 5.2 Opt out

```
func start --no-azurite
```

Disables all Azurite management for the current invocation.

If `AzureWebJobsStorage` references local development storage and `--no-azurite` is specified, Core Tools launches the host without probing, starting, or validating Azurite. Any storage failures are left to the host.

### 5.3 Verbose logging

```
func start --verbose
```

When Core Tools starts Azurite, normal output shows a short startup summary and the log file path. Verbose output may stream Azurite startup logs inline.

Access logs should not be interleaved with normal Functions host output by default.

### 5.4 Example output

Azurite already running:

```text
AzureWebJobsStorage references local development storage.
Using existing Azurite endpoint at http://127.0.0.1:10000.
Starting Functions host...
```

Starting local Azurite executable:

```text
AzureWebJobsStorage references local development storage.
Starting Azurite from C:\Users\me\AppData\Roaming\npm\azurite.cmd...
Azurite data: C:\Users\me\.azure-functions\azurite\scopes\7f3a2c1b\data
Azurite logs: C:\Users\me\.azure-functions\azurite\scopes\7f3a2c1b\logs\azurite.log
Starting Functions host...
```

Starting Docker fallback:

```text
AzureWebJobsStorage references local development storage.
Azurite executable was not found. Starting Azurite with Docker...
Azurite image: mcr.microsoft.com/azure-storage/azurite:3.35.0
Azurite data: C:\Users\me\.azure-functions\azurite\scopes\7f3a2c1b\data
Starting Functions host...
```

No available runtime:

```text
AzureWebJobsStorage references local development storage, but Azurite is not running.

Core Tools could not find an Azurite executable and Docker is not available.

Install one of the following and run func start again:
  - Azurite: npm install -g azurite
  - Docker Desktop: https://docs.docker.com/desktop/

Learn more about how to use Azurize with Azure Functions: https://aka.ms/azfunc-azururite
```

---

## 6. Detecting Azurite References

The CLI should parse the connection string into semicolon-delimited key/value pairs. Detection must be case-insensitive for keys and conservative for values.

### 6.1 Positive local development storage formats

#### Shortcut

```text
UseDevelopmentStorage=true
```

This is always a positive Azurite reference.

#### Shortcut with proxy

```text
UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://127.0.0.1
```

This is a positive local development storage reference, but it is user-configured. The CLI may probe it, but should not attempt to start Azurite behind an arbitrary proxy in v1.

#### Explicit default Azurite endpoints

```text
DefaultEndpointsProtocol=http;
AccountName=devstoreaccount1;
AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;
BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;
QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;
TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;
```

This is a positive Azurite reference. If the endpoints use default ports and the default `devstoreaccount1` account, the CLI can manage Azurite.

#### Explicit HTTPS Azurite endpoints

```text
DefaultEndpointsProtocol=https;
AccountName=devstoreaccount1;
AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;
BlobEndpoint=https://localhost:10000/devstoreaccount1;
QueueEndpoint=https://localhost:10001/devstoreaccount1;
TableEndpoint=https://localhost:10002/devstoreaccount1;
```

This is a positive local emulator reference, but it requires certificate configuration. The CLI should probe and reuse it if already running, but should not auto-start HTTPS Azurite in v1 unless future configuration supplies certificate paths.

#### Custom local endpoints

```text
DefaultEndpointsProtocol=http;
AccountName=account1;
AccountKey=<base64-key>;
BlobEndpoint=http://127.0.0.1:10000/account1;
QueueEndpoint=http://127.0.0.1:10001/account1;
TableEndpoint=http://127.0.0.1:10002/account1;
```

This is a local emulator reference, but it may depend on `AZURITE_ACCOUNTS` or a user-managed Azurite configuration. The CLI should probe and reuse it if already running. It should not auto-start this configuration in v1 unless it can reproduce the required account settings.

#### Production-style local endpoints

```text
DefaultEndpointsProtocol=http;
AccountName=account1;
AccountKey=<base64-key>;
BlobEndpoint=http://account1.blob.localhost:10000;
QueueEndpoint=http://account1.queue.localhost:10001;
TableEndpoint=http://account1.table.localhost:10002;
```

This is a local emulator reference. The CLI should treat `*.localhost` endpoints as local. It should probe and reuse if running, but should not auto-start production-style custom account configurations in v1.

### 6.2 Non-Azurite formats

The CLI must not classify these as Azurite references:

```text
DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=<key>;EndpointSuffix=core.windows.net
```

```text
BlobEndpoint=https://myaccount.blob.core.windows.net;SharedAccessSignature=<sas>
```

```text
DefaultEndpointsProtocol=https;
AccountName=myaccount;
AccountKey=<key>;
BlobEndpoint=https://myaccount.blob.core.windows.net;
QueueEndpoint=https://myaccount.queue.core.windows.net;
TableEndpoint=https://myaccount.table.core.windows.net;
```

### 6.3 Local host classification

Endpoint hosts are considered local when they match one of:

- `localhost`
- `127.0.0.1`
- `[::1]`
- `::1`
- any host ending in `.localhost`
- `host.docker.internal`

`host.docker.internal` should be considered local for detection and probing, but CLI-managed startup should still bind to loopback endpoints exposed to the host.

### 6.4 Manageable vs user-configured references

The CLI distinguishes between two positive classifications:

| Classification | Meaning | CLI behavior |
| --- | --- | --- |
| `ManageableAzurite` | The CLI can reproduce the configuration exactly. | Probe; if not running, start local executable or Docker. |
| `UserConfiguredAzurite` | The value points at local storage but requires user-specific configuration. | Probe; if not running, fail with guidance rather than guessing. |

Initial `ManageableAzurite` cases:

- `UseDevelopmentStorage=true` with no `DevelopmentStorageProxyUri`.
- Explicit HTTP endpoints for `devstoreaccount1` on local hosts using blob, queue, and table endpoints.

Initial `UserConfiguredAzurite` cases:

- `UseDevelopmentStorage=true` with `DevelopmentStorageProxyUri`.
- HTTPS endpoints.
- Custom account names.
- Production-style `*.localhost` account hosts.
- Custom endpoint shapes that omit one or more required service endpoints.

---

## 7. Readiness and Endpoint Probing

Azurite does not provide a dedicated `/health` endpoint. The CLI should probe storage service endpoints directly using `HttpClient`.

### 7.1 Probe requirements

- Do not use Azure Storage SDK packages.
- Use short timeouts.
- Avoid mutating storage state.
- Treat expected storage errors as proof that a storage service is listening.
- Distinguish "storage-shaped response" from "some other process is listening on this port."

### 7.2 Blob probe

For an account named `devstoreaccount1`:

```http
GET http://127.0.0.1:10000/devstoreaccount1?comp=list
x-ms-version: 2021-12-02
```

The request does not need to be authenticated. A `400` or `403` response can still prove that Azurite or a storage-compatible service is listening.

### 7.3 Queue probe

```http
GET http://127.0.0.1:10001/devstoreaccount1?comp=list
x-ms-version: 2021-12-02
```

### 7.4 Table probe

```http
GET http://127.0.0.1:10002/devstoreaccount1/Tables
x-ms-version: 2021-12-02
```

### 7.5 Storage-shaped response

A response is storage-shaped when any of the following is true:

- It includes `x-ms-request-id`.
- It includes `x-ms-error-code`.
- The response body is an Azure Storage-style error payload.
- The `Server` header or equivalent response metadata identifies Azurite.

The exact status code is not sufficient. A `200`, `400`, `403`, or `404` can all be valid signals depending on the request and emulator configuration.

### 7.6 Probe result model

| Result | Meaning | Behavior |
| --- | --- | --- |
| `Ready` | All required endpoints return storage-shaped responses. | Reuse or proceed. |
| `NotListening` | No process accepts connections on one or more required endpoints. | Start Azurite if manageable. |
| `PortConflict` | A process accepts connections but does not return storage-shaped responses. | Fail with port conflict guidance. |
| `Partial` | Some required endpoints are ready and some are missing or conflicting. | Fail unless future configuration explicitly allows partial services. |

---

## 8. Startup Resolution

### 8.1 Resolution flow

```
func start
  |
  +-- Resolve effective AzureWebJobsStorage
  |
  +-- If --no-azurite:
  |     +-- Launch host without Azurite management
  |
  +-- Classify AzureWebJobsStorage
        |
        +-- Not local storage:
        |     +-- Launch host
        |
        +-- UserConfiguredAzurite:
        |     +-- Probe configured endpoints
        |           +-- Ready: launch host
        |           +-- Not ready: fail with user-managed Azurite guidance
        |
        +-- ManageableAzurite:
              +-- Probe configured endpoints
                    +-- Ready: launch host
                    +-- PortConflict or Partial: fail with details
                    +-- NotListening:
                          +-- Find Azurite executable
                                +-- Found: start native Azurite
                                +-- Missing:
                                      +-- Check Docker
                                            +-- Available: start container
                                            +-- Missing: fail with install guidance
```

### 8.2 Local executable discovery

The CLI should look for an Azurite executable in this order:

1. Project-local npm binary:
   - Windows: `<projectRoot>\node_modules\.bin\azurite.cmd`
   - Unix: `<projectRoot>/node_modules/.bin/azurite`
2. PATH lookup:
   - Windows: `azurite.cmd`, then `azurite.exe`, then `azurite`
   - Unix: `azurite`

The CLI should not require Node.js directly when an executable is present. It should execute Azurite through the discovered command path.

### 8.3 Optional version check

The CLI may run:

```text
azurite --version
```

If the command succeeds, Core Tools may warn when the version is below the minimum supported version. A version check failure should not block startup if the executable can still be launched and the readiness probe passes.

### 8.4 Docker discovery

Docker is considered available only when both checks succeed:

```text
docker --version
docker info
```

`docker --version` alone is insufficient because Docker Desktop may be installed but not running.

---

## 9. Starting Azurite

### 9.1 Native Azurite command

For default HTTP development storage:

```text
azurite
  -l <azuriteDataPath>
  --blobHost 127.0.0.1
  --queueHost 127.0.0.1
  --tableHost 127.0.0.1
  --blobPort 10000
  --queuePort 10001
  --tablePort 10002
  --disableProductStyleUrl
  --skipApiVersionCheck
  --disableTelemetry
  --silent
  --debug <azuriteLogPath>
```

`--skipApiVersionCheck` is required to reduce local failures when the Functions host or storage libraries send a newer storage API version than the installed Azurite version recognizes.

`--disableProductStyleUrl` ensures path-style URLs such as `http://127.0.0.1:10000/devstoreaccount1` are interpreted consistently.

### 9.2 Docker image

The Docker image must use a pinned tag, not `latest`.

Initial image:

```text
mcr.microsoft.com/azure-storage/azurite:3.35.0
```

The pinned version should be owned by Core Tools and updated intentionally during CLI releases.

### 9.3 Docker command

```text
docker run --rm
  --name func-azurite-<storage-scope-id>
  -p 127.0.0.1:10000:10000
  -p 127.0.0.1:10001:10001
  -p 127.0.0.1:10002:10002
  -v <azuriteDataPath>:/data
  -v <azuriteLogDirectory>:/logs
  mcr.microsoft.com/azure-storage/azurite:3.35.0
  azurite
    -l /data
    --blobHost 0.0.0.0
    --queueHost 0.0.0.0
    --tableHost 0.0.0.0
    --blobPort 10000
    --queuePort 10001
    --tablePort 10002
    --disableProductStyleUrl
    --skipApiVersionCheck
    --disableTelemetry
    --silent
    --debug /logs/azurite.log
```

Docker binds to `0.0.0.0` inside the container so mapped host ports can reach the services. Host port publishing should bind to `127.0.0.1` when supported.

### 9.4 Startup timeout

After starting Azurite, Core Tools polls the required endpoints until:

- all required endpoints are ready,
- the process exits,
- the container exits,
- a port conflict is detected, or
- the startup timeout expires.

The initial timeout should be long enough to cover Docker image startup but short enough to fail before the Functions host begins. A default of 30 seconds is recommended.

---

## 10. Data, Logs, and Cleanup

### 10.1 Data location

Managed Azurite data is scoped to the local Functions storage scope, but it is not stored under the project root by default. Core Tools should not assume `.azurite/` is ignored by source control, and `func start` should not silently create source-control-visible emulator state.

Default managed data location:

```text
<funcHome>/azurite/scopes/<storage-scope-id>/data/
```

`<funcHome>` defaults to:

```text
~/.azure-functions
```

The storage scope ID must be stable for a given local host storage scope and must not contain raw project paths. It should be derived primarily from the effective Functions host ID and the endpoint tuple used by `AzureWebJobsStorage`. The normalized project root should be recorded as metadata and used as a fallback or disambiguator only when Core Tools cannot determine the effective host ID reliably.

The Functions host ID is part of the storage coordination scope used by the host. Locally, the host derives a default host ID from the machine name and script path when no explicit host ID is configured, which already provides isolation between projects that use the default host ID. Core Tools should share that contract or call into the same host ID resolution logic where possible; if Core Tools cannot determine the effective host ID reliably, it should be conservative and avoid sharing a managed Azurite instance across ambiguous scopes.

Reference: [`ScriptHostIdProvider`](https://github.com/Azure/azure-functions-host/blob/dev/src/WebJobs.Script/Host/ScriptHostIdProvider.cs).

Core Tools should not delete this directory automatically.

Core Tools should not use a temporary directory for managed Azurite data. Local emulator data is useful development state, and temp cleanup would create unpredictable data loss.

### 10.2 Project-local data opt in

Project-local data may be supported as an explicit opt-in later, for example:

```text
<projectRoot>/.azurite/
```

When project-local data is explicitly configured, templates and documentation should recommend ignoring `.azurite/`. The CLI should not modify an existing `.gitignore` as part of `func start`.

### 10.3 Log location

Managed Azurite debug logs are written to:

```text
<funcHome>/azurite/scopes/<storage-scope-id>/logs/azurite.log
```

Normal `func start` output should include this path when Core Tools starts Azurite.

### 10.4 Scope metadata

Core Tools should maintain a metadata file for each managed storage scope:

```text
<funcHome>/azurite/scopes/<storage-scope-id>/scope.json
```

The metadata should include:

- storage scope ID
- effective host ID, when known
- project root for display and cleanup decisions
- endpoint tuple
- data path
- log path
- created time
- last-used time
- last startup mechanism (`native` or `docker`)

Each `func start` that uses a managed scope should update `lastUsedUtc`. The metadata enables future cleanup commands without scanning repository folders or inspecting Azurite data files.

### 10.5 Cleanup policy

Core Tools stops CLI-owned Azurite processes or containers when `func start` exits and no other active `func start` invocation has leased the same managed Azurite instance.

Core Tools does not delete Azurite data or logs automatically.

### 10.6 Future cleanup command

Accumulation under `<funcHome>/azurite/scopes/` is expected over time. A future command should provide explicit cleanup rather than relying on temp-directory behavior or automatic deletion during `func start`.

Possible command names:

```text
func azurite clean
func storage clean
```

Cleanup should support:

- listing managed scopes with project path, host ID, endpoint tuple, last-used time, and data size;
- cleaning the current project's managed scope;
- cleaning scopes older than a user-provided age;
- cleaning all managed Azurite data with confirmation;
- non-interactive mode only when the target scope is explicit.

---

## 11. Ownership and Multiple `func start` Instances

The CLI must own only what it started.

### 11.1 Ownership states

| State | Description | Shutdown behavior |
| --- | --- | --- |
| User-managed | Azurite was already running before `func start`. | Never stop it. |
| CLI-owned native | Core Tools started an Azurite executable. | Stop when the last lease exits. |
| CLI-owned Docker | Core Tools started an Azurite container. | Stop the container when the last lease exits. |

### 11.2 Lease file

Core Tools should create a lease file for CLI-owned Azurite instances. The lease coordinates multiple `func start` processes for the same storage scope.

Recommended location:

```text
<funcHome>/azurite/scopes/<storage-scope-id>/func-azurite.lock
```

The lock metadata should include:

- project root
- effective host ID, when known
- storage scope ID
- endpoint tuple
- process id of the Azurite owner
- process id of each active `func start` lease holder
- startup mechanism (`native` or `docker`)
- Docker container name, when applicable
- data path
- log path

### 11.3 Same-scope sharing

If a second `func start` detects a CLI-owned Azurite instance with matching storage scope ID, effective host ID, endpoint tuple, and data path:

1. Add a lease.
2. Reuse the existing emulator.
3. Do not start a second emulator.
4. Do not stop the emulator until all leases have exited.

This is sharing, not isolation. Two Functions host instances that use the same host ID and the same storage account can coordinate through the same storage artifacts and may affect each other when run side-by-side. This is especially relevant for leases, singleton locks, timers, and other host-owned storage data. Users who need independent side-by-side runs should use distinct host IDs and distinct local storage endpoints.

If Core Tools detects an existing CLI-owned Azurite instance with the same endpoints but a different effective host ID or data path, it should not silently share that instance. Because Azurite binds ports process-wide, independent scopes require different local endpoint ports in v1.

### 11.4 Different-scope conflict

If a `func start` detects a CLI-owned Azurite instance on the required ports for a different storage scope, Core Tools should not silently share the emulator because the data directory and host coordination scope belong to a different run context.

The CLI should fail with a clear message:

```text
Azurite is already managed by another Functions storage scope on ports 10000, 10001, and 10002.

Project: C:\repo\OtherFunctionApp
Host ID: mymachine-123456789
Data:    C:\Users\me\.azure-functions\azurite\scopes\9d4e620a\data

Stop that func start session, start your own Azurite instance, or configure AzureWebJobsStorage with different local endpoints.
```

User-managed Azurite is different: if the user started Azurite outside Core Tools, Core Tools may reuse it because the user has already chosen to provide a shared emulator.

### 11.5 Stale leases

When reading a lease file, Core Tools should remove lease entries for processes that no longer exist. If the owner process no longer exists, Core Tools should treat the endpoints as normal:

- if endpoints are ready, classify as user-managed and reuse;
- if endpoints are not listening, start a new managed Azurite instance;
- if endpoints conflict, fail.

---

## 12. Port Handling

### 12.1 Defaults

The default ports are:

| Service | Port |
| --- | --- |
| Blob | `10000` |
| Queue | `10001` |
| Table | `10002` |

### 12.2 Custom ports

If `AzureWebJobsStorage` explicitly specifies local endpoints with custom ports, the CLI can classify the value as local storage. However, v1 should only auto-start when the configuration is manageable.

For custom ports to be manageable, all of the following must be true:

- blob, queue, and table endpoints are present;
- all endpoints use HTTP;
- all endpoint hosts are local;
- account name is `devstoreaccount1`;
- endpoint paths match the default account path format.

If these conditions are met, Core Tools may start Azurite using the specified ports.

### 12.3 No automatic port selection in v1

The CLI should not automatically choose alternate ports when default ports are occupied.

Automatically selecting ports requires rewriting `AzureWebJobsStorage` for the host process. That can be supported later, but v1 should prefer predictable behavior and explicit errors.

### 12.4 Ports and data scopes

Azurite uses one workspace per process. Independent storage scopes require separate Azurite workspaces, which means separate Azurite processes and separate endpoint ports when runs overlap.

Because v1 does not automatically choose alternate ports, only one CLI-owned storage scope can use the default `10000`-`10002` ports at a time. A second `func start` may share the existing Azurite process only when it matches the same storage scope. Otherwise, the CLI should fail with guidance to stop the existing run or configure explicit local endpoints on different ports.

### 12.5 Port conflict

If a required port is occupied by a process that does not return a storage-shaped response, Core Tools should fail before starting the host.

Example:

```text
AzureWebJobsStorage references local development storage, but port 10000 is already in use by a non-storage service.

Stop the process using port 10000, or configure AzureWebJobsStorage with local Azurite endpoints that use different ports.
```

When practical, include process information. Failure to identify the process should not hide the port conflict.

---

## 13. Failure Behavior

### 13.1 Azurite executable exits during startup

If the native Azurite process exits before readiness:

```text
Azurite exited before it was ready.

Log file: <azuriteLogPath>
```

Core Tools should not launch the Functions host.

### 13.2 Docker container exits during startup

If the Docker container exits before readiness, include:

- container name,
- image tag,
- log file path if the volume was mounted,
- a hint to run `docker logs <container>` when available.

### 13.3 User-configured Azurite not running

For local configurations that are not manageable:

```text
AzureWebJobsStorage points to a local storage emulator, but Core Tools cannot start this configuration automatically.

Start Azurite with matching endpoints, then run func start again.
```

Include the endpoints that were expected.

### 13.4 Docker unavailable

Docker is unavailable when the executable is missing, Docker Desktop is not running, or the daemon cannot be reached.

The error should distinguish:

- Docker executable not found.
- Docker installed but daemon unavailable.
- Docker command failed.

### 13.5 Startup timeout

If the timeout expires:

```text
Azurite did not become ready within 30 seconds.

Expected endpoints:
  Blob:  http://127.0.0.1:10000/devstoreaccount1
  Queue: http://127.0.0.1:10001/devstoreaccount1
  Table: http://127.0.0.1:10002/devstoreaccount1

Log file: <azuriteLogPath>
```

---

## 14. Security and Privacy Considerations

- Managed Azurite must bind to loopback by default.
- Docker host port publishing should bind to `127.0.0.1` when supported.
- Core Tools should not print account keys except for the known Azurite development key in examples or documentation.
- Core Tools should not upload or transmit Azurite logs.
- The managed data directory may contain user test data and should never be deleted automatically.
- The default managed data directory should live under the Core Tools user data root, not the project repository, to reduce accidental source-control commits.
- `--disableTelemetry` should be passed when Core Tools starts Azurite so the developer's `func start` does not implicitly opt into separate Azurite telemetry.

---

## 15. Configuration and Extensibility

v1 should work without new project configuration. Future extensions may add:

| Setting | Purpose |
| --- | --- |
| Azurite executable path | Start a specific Azurite binary. |
| Azurite image tag | Override the pinned Docker image for testing. |
| Azurite data path | Use a project-local, global, or custom workspace. |
| Azurite startup timeout | Increase timeout for slow Docker startup. |
| Azurite log mode | Stream, file-only, or silent. |
| HTTPS certificate paths | Allow CLI-managed HTTPS Azurite. |

Any persistent project-level configuration should be explicit and source-control friendly. Environment-variable overrides are preferable for machine-specific paths.

---

## 16. Testing Requirements

### 16.1 Unit tests

- Parses `UseDevelopmentStorage=true`.
- Parses `UseDevelopmentStorage=true;DevelopmentStorageProxyUri=...`.
- Classifies default Azurite explicit connection strings.
- Classifies HTTPS local endpoints as user-configured.
- Classifies custom local account endpoints as user-configured.
- Does not classify Azure Storage account connection strings as Azurite.
- Does not classify SAS connection strings targeting Azure as Azurite.
- Handles connection string keys case-insensitively.
- Handles extra semicolons and whitespace.
- Produces expected endpoint tuples for default and explicit local formats.

### 16.2 Probe tests

Use a local HTTP test server, not Azurite, for most tests:

- `x-ms-request-id` response is classified as storage-shaped.
- `x-ms-error-code` response is classified as storage-shaped.
- Non-storage response on the same port is classified as port conflict.
- Connection refused is classified as not listening.
- Partial endpoint readiness is classified as partial.

### 16.3 Process management tests

- Local Azurite executable discovery prefers project-local npm binary over PATH.
- Docker is used only when native Azurite is not found.
- Docker is not used when `docker info` fails.
- CLI-owned native process is stopped when the last lease exits.
- User-managed endpoint is not stopped.
- Same-scope concurrent `func start` invocations share a CLI-owned emulator.
- Same-project but different-host-ID scopes are not silently shared.
- Different-scope CLI-owned emulator conflict is reported.
- Managed scope metadata is created and `lastUsedUtc` is updated when a scope is used.
- Stale leases are cleaned up.

### 16.4 Integration tests

- `func start` with `UseDevelopmentStorage=true` starts native Azurite when available.
- `func start` with `UseDevelopmentStorage=true` starts Docker Azurite when native Azurite is unavailable and Docker is available.
- `func start --no-azurite` does not probe or start Azurite.
- Port conflict produces a pre-host-launch error.
- The Functions host starts successfully after managed Azurite is ready.

---

## 17. Open Questions

1. Should `--no-azurite` be the only public switch, or should v1 also include an explicit `--azurite` mode selector?
2. Should Core Tools support a machine-level Azurite executable override in v1?
3. Should Core Tools expose first-class flags for side-by-side isolated local storage scopes, including explicit host ID and local endpoint ports?
4. Should custom HTTP ports be auto-startable in v1 when all endpoint details are explicit?
5. Should the CLI surface Azurite process output in `--verbose`, or keep all Azurite output file-only?
6. Should the future cleanup command be Azurite-specific (`func azurite clean`) or part of a broader local storage command group (`func storage clean`)?

---

## 18. Success Criteria

- `func start` with `AzureWebJobsStorage=UseDevelopmentStorage=true` starts successfully when Azurite is already running.
- `func start` starts a project-local or PATH Azurite executable when no emulator is running.
- `func start` starts Docker Azurite when no executable is available and Docker is running.
- `func start` fails before host launch with actionable guidance when neither Azurite nor Docker is available.
- Core Tools does not take a dependency on Azure Storage SDK packages for readiness checks.
- Core Tools never stops a user-managed Azurite instance.
- CLI-owned Azurite data and logs are written under the Core Tools user data root by default, scoped primarily by effective host ID and endpoint tuple.
- Port conflicts are detected before the Functions host starts.
