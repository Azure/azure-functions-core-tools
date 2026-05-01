# Design Document: Profile-Based Local Development for Azure Functions CLI

**Status:** Draft

## 1. Problem Statement

Azure Functions supports multiple deployment SKUs — Flex Consumption, Linux Premium, Windows Consumption, Windows Dedicated, and Linux Consumption. Each SKU runs its own version of the Functions runtime, and our desire is to support different deployment cadences.

Today, Azure Functions Core Tools bundles a **single host version**. To prevent "works locally, breaks in cloud" failures when the developer's code depends on host behaviors, APIs, or extension bundle features not yet deployed to their target SKU, Core Tools follows a strict deployment sequence to ensure it never gets ahead of the host versions deployed to the cloud.

### Impact

- **Silent correctness failures**: Code passes local testing but fails in production on an older host version.
- **Extension bundle drift**: Locally-resolved bundle versions may exceed what the target environment supports, causing runtime errors after deployment.
- **Side-by-side comparison impossible**: Developers cannot run two environment configurations simultaneously to validate migration or compare behavior.

---

## 2. Key Concept: Profiles

Rather than coupling directly to SKU names, this design introduces **profiles** as the primary abstraction. A profile is a **constraint set** that defines the local development environment: host version bounds, extension bundle bounds, and feature toggles.

Built-in profiles are named after SKUs (e.g., `flex`, `windows-consumption`), but the SKU is metadata _within_ the profile, not its identity. This decoupling enables:

- **Custom profiles**: Teams can define profiles that pin host versions, use preview channels, or enforce organizational constraints — without waiting for a registry update.
- **Dynamic resolution**: Instead of a static SKU → exact-version lookup, the CLI resolves the best available host version within a profile's version range from installed workloads.
- **Cross-flow reuse**: The same profile informs `func start` (which host to launch), `func pack` (validate compatibility), and potentially `func deploy` (warn on mismatches).
- **Composability**: Custom profiles can extend built-in profiles, overriding only what differs.

---

## 3. Goals

1. **Environment alignment**: `func start --profile <name>` launches a host version matching the constraints of the target environment, so local behavior matches cloud behavior at the host-version level.
2. **Side-by-side execution**: Developers can run two different profiles simultaneously on different ports for comparison testing.
3. **Extension bundle constraining**: The extension bundle version resolved locally is constrained by the intersection of the project's `host.json` range and the profile's bundle range.
4. **Automatic resolution**: When a project declares its target profile(s), `func start` uses the correct host version without manual intervention.
5. **Custom profiles**: Developers and teams can define custom profiles that extend built-in profiles to pin versions, test preview channels, or enforce organizational standards.
6. **Integrate with Workloads**: Profile resolution builds on the Workloads model (§4.6 of the Workloads Spec) where the host runtime is a separately-versioned workload.
7. **Offline-capable**: Profile resolution degrades gracefully when network is unavailable, using cached or bundled profile data.

## 4. Non-Goals and Scope Boundary

This design targets **profile-level host version alignment**, not full cloud parity.

### In scope
- Matching the host runtime version within a profile's constraints
- Constraining extension bundle versions to what the profile supports
- Side-by-side host version execution
- Built-in profile registry distribution and caching
- Custom profile definition at project and user levels
- Feature toggles (boolean constraints on host behavior)
- Cross-flow profile usage (start, pack)

### Out of scope
- **Region/stamp/ring-level parity**: Cloud rollouts are staged (UD 0 → UD N). Built-in profiles model a SKU as a single version target, not per-region state.
- **OS-level behavior differences**: A developer on macOS using the `windows-consumption` profile will get the correct host version but not Windows-specific OS behaviors.
- **SKU-specific scale/networking/billing**: Consumption cold-start behavior, VNET integration, instance limits, etc. are not emulated.

> NOTE: Planned future iterations of CLI will support running on target environment containers, more closely aligning with the cloud environments.

---

## 5. Profile Schema

### 5.1 Built-in Profile Registry

The registry uses a `$schema` URI for self-description and versioning, following the pattern established by Azure Resource Manager templates:

