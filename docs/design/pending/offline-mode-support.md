# Design Document: Full Offline Mode Support for Azure Functions Core Tools

**Issue**: [#3821 - Internet access is required for running azure functions on local machine](https://github.com/Azure/azure-functions-core-tools/issues/3821)

**Authors**: Core Tools Team  
**Date**: January 2026  
**Status**: Draft - Pending Team Review

---

## Executive Summary

Azure Functions Core Tools currently requires internet connectivity to download extension bundles from `cdn.functions.azure.com` during `func start`, causing local development to fail in offline or air-gapped environments. This design proposes making the tool fully functional offline by implementing cache-first fallbacks for all network operations while maintaining optimal online performance.

---

## Problem Statement

### Current Behavior

When developers run `func start` without internet access, the tool fails with:

```log
Error building configuration in an external startup class.
System.Net.Http: No such host is known. (cdn.functions.azure.com:443).
System.Net.Sockets: No such host is known.
```

### Impact

1. **Development Blocked**: Developers cannot work locally during network outages or in secure/air-gapped environments
2. **CDN Dependency**: Recent CDN outages (Oct 2025) blocked all local development globally
3. **Enterprise Limitations**: Organizations with restricted network access cannot use Core Tools
4. **Logic Apps Impact**: Logic App designer cannot open offline, blocking workflow development

### User Scenarios

1. Developer working on airplane/train without internet
2. Secure enterprise environment with restricted internet access
3. CDN outage blocking development worldwide
4. Developer using local Azure Storage emulator for isolated testing

---

## Goals and Requirements

### Primary Goals

1. **Offline Functionality**: `func start` must work without internet when extension bundles are cached
2. **Automatic Fallback**: Network failures should gracefully fall back to cached resources
3. **No Breaking Changes**: Existing online behavior and workflows must remain unchanged
4. **Local Emulator Support**: Full support for Azure Storage emulator (Azurite) in offline mode

### Requirements

#### Functional Requirements

- **FR-1**: CLI must start successfully when CDN is unreachable if extension bundles are cached
- **FR-2**: Extension bundle downloads must check cache before attempting network calls
- **FR-3**: Cache age warnings must appear when bundles are older than 30 days
- **FR-4**: Template operations (`func new`, `func init`) must work offline with local templates
- **FR-5**: Optional `--offline` CLI flag must skip all network attempts for faster startup
- **FR-6**: Custom cache location must be configurable via `FUNCTIONS_BUNDLE_CACHE_PATH` environment variable

#### Non-Functional Requirements

- **NFR-1**: Offline startup time should not exceed online startup time significantly
- **NFR-2**: Error messages must clearly indicate offline-related issues with actionable guidance
- **NFR-3**: Verbose logging must indicate when cached vs fresh resources are used
- **NFR-4**: Telemetry failures must never block operation

---

## Design Overview

### Architecture Principles

1. **Cache-First Strategy**: Check local cache before attempting network calls
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
func start → GetExtensionBundle() → Check Cache
↓
Cache Exists? → Yes → Use Cache + Check Age
↓
No → Attempt Download
↓
Network Success? → Yes → Cache + Use
↓
No → Error (no cache available)
```

**Proposed Flow With --offline Flag**:

```
func start --offline → GetExtensionBundle() → Check Cache Only
↓
Cache Exists? → Yes → Use Cache + Check Age
↓
No → Error immediately
```

**Proposed Changes**:

- Implement cache-first logic that checks for cached bundles before attempting network downloads. When bundles are cached, use them immediately and optionally attempt background updates.
- Add cache age warnings (30+ days) to inform users when their cache is stale.
- Support custom cache locations via `FUNCTIONS_BUNDLE_CACHE_PATH` environment variable. - Skip all CDN calls (version checks, deprecation warnings) when `--offline` flag is set.

---

### 2. Global Offline Flag

**Problem**: Need explicit offline mode to skip network attempts entirely.

**Proposed Changes**:

- Add global `--offline` CLI flag available to all commands.
- Create `GlobalSettings` class to track offline state throughout the application.
- When offline mode is active, all network-dependent operations check this state and skip network calls, using cached/fallback values instead.

---

### 3. Version Check Optimization

**Problem**: GitHub version check can timeout and delay startup unnecessarily.

**Proposed Changes**:

- Skip version check entirely when `--offline` flag is set.
- Reduce HTTP timeout to 2 seconds (from default 100s) for faster failure on slow networks. Keep existing graceful exception handling.

---

### 4. Template Management

**Problem**: Template operations fail offline even when local templates are available.

**Proposed Changes**:

- Add fallback to local bundled templates when extension bundle templates are unavailable.
- In offline mode, skip extension bundle entirely and use local templates immediately.
- Ensure all common templates (HTTP, Timer, Queue, Blob) are bundled locally.

---

### 5. Bundle Management Commands

**Problem**: No way to pre-cache bundles or view cached bundles.

**Proposed Changes**:

- Add `func bundle download` command to explicitly download and cache bundles from host.json configuration.
- Add `func bundle list` command to display cached bundles with age information. Both commands help users prepare for and troubleshoot offline scenarios.

---

### 6. Telemetry Verification

**Problem**: Ensure telemetry doesn't block offline operation.

**Proposed Changes**:

- No code changes required - verify existing `PersistenceChannel` handles offline gracefully.
- Document that telemetry can be disabled via `FUNCTIONS_CORE_TOOLS_TELEMETRY_OPTOUT=1` for air-gapped environments.

---

## User Experience

### Success Scenarios

#### Scenario 1: Working Offline with Cache

First time online - cache bundles

```bash
$ func start
Azure Functions Core Tools
Downloading extension bundles...
...
Functions loaded successfully

# Later, offline
$ func start
Azure Functions Core Tools
Using cached extension bundle
Warning: Extension bundle cache is 45 days old. Run 'func bundle download' to update.
...
Functions loaded successfully
```

#### Scenario 2: Explicit Offline Mode

```bash
$ func start --offline
Azure Functions Core Tools
Offline mode: Using cached extension bundle
...
Functions loaded successfully
```

#### Scenario 3: Pre-caching for Offline

```bash
$ func bundle download
Downloading extension bundles...
Extension bundles cached to: ~/.azure-functions-core-tools/Functions/ExtensionBundles/Microsoft.Azure.Functions.ExtensionBundle
Ready for offline use

$ func bundle list
Cached extension bundles:
  Microsoft.Azure.Functions.ExtensionBundle v4.15.0
    Last Updated: 2026-01-06 10:30:00 (0 days ago)
```

### Error Scenarios

#### Error 1: Offline without Cache

```bash
$ func start --offline
Error: Offline mode enabled but extension bundle not cached.
Connect to internet and run 'func start' to cache bundles, or remove extensionBundle from host.json.
```

#### Error 2: Network Failure without Cache

```bash
$ func start
Error: Unable to download extension bundle and no cached version available.
Check your internet connection or run 'func start --offline' with pre-cached bundles.
```

## Testing Strategy

### Test Cases

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

### Phase 1: Core Functionality (Sprint 1)
- Implement cache-first bundle download
- Add cache age warnings
- Add fallback for CDN version checks

### Phase 2: Offline Flag (Sprint 2)
- Add global `--offline` flag
- Implement version check suppression
- Add template fallback logic

### Phase 3: Bundle Management (Sprint 3)
- Implement `func bundle download` command
- Implement `func bundle list` command
- Add custom cache location support

### Phase 4: Documentation and Testing (Sprint 4)
- Update CLI help text
- Add offline mode documentation
- Comprehensive E2E testing
- Update error messages

### Breaking Changes

**None** - All changes are additive or improve existing error handling.

---

## Open Questions for Team Discussion

### Design Decisions Requiring Discussion

1. **Bundle Download Scope**
   - **Question**: Should `func bundle download` download:
     - Option A: Only the version specified in current host.json (simpler, focused)
     - Option B: All available bundle versions (comprehensive, larger cache)
     - Option C: Support both with `--all` flag
   - **Recommendation**: Option A - host.json version only
   - **Reasoning**: Most users work on specific projects, full download unnecessary

2. **Cache Validation Depth**
   - **Question**: Should cache validation:
     - Option A: Just check directory exists (fast, current approach)
     - Option B: Verify bundle integrity (checksum/file count) (safer, slower)
   - **Recommendation**: Option A - directory check only
   - **Reasoning**: Bundle corruption rare, startup speed matters
   - **Future**: Consider Option B as opt-in for enterprise scenarios

3. **Warning Frequency**
   - **Question**: Should 30-day cache age warning:
     - Option A: Show on every `func start` (current plan)
     - Option B: Show once per day/session only
     - Option C: Configurable frequency
   - **Recommendation**: Option A - every start
   - **Reasoning**: Important for awareness, not too noisy (only when 30+ days)

4. **Timeout Configuration**
   - **Question**: Should HTTP timeouts be:
     - Option A: Hardcoded (2s offline, 10s online, 60s downloads)
     - Option B: Configurable via environment variables
   - **Recommendation**: Option A - hardcoded
   - **Reasoning**: Most users don't need customization, complexity not worth it

5. **Multi-Runtime Support**
   - **Question**: Some apps have multiple language workers. Should:
     - Option A: Cache age check only extension bundles (current plan)
     - Option B: Check all runtime dependencies (complex)
   - **Recommendation**: Option A - extension bundles only
   - **Reasoning**: Other runtime downloads handled by Functions Host SDK

### Clarifications Needed

1. **Host SDK Behavior**
   - Does `ExtensionBundleManager.GetExtensionBundlePath()` from Microsoft.Azure.WebJobs.Script already implement cache-first logic?
   - If yes, do we just need to handle errors better?
   - **Action**: Test with network disconnected to verify current behavior

2. **Template Storage**
   - Where are local templates currently stored in the Core Tools package?
   - Are they complete enough for offline development?
   - **Action**: Audit local template completeness

3. **Worker Downloads**
   - Are language worker binaries (Python, PowerShell, Node) downloaded by Core Tools or Functions Host?
   - Do we need offline support for worker downloads?
   - **Action**: Verify worker download responsibilities

---

## Work Items (GitHub Issues)

### High Priority

1. **[Feature] Implement cache-first extension bundle download**
   - Modify `ExtensionBundleHelper.GetExtensionBundle()` to check cache before download
   - Add cache age warning (30 days)
   - Update error messages for offline scenarios
   - **Files**: ExtensionBundleHelper.cs, StartHostAction.cs

2. **[Feature] Add global --offline flag**
   - Add `--offline` option to BaseAction
   - Create GlobalSettings for offline mode tracking
   - **Files**: BaseAction.cs, Constants.cs, GlobalSettings.cs (new)

3. **[Feature] Skip version check in offline mode**
   - Suppress GitHub version check when offline flag set
   - Reduce timeout to 2 seconds for faster failure
   - **Files**: VersionHelper.cs, Program.cs

4. **[Feature] Add template fallback for offline scenarios**
   - Implement cache-first template loading
   - Fall back to local templates on network failure
   - Skip bundle download in offline mode
   - **Files**: TemplatesManager.cs, InitAction.cs, NewAction.cs

### Medium Priority

5. **[Feature] Implement func bundle download command**
   - Create BundleActions/DownloadBundleAction.cs
   - Pre-cache bundles based on host.json
   - Show progress and success messages
   - **Files**: DownloadBundleAction.cs (new), Context.cs

6. **[Feature] Implement func bundle list command**
   - Create BundleActions/ListBundleAction.cs
   - Display cached bundles with age information
   - **Files**: ListBundleAction.cs (new)

7. **[Feature] Support custom cache location**
   - Add `FUNCTIONS_BUNDLE_CACHE_PATH` environment variable
   - Update `GetBundleDownloadPath()` to check custom location
   - **Files**: ExtensionBundleHelper.cs, Constants.cs

### Low Priority

8. **[Docs] Update documentation for offline mode**
   - Add offline mode guide to docs/
   - Update CLI help text
   - Add troubleshooting section

9. **[Test] Add E2E tests for offline scenarios**
   - Test offline with cache
   - Test offline without cache
   - Test custom cache location
   - Test bundle commands

---

## Risks and Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Bundle corruption in cache | Users stuck offline | Low | Add `func bundle download --force` to re-download |
| Breaking changes in bundle manager | Offline mode fails | Medium | Comprehensive testing with Functions Host SDK |
| Cache size growth | Disk space issues | Low | Document cache cleanup, consider max age cleanup |

---

## Appendix

### Related Issues

- [#3821 - Internet access required for running azure functions](https://github.com/Azure/azure-functions-core-tools/issues/3821)
- [#4696 - CDN endpoint outage](https://github.com/Azure/azure-functions-core-tools/issues/4696)
- [#4697 - Feature Request for Offline/Cached Mode](https://github.com/Azure/azure-functions-core-tools/issues/4697)
- [#3778 - func host start stuck](https://github.com/Azure/azure-functions-core-tools/issues/3778)
- [Azure/azure-functions-host#9434 - Extension bundles](https://github.com/Azure/azure-functions-host/issues/9434)
