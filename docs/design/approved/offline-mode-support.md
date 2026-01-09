# Design Document: Full Offline Mode Support for Azure Functions Core Tools

**Issue**: [#3821 - Internet access is required for running azure functions on local machine](https://github.com/Azure/azure-functions-core-tools/issues/3821)

**Authors**: Core Tools Team  
**Date**: January 2026  
**Status**: Approved

---

## Executive Summary

Azure Functions Core Tools currently requires internet connectivity to download extension bundles from `cdn.functions.azure.com` during `func start`
and `func new`, causing local development to fail in offline or air-gapped environments. This design proposes making the tool fully functional offline
by implementing cache-first fallbacks for all network operations while maintaining optimal online performance.

---

## Problem Statement

### Current Behavior

When developers run `func start` without internet access, the tool fails with:

```log
Error building configuration in an external startup class.
System.Net.Http: No such host is known. (cdn.functions.azure.com:443).
System.Net.Sockets: No such host is known.
```

This is also an issue with `func new` as templates also require a bundles download.

```log
> func new
'local.settings.json' found in root directory (/Users/likasem/source/ado_projects/pythonapp).
Resolving worker runtime to 'python'.
nodename nor servname provided, or not known (cdn.functions.azure.com:443)
```

### Impact

1. **Development Blocked**: Developers cannot work locally during network outages or in secure/air-gapped environments
2. **CDN Dependency**: Recent CDN outages (Oct 2025) blocked all local development globally
3. **Enterprise Limitations**: Organizations with restricted network access cannot use Core Tools
4. **Logic Apps Impact**: Logic App designer cannot open offline, blocking workflow development

---

## Goals and Requirements

### Primary Goals

1. **Offline Functionality**:
   - `func start` must work without internet when extension bundles are cached
   - `func new` must work without internet
3. **Automatic Fallback**: Network failures should gracefully fall back to cached resources
4. **No Breaking Changes**: Existing online behavior and workflows must remain unchanged

### Requirements

#### Functional Requirements

- **FR-1**: Extension bundle downloads must fallback to cache if unable to download
- **FR-2**: Extension bundle downloads should always download the latest when there is network availability
- **FR-3**: The CLI should log warnings when bundles are used from cache
- **FR-4**: Template operations (`func new`, `func init`) must work offline with local templates
- **FR-5**: Optional `--offline` CLI flag must skip all network attempts for faster startup
- **FR-6**: Users must have a way to pre-install bundles 

#### Non-Functional Requirements

- **NFR-1**: Offline startup time should not exceed online startup time significantly
- **NFR-2**: Error messages must clearly indicate offline-related issues with actionable guidance
- **NFR-3**: Logging must indicate when cached vs fresh resources are used

---

## Design Overview

### Architecture Principles

1. **Cache Bundles**: Resources required for `func start` or `func new` must be cached
2. **Graceful Degradation**: Fall back to cached/default values on network failures
3. **Explicit Optimization**: `--offline` flag bypasses network attempts entirely
4. **Zero Config Default**: Works offline automatically without configuration when cache exists

### Network Dependencies Identified

| Component | Location | Criticality | Current Behavior | Proposed Behavior |
|-----------|----------|-------------|------------------|-------------------|
| Extension Bundles | ExtensionBundleHelper.cs:70-91 | **HIGH** - Blocks startup | Retries with timeout | Check cache first |
| Version Check | VersionHelper.cs:11 | LOW - Background | Exception caught | Skip in offline mode |
| Telemetry | Telemetry.cs:45 | LOW - Background | Persistence channel | No change needed |
| Templates | TemplatesManager.cs:54 | MEDIUM - Blocks `func new` | Downloads from bundle | Fall back to local |
| CDN Version Range | ExtensionBundleHelper.cs:145 | LOW - Has fallback | Returns default on error | Skip in offline mode |

---

## Detailed Design

### 1. Extension Bundles (High Priority)

**Problem**: Extension bundle download attempts block startup when CDN is unreachable.

**Current Flow**:

```
func start → GetExtensionBundle() → Network Download → Retry 3x → Silent Fail → Host Retries
```

**Proposed Flow**:

```
func start → GetExtensionBundle() → Attempt Download
↓
Network Success? → Yes → Use
↓
No → Check bundles download path
↓
Bundles Exists? → Yes →  Use + warn that cached bundles are used
↓
No → Error (unable to download bundles and no cache available)
```

**Proposed Flow With --offline Flag**:

```
func start --offline → GetExtensionBundle() → Check bundles download path first
↓
Bundles Exists? → Yes → Use + warn that cached bundles are used
↓
No → Error (unable to download bundles and no cache available)
```
**Other changes**:

1. We should disable the `EnsureLatest` bundles setting from the host. We should not need this if we
do the bundles management on the core tools side.
2. We should not download bundles for stacks that do not require it (e.g. dotnet apps)

---

### 2. Template Management

**Problem**: Template operations fail offline even when local templates are available.

**Proposed Changes**:

- Add fallback to local bundled templates when extension bundle templates are unavailable.
- In offline mode, skip extension bundle entirely and use local templates immediately.
- Ensure all common templates (HTTP, Timer, Queue, Blob) are bundled locally.

---

### 3. Bundle Management Commands

**Problem**: No way to pre-cache bundles or view cached bundles.

**Proposed Changes**:

- Add `func bundle download` command to explicitly download and cache bundles from host.json configuration.
- Add `func bundle list` command to display cached bundles with age information. Both commands help users prepare for and troubleshoot offline scenarios.

---

### 4. Version Check Optimization

**Problem**: GitHub version check can timeout and delay startup unnecessarily.

**Proposed Changes**:

- Skip version check entirely when `--offline` flag is set.
- Reduce HTTP timeout to 2 seconds (from default 100s) for faster failure on slow networks.
- Keep existing graceful exception handling.

---

### 5. Global Offline Flag

**Problem**: Have a explicit offline mode to skip network attempts entirely.

**Proposed Changes**:

- Add global `--offline` CLI flag available to all commands.
- When offline mode is active, all network-dependent operations check this state and skip network calls, using cached/fallback values instead.

---

## User Experience

> We can use the same messaging for `func new` scenarios.

### Success Scenarios

#### Scenario 1: Working Offline with Cache

First time online - download bundles

```bash
$ func start
Info: Downloading extension bundles <version>


# Later, offline
$ func start
Warn: Using cached extension bundle <version>. When you have network connectivity, you can run 'func bundle download' to update.
```

#### Scenario 2: Explicit Offline Mode

```bash
$ func start --offline
Offline mode: Using cached extension bundle <version>
```

#### Scenario 3: Pre-caching for Offline

```bash
$ func bundle download
Downloading extension bundles...
Extension bundles downloaded to: ~/.azure-functions-core-tools/Functions/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle

$ func bundle list
Available extension bundles:
  Microsoft.Azure.Functions.ExtensionBundle v4.15.0
```

### Error Scenarios

#### Error 1: Network Failure without Cache

```bash
$ func start
Error: Unable to download extension bundle and no cached version available. Bundles must be pre-cached before you can run offline.
When you have network connectivity, you can use `func bundles download` to download bundles and pre-cache them for offline use.
```

#### Error 2: Offline without Cache

```bash
$ func start --offline
Error: Extension bundles must be pre-cached before you can run offline.
When you have network connectivity, you can use `func bundles download` to download bundles and pre-cache them for offline use.
```

## Testing Cases

1. **Offline with Cache**: Pre-cache bundles, disconnect, verify `func start` succeeds
2. **Offline without Cache**: Clear cache, disconnect, verify clear error message
3. **Offline Flag**: Verify `--offline` skips network attempts entirely
4. **CDN Failure Fallback**: Block CDN via hosts file, verify fallback to cache
5. **Cache Age Warning**: Modify cache timestamp, verify warning displays
6. **Custom Cache Location**: Set env var, verify bundles cached to custom path
7. **Template Operations**: Verify `func new` and `func init` work offline
8. **Bundle Commands**: Test `func bundle download` and `func bundle list`

---

## Migration and Rollout

### 1: Bundle Management

- Implement `func bundle download` command
- Implement `func bundle list` command
- Implement `func bundle path` command (this already exists, just need to redirect to this new command path)

### 2: Fallback to bundles download path if network fails
- Add logic to check the bundle download location for a valid bundles installation if unable to download latest.
- Add error logs when bundles are not available (unable to download, and not in cache)
- Add warning logs when cached bundles are in use
- Add clear instructions of how to install bundles
- These changes should fix the issue for both `func start and func new`

### 3: Validate and shortcircuit other network calls
- Update `func publish` to shortcircut if offline/no network available
- Validate `func pack` works offline

### 4: Offline Flag (Needs design + review)
- Add global `--offline` flag
- Do we want to provide a way for customers to prepare for offline usage easily?

---

## Appendix

### Related Issues

- [#3821 - Internet access required for running azure functions](https://github.com/Azure/azure-functions-core-tools/issues/3821)
- [#4696 - CDN endpoint outage](https://github.com/Azure/azure-functions-core-tools/issues/4696)
- [#4697 - Feature Request for Offline/Cached Mode](https://github.com/Azure/azure-functions-core-tools/issues/4697)
- [#3778 - func host start stuck](https://github.com/Azure/azure-functions-core-tools/issues/3778)
- [Azure/azure-functions-host#9434 - Extension bundles](https://github.com/Azure/azure-functions-host/issues/9434)