```json
{
  "$schema": "https://aka.ms/func-profiles/v1/schema.json",
  "generatedAt": "2026-04-20T00:00:00Z",
  "profiles": {
    "flex": {
      "sku": "flex-consumption",
      "status": "GA",
      "host": {
        "version": "[4.1048.0, 4.1049.0)"
      },
      "extensionBundle": {
        "version": "[4.0.0, 4.99.1)"
      },
      "features": {
        "proxies": false
      },
      "supportedRuntimes": ["node", "python", "java", "powershell", "dotnet-isolated", "custom"],
      "notes": "Newest host — Flex gets bits first"
    }
  }
}
```

The `$schema` URI encodes the version (`v1`). When the schema evolves in a breaking way, a new URI is published (`v2`). The CLI checks the URI against its supported set — an unrecognized schema triggers: `"Profile registry requires a newer CLI. Run: func upgrade"`

Unknown fields within a supported schema version are ignored (forward-compatible additive changes do not require a new schema version).

### 5.2 Custom Profile (User or Project Defined)

Custom profiles also use `$schema` for editor support and validation:

```json
{
  "$schema": "https://aka.ms/func-custom-profiles/v1/schema.json",
  "staging": {
    "extends": "flex",
    "host": {
      "version": "[4.1046.100]"
    }
  },
  "locked-prod": {
    "extends": "windows-consumption",
    "extensionBundle": {
      "version": "[4.0.0, 4.22.1)"
    }
  }
}
```

### 5.3 Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sku` | string | no | Azure SKU identifier. Metadata only — not used for resolution. |
| `status` | string | no | `GA`, `preview`, `deprecated`. CLI warns on deprecated profiles. |
| `deprecationUrl` | string | no | URL to deprecation documentation with migration guidance. Used when `status` is `deprecated`. |
| `extends` | string | no | Name of a parent profile to inherit from. Single inheritance only. |
| `host.version` | version range | yes (or inherited) | NuGet-style version range for the host runtime. |
| `extensionBundle.version` | version range | no | Version range constraining extension bundles (applied on top of `host.json`). |
| `features` | object | no | Boolean feature toggles. See §7. |
| `supportedRuntimes` | string[] | no | Worker runtimes this profile supports. Enables early validation. |
| `notes` | string | no | Human-readable description. |

### 5.4 Version Range Expressions

All version constraints use NuGet-style version range syntax:

| Intent | Expression |
|--------|-----------|
| Latest within SKU bounds | `[4.1045.0, 4.1049.0)` |
| Pinned to exact version | `[4.1046.100]` |
| Any 4.x | `[4.0.0, 5.0.0)` |
| Preview channel | `[4.1048.100-preview, 4.1049.0)` |

A single field replaces what would otherwise be separate `maxVersion`, `pinnedVersion`, and `channel` properties. The resolution rule is: **highest installed (or available) workload version satisfying the range**.

---

## 6. Profile Sources and Layering

Profiles are resolved from four sources, in precedence order:

```
1. CLI flag           --profile <name>              (highest priority)
2. Project config     .func/config.json             (team-shared)
3. User profiles      ~/.azure-functions/profiles.json
4. Built-in profiles  Registry (remote → cache → bundled fallback)
```

### 6.1 Built-in Profiles (Registry)

Built-in profiles are distributed via the **profile registry** — a lightweight JSON artifact published to a well-known CDN endpoint, separate from host workload packages.

**Distribution:**

```
1. Remote fetch    → https://aka.ms/func-profiles (canonical URL)
                     https://aka.ms/func-profiles-sha256 (detached checksum)
2. Local cache     → ~/.azure-functions/profiles/registry.json (1-hour TTL)
3. Bundled fallback → Shipped with the CLI package (updated each CLI release)
```

**Publishing:**

The registry is maintained in the same CDN-backed storage that serves extension bundles today. Updates are made by the existing SKU deployment pipelines:

1. SKU deployment pipeline deploys a new host version
2. Same pipeline updates `registry.json` in CDN-backed storage
3. CDN serves the updated registry at the well-known URL
4. **Publish ordering is inherent**: the pipeline that publishes the host binary is the same pipeline that updates the registry, so the host package is always available before the registry references it.

**Integrity:**

A detached checksum file (`registry.json.sha256`) is published alongside the registry. The CLI fetches both, computes the SHA-256 of the downloaded registry, and compares. Mismatches cause the fetch to be discarded and the cache/bundled fallback to be used instead.

**Staleness:**

The registry includes a `generatedAt` timestamp. If the cached registry is older than 7 days and a fresh fetch fails, the CLI warns: `"Profiles are N days old. Host versions may not match current cloud deployments."`

### 6.2 User-Level Profiles

Defined in `~/.azure-functions/profiles.json`. These are personal profiles for experiments, channel testing, or developer-specific overrides.

```json
{
  "my-preview": {
    "extends": "flex",
    "host": {
      "version": "[4.1050.0-preview, 4.1051.0)"
    }
  }
}
```

### 6.3 Project-Level Profiles

Defined in `.func/profiles.json` and committed to source control. These are team-shared custom profiles.

```json
{
  "staging": {
    "extends": "flex",
    "host": {
      "version": "[4.1046.100]"
    }
  },
  "locked-prod": {
    "extends": "windows-consumption",
    "extensionBundle": {
      "version": "[4.0.0, 4.22.1)"
    }
  }
}
```

### 6.4 Project Configuration

The project declares which profiles it targets in `.func/config.json`:

```json
{
  "$schema": "https://aka.ms/func-config/v1/schema.json",
  "profiles": ["flex", "staging"],
  "defaultProfile": "flex"
}
```

This file is committed to source control. CLI behavior based on this file:

| Scenario | Behavior |
|----------|----------|
| Single profile declared, no `--profile` flag | Auto-apply it silently |
| Multiple profiles declared, no `--profile` flag | **Interactive**: present a selector. **Non-interactive/CI**: use `defaultProfile`, or error if not set |
| `--profile` flag matches a declared profile | Use it |
| `--profile` flag names an undeclared profile | Allow it, but warn: `"Profile 'X' is not declared in this project's config"` |
| No `.func/config.json` exists | Fall back to `--profile` flag or latest installed (backward-compat) |

### 6.5 Name Resolution

When looking up a profile by name, the CLI searches in order:

```
1. Project-level (.func/profiles.json)
2. User-level (~/.azure-functions/profiles.json)
3. Built-in (registry)
```

First match wins. A project-level profile can shadow a built-in profile name — this is intentional, allowing a project to override the built-in `flex` with a pinned version.

---

## 7. Features

Features are boolean flags that the CLI uses to **configure the host process at launch time**, disabling capabilities that are unavailable in the target environment. This ensures local behavior matches the cloud — unsupported features simply don't work locally, rather than generating warnings that developers may ignore.

### 7.1 Semantics

- **Absent** → feature is enabled (default, no constraint)
- **`false`** → feature is actively disabled in the host process
- **`true`** → feature is explicitly enabled (useful in custom profiles to re-enable something a parent disabled)

The CLI enforces features by setting the appropriate environment variables or host configuration when launching the host process.

### 7.2 Schema

```json
"features": {
  "proxies": false,
  "inProcess": false
}
```

All values are booleans. Feature names are simple identifiers (not `proxiesEnabled` — just `proxies`).

### 7.3 Example

A developer targeting Flex defines proxies in their app. The `flex` profile sets `"proxies": false`, so the CLI disables proxies in the host. Proxies don't work locally — the developer discovers the incompatibility before deploying, not after.

### 7.4 Built-in Feature Catalog

The initial set covers features where SKU behavior diverges and the host can be configured to match:

| Feature | What it disables | Profiles that disable it |
|---------|-----------------|--------------------------|
| `proxies` | Azure Functions Proxies | `flex` |

The feature catalog is owned by the CLI team, since features map to host launch configuration that the CLI controls. New features are added to the registry as SKU-level differences are identified.

> **Note on in-process .NET:** In-process support is not modeled as a profile feature. The v5 CLI does not support in-process .NET at all — this is a CLI-level constraint enforced before host launch, independent of profiles. The CLI detects in-process projects from project files and exits with a clear error.

### 7.5 Feature Inheritance

Features use **deep merge** during inheritance (unlike other sections which use replace). When extending a profile:

- Present keys override the parent value
- `null` removes an inherited feature (restores to default: enabled)
- New keys are added

```json
// Parent (flex)
"features": { "proxies": false }

// Child
"features": { "proxies": null, "customHandler": false }

// Resolved
"features": { "customHandler": false }
// proxies removed — restored to default (enabled)
// customHandler disabled by child
```

---

## 8. Profile Inheritance

### 8.1 Merge Rules

| Section | Merge behavior |
|---------|---------------|
| `host` | **Replace** — if present in child, replaces parent's `host` entirely |
| `extensionBundle` | **Replace** — if present in child, replaces parent's `extensionBundle` entirely |
| `features` | **Deep merge** — child values overlay parent. `null` removes a key |
| `sku`, `name`, `status`, `notes` | **Replace** — child value wins |
| `supportedRuntimes` | **Replace** — if present in child, replaces parent's list |

### 8.2 Chain Rules

- **Single inheritance only**: `extends` names exactly one parent profile. No multiple inheritance.
- **Max chain depth**: 5 levels. The CLI validates and errors if exceeded: `"Profile inheritance chain exceeds maximum depth of 5."`
- **Cycle detection**: If a profile name appears twice in the resolution chain, the CLI errors: `"Circular profile inheritance detected: A → B → A."`
- **Cross-source inheritance**: A project-level profile can extend a user-level or built-in profile. A user-level profile can extend a built-in profile. Built-in profiles do not extend other profiles.

### 8.3 Example Chain

```
"staging" (project) → extends "flex" (built-in)

Resolved "staging":
  name:             "staging"          (own)
  sku:              "flex-consumption" (inherited from flex)
  host.version:     "[4.1046.100]"     (own — replaces flex's range)
  extensionBundle:  "[4.0.0, 4.99.1)" (inherited from flex)
  features:         { proxies: false }  (inherited from flex)
```

---

## 9. Resolution: Host Version

### 9.1 Resolution Flow

```
func start --profile flex
    │
    ├─ Resolve profile (§6.5 name resolution)
    ├─ Resolve inheritance chain (§8)
    ├─ Extract host.version range from resolved profile
    │
    ├─ Find installed host workloads matching the version range
    │   ├─ Match found → use highest matching version
    │   └─ No match ─┐
    │                 ├─ Interactive: prompt to install best available from feed
    │                 ├─ Non-interactive/CI: error with install command
    │                 └─ Offline: error if not cached
    │
    └─ Launch host
```

### 9.2 Precedence with `--host-version`

The `--host-version` flag (from Workloads spec §4.6) bypasses profile resolution entirely:

```bash
func start --host-version 4.1045.200
```

The CLI emits a diagnostic note:

```
Note: Using explicit host version 4.1045.200. Profile constraints
(extension bundle, features, runtime compatibility) are not applied.
```

### 9.3 Conflict: `--profile` + `--host-version`

If both are specified, the CLI **errors**:

```
Error: --profile and --host-version are mutually exclusive.
  Use --profile to apply environment constraints automatically.
  Use --host-version to pin to a specific version regardless of profile.
```

### 9.4 Default Behavior (No Profile Specified)

When no profile is specified via flag or project config:

- The CLI uses the latest installed host-runtime workload with no constraints. This matches today's Core Tools behavior.
- A warning is emitted suggesting the developer configure a target profile:
  ```
  Warning: No profile configured. Using latest installed host (4.1048.100).
  To match a specific Azure environment, configure a target profile with:
    func profile set flex
  ```
- The warning is suppressed once `.func/config.json` exists — the project has made an explicit choice.

---

## 10. Resolution: Extension Bundles

### 10.1 Intersection Model

Extension bundles have two version dimensions:

1. **`host.json` range** — defined by the developer, shipped with the app. This is part of the deployment payload.
2. **Profile bundle range** — defines what the target environment supports. This is a local-development constraint and is _not_ deployed.

The CLI resolves the **intersection** of these two ranges:

```
host.json says:            [4.19.*, 5.0.0)
Profile says:              [4.0.0, 4.25.1)
Intersection:              [4.19.0, 4.25.1)
→ Resolved:                4.25.0 (highest available in intersection)
```

### 10.2 Empty Intersection

If the intersection is empty, the CLI errors with an actionable message:

```
Error: No extension bundle version satisfies both:
  host.json range:     [4.30.*, 5.0.0)
  Profile constraint:  [4.0.0, 4.25.1)  (windows-consumption)

The app requires bundle versions newer than this profile supports.
Update host.json or target a different profile.
```

### 10.3 Cross-Flow Reuse

`func pack` performs the same intersection check at packaging time. If the app's `host.json` bundle range is incompatible with the project's declared profile, `func pack` warns before the developer deploys.

### 10.4 No Profile Active

When no profile is active (backward-compat mode), no bundle constraint is applied — `host.json` range is used as-is, matching today's behavior.

---

## 11. Side-by-Side Execution

### 11.1 Usage

```bash
# Terminal 1
func start --profile flex --port 7071

# Terminal 2
func start --profile windows-consumption --port 7072
```

Each invocation independently resolves its profile, ensures the appropriate host workload is installed, and launches a separate host process.

### 11.2 Requirements

- Each host process has its own port and environment.
- The CLI detects port conflicts before spawning the host and suggests an available port.
- The startup banner includes the profile name and resolved versions for disambiguation.

### 11.3 Startup Banner

```
Azure Functions CLI
Profile:           windows-consumption (built-in)
Host Version:      4.1045.200
Extension Bundle:  v4.25.0 (constrained to [4.0.0, 4.25.1))
Registry:          remote (updated 2026-04-20)

Functions:
    HttpTrigger: [GET,POST] http://localhost:7072/api/HttpTrigger
```

---

## 12. Profile Listing

### 12.1 Commands

```bash
func profile list                    # List all available profiles (built-in + user + project)
func profile list --built-in         # Built-in profiles only
func profile list --json             # Machine-readable output
func profile show flex               # Detailed view of a specific profile (resolved)
func profile show staging --raw      # Show without inheritance resolution
```

### 12.2 Output

```
Available profiles:

  Name                    Source     Host Version Range       Bundle Range          Status
  ─────────────────────── ───────── ──────────────────────── ───────────────────── ──────────
  flex                    built-in  [4.1048.0, 4.1049.0)     [4.0.0, 4.99.1)       GA
  linux-premium           built-in  [4.1046.0, 4.1047.0)     [4.0.0, 4.30.1)       GA
  windows-consumption     built-in  [4.1045.0, 4.1046.0)     [4.0.0, 4.25.1)       GA
  windows-dedicated       built-in  [4.1045.0, 4.1046.0)     [4.0.0, 4.25.1)       GA
  linux-consumption       built-in  [4.1044.0, 4.1045.0)     [4.0.0, 4.22.1)       deprecated
  staging                 project   [4.1046.100]             (inherited: flex)      —
  my-preview              user      [4.1050.0-pre, 4.1051.0) (inherited: flex)      —

  Project targets: flex, staging (default: flex)

Registry: remote | Cache age: 2 hours
```

---

## 13. Integration with Workloads

### 13.1 Host-Runtime Workload Resolution

The resolved profile's `host.version` range maps to host-runtime workload versions:

```
Profile "flex" → host.version "[4.1048.0, 4.1049.0)"
    ↓
Scan installed workloads: Azure.Functions.Cli.Workload.Host
    ↓
Highest installed version in range → 4.1048.100
```

### 13.2 Auto-Install Behavior

When no installed workload satisfies the profile's version range:

- **Interactive**: `"No installed host matches profile 'flex' [4.1048.0, 4.1049.0). Install latest? [Y/n]"`
- **Non-interactive/CI**: Error with install command: `"Run: func workload install host --version 4.1048.100"`
- **Offline**: Error: `"No installed host matches profile 'flex'. Pre-install with: func workload install host --version 4.1048.100"`

### 13.3 Precedence with Workloads Spec

The Workloads spec (§4.6) resolves the host version via: `--host-version` → project pin → latest installed. Profiles insert into this chain:

```
--host-version flag  →  --profile flag  →  project config profile  →  latest installed
```

When a profile is active, "latest installed" is replaced by "highest installed within profile range." This prevents a newer host installed for one project from silently becoming the default for a profile-constrained project.

---

## 14. Offline and Cache Behavior

### 14.1 Fetch Strategy

```
func start --profile flex
    │
    ├─ Fetch remote profile registry
    │   ├─ Success → validate checksum, persist to cache
    │   └─ Failure ─┐
    │               ├─ Cache exists and < 7 days old → use with warning
    │               ├─ Cache exists and ≥ 7 days old → use with stronger warning
    │               └─ No cache → use bundled fallback with warning
    │
    └─ Resolve host version from profile
        ├─ Host workload installed → launch
        └─ Host workload not installed → auto-install or error (§13.2)
```

### 14.2 `--offline` Flag

```bash
func start --profile flex --offline
```

Skips all network attempts. Uses cached registry (or bundled fallback) and installed host workloads only. Errors if no installed workload satisfies the profile.

### 14.3 Cache and File Layout

```
~/.azure-functions/
  profiles/
    registry.json              # Cached built-in profile registry
    registry.json.sha256       # Detached checksum
    registry.json.meta         # Fetch timestamp, ETag, source URL
  profiles.json                # User-level custom profiles
  workloads/
    host/
      4.1048.100/              # Installed host workload
      4.1045.200/              # Another version (side-by-side)

<project>/
  .func/
    config.json                # Project config (profiles, defaultProfile)
    profiles.json              # Project-level custom profiles
  host.json
  local.settings.json
```

---

## 15. Diagnostics and User Experience

### 15.1 Startup Diagnostics

Every `func start` with a profile prints:

```
Profile:           flex (built-in, from project config)
Host Version:      4.1048.100
Extension Bundle:  v4.25.0 (constrained to [4.0.0, 4.99.1))
Registry:          remote (2026-04-20)
```

### 15.2 `--verbose` Diagnostics

```
[profile] Resolution:
  --profile flag:    (not set)
  Project config:    flex (default of [flex, staging])
  Resolved source:   built-in registry
[profile] Inheritance: flex (no parent)
[profile] Host range: [4.1048.0, 4.1049.0) → resolved 4.1048.100 (installed)
[profile] Bundle: host.json [4.22.*, 5.0.0) ∩ profile [4.0.0, 4.99.1) → [4.22.0, 4.99.1) → resolved 4.25.0
[profile] Features: proxies=false
```

### 15.3 Deprecation Warnings

```
Warning: Profile "linux-consumption" is deprecated (retirement: 2028-09-30).
See: https://aka.ms/func-linux-consumption-deprecation
```

---

## 16. Failure Modes and Recovery

| Scenario | Behavior |
|----------|----------|
| Unknown profile name | Error listing available profiles. Suggest `func profile list`. |
| Profile fetch fails, no cache | Use bundled fallback. Warn that versions may be stale. |
| No installed host satisfies profile range | Auto-install prompt (interactive) or error with install command (CI). |
| Host workload download fails midway | Atomic install: incomplete downloads are cleaned up. No partial state. |
| Concurrent `func start` for same profile | Cross-process locking on the workload install directory (per Workloads spec §4.1). |
| Bundle range intersection is empty | Error explaining the conflict between `host.json` and profile constraint. |
| `--profile` + `--host-version` both specified | Error: mutually exclusive flags. |
| Deprecated profile | Warn on every startup; do not block. |
| Corrupt cached registry (checksum mismatch) | Discard cache, re-fetch. If re-fetch also fails, use bundled fallback. |
| Project targets unsupported runtime | Error: `"Profile 'X' does not support runtime 'Y'. Supported: [...]"` |
| Inheritance chain exceeds depth 5 | Error: `"Profile inheritance chain exceeds maximum depth of 5."` |
| Circular inheritance | Error: `"Circular profile inheritance detected: A → B → A."` |
| `--profile` names undeclared profile | Warn but allow: `"Profile 'X' is not declared in this project's config."` |
| Profile extends unknown parent | Error: `"Profile 'X' extends 'Y', which was not found."` |

---

## 17. Open Questions

1. **~~Who publishes the built-in profile registry?~~**
   - **Resolved**: The registry lives in the existing CDN-backed storage (same infrastructure as extension bundles). Updates are made by the existing SKU deployment pipelines as part of the host deployment flow.

2. **~~Should the default behavior (no profile specified) change?~~**
   - **Resolved**: No default profile. When no profile is specified (no `--profile` flag, no `.func/config.json`), the CLI uses the latest installed host with no constraints (backward-compatible) and emits a one-time warning suggesting the developer configure a target profile using the appropriate CLI command. The warning is suppressed once a `.func/config.json` exists (the project has made an explicit choice). This avoids locking in a default SKU that becomes a breaking change to update later.

3. **~~One host workload ID or one per major version?~~**
   - **Resolved**: One workload ID (`host`) with semver versions. The profile's version range constrains which versions are valid. Major version boundaries are handled naturally by version range expressions (e.g., `[4.0.0, 5.0.0)`).

4. **~~Pre-caching for CI/offline:~~**
   - **Resolved**: `func profile install <name>` resolves the profile and installs all required dependencies (host workload + extension bundle). `func profile install` (no argument) installs dependencies for all profiles declared in `.func/config.json`. This is the recommended CI step for ensuring offline readiness.

5. **~~Registry schema versioning~~** *(resolved)*
   - All JSON documents in this design use `$schema` URIs for self-description and versioning, following the pattern established by Azure Resource Manager templates (e.g., `"$schema": "https://aka.ms/func-profiles/v1/schema.json"`).
   - The version is encoded in the URI path (`v1`, `v2`). Breaking changes produce a new URI.
   - Unknown fields within a supported schema version are ignored (additive changes do not require a new schema version).
   - Unrecognized `$schema` URI → error: `"Profile registry requires a newer CLI. Run: func upgrade"`
   - Applies to: registry (`func-profiles`), custom profiles (`func-custom-profiles`), and project config (`func-config`).

6. **~~Feature catalog~~** *(resolved)*
   - Features are **host configuration directives**, not validation checks. The CLI disables features in the host process at launch time, so local behavior matches the target environment. Absent features default to enabled; `false` actively disables.
   - v1 set: `proxies`, `inProcess`. New features added by the CLI team as SKU differences are identified.
   - No warn/block mode — the host simply runs with the target environment's capabilities.

7. **~~Project config file name and location~~** *(resolved)*
   - `.func/` directory in the project root. Contains `config.json` (project configuration) and `profiles.json` (custom profile definitions). Groups all CLI config together, avoids overloading `host.json` (which is deployed) or `func.json` (which has legacy v1 meaning). Follows the pattern of `.cargo/`, `.nuget/`.

---

## Appendix: Comparison with SKU-Centric Design

This design introduces features and improvements compared to the SKU-centric approach. The following table captures the key differences and improvements.

| Aspect | Original (SKU-Centric) | Current (Profile-Based) | Improvement |
|--------|----------------------|------------------------|-------------|
| **Primary abstraction** | SKU name is the identity | Profile is a constraint set; SKU is metadata | Decouples from Azure infrastructure naming; enables custom environments |
| **Version targeting** | Static lookup: SKU → exact host version | Version range expression (NuGet-style) | Supports pinning, floating, minimum version and preview channels with one field |
| **Custom environments** | Not supported — only registry-defined SKUs | First-class custom profiles at user and project level | Teams can enforce organizational standards without registry changes |
| **Inheritance** | None | Single inheritance with `extends`, max depth 5 | Custom profiles derive from built-in profiles, overriding only what differs |
| **Features** | Not modeled | Boolean host configuration directives | CLI configures the host to match target environment capabilities |
| **Bundle constraining** | `maxVersion` cap applied on top of `host.json` | Intersection of two version ranges (`host.json` ∩ profile) | Cleaner semantics; both constraints are expressed the same way |
| **Project configuration** | `local.settings.json` `TargetSku` field (gitignored) | `.func/config.json` with declared profiles + default (git-tracked) | Team shares target config via source control; supports multi-target |
| **Multi-target** | One SKU at a time | Project declares multiple profiles | `func pack` validates against all declared targets; `func profile install` pre-caches all in one step; team intent is captured in source control |
| **Profile sources** | Single: built-in registry | Three layers: built-in → user-level → project-level | Flexible layering for different use cases |
| **Schema versioning** | Integer `schemaVersion` field | `$schema` URI with version in path | Self-describing documents; editor IntelliSense; ARM template alignment |
| **Integrity** | Inline `checksum` field (circular) | Detached `.sha256` file | Avoids validating a hash that's inside the payload it protects |
| **Deprecation guidance** | Hardcoded alternative profiles in CLI output | `deprecationUrl` links to online documentation | Docs own migration guidance; CLI stays simple |
| **Default behavior** | Implicit default to a specific SKU | No default; warn and suggest `func profile set` | Avoids locking in a default that becomes a breaking change |
| **Pre-caching** | Not addressed | `func profile install [<name>]` | CI-friendly; installs host + bundle for all declared profiles |
| **Cross-flow reuse** | `func start` only | Profiles inform `func start`, `func pack`, and potentially `func deploy` | Catch incompatibilities at packaging time, before deployment |
| **CLI commands** | `func sku list`, `--sku` flag | `func profile list/show/install/set`, `--profile` flag | Richer management surface; profile is a first-class concept |
| **In-process .NET** | Modeled as a profile feature | CLI-level pre-launch constraint, independent of profiles | Cleaner separation: v5 CLI doesn't support in-process at all |
