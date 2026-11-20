# Telemetry Shutdown Status

This document records the current scope and remaining limitations of the Azure Functions CLI telemetry shutdown changes on `telemetry-fix`. The current change addresses only the **network-drain portion of shutdown**. Most other findings remain valid.

## Current Status

| Finding / proposal | Current branch status |
| --- | --- |
| Providers initialize before every command | **Unchanged.** `CreateHostAsync` and `host.StartAsync` happen before argument parsing and command invocation. Simple commands still pay trace/metric provider startup costs. |
| Host synchronously disposes trace and metric providers | **Still true.** `host.Dispose()` remains synchronous and provider shutdown is not explicitly owned or parallelized. |
| Shutdown waits for network ingestion | **Fixed for writable storage.** Exporter 1.10 receives a zero shutdown drain budget and persists pending data locally rather than awaiting HTTP. |
| Serialization/compression/persistence cost | **Still present.** The process must serialize and persist queued telemetry before disposal completes. Only the ingestion round trip was removed. |
| Version and alias notices appear after disposal | **Unchanged.** They are still printed after `host.Dispose()`. |
| ReadyToRun disabled | **Unchanged.** There is no `PublishReadyToRun` configuration in the repository. |
| Unwritable storage may fall back to network | **Still untested.** The new regression test uses a writable temporary directory only. |
| Keep metrics | **Done.** Both tracing and metrics remain enabled. |

## Current Lifecycle

```text
start host and telemetry providers
→ parse and execute command
→ stop command stopwatch
→ record metrics and complete activity
→ synchronously dispose host
    → serialize/compress/persist telemetry locally
→ print version/alias notices
→ exit
```

One important timing distinction is that the telemetry command-duration stopwatch stops **before** `host.Dispose()`. Therefore:

- Provider **startup** is included in the recorded command duration.
- Telemetry **shutdown** is excluded from that metric.
- Both remain visible in user-observed process wall time.

## Upgrading the Exporter: Remaining Concerns

- **Persisted telemetry needs a later run.** With the network drain budget set to zero, shutdown saves pending telemetry locally for a later process to upload. If the user never runs `func` again, that data will not be sent.
- **CI and ephemeral environments may not upload the backlog.** A one-shot job or short-lived container may exit without another process to send persisted telemetry. Consider disabling persist-on-shutdown in those environments.
- **Storage location must remain stable.** The default storage directory can be tied to the process name and install location. Side-by-side installs or `func update` may leave backlogs isolated from the next CLI instance; configure a stable `StorageSubDirectory` if supported by the selected exporter version.
- **Opt-out behavior needs a policy.** When telemetry is disabled, pending files will not be uploaded. Decide whether to remove them and document the local storage behavior.
- **Unwritable storage remains unverified.** Test what happens when the exporter cannot write its storage directory and ensure shutdown does not unexpectedly wait on network ingestion.
- **SDK statistics are enabled by default in newer exporter versions.** Decide whether to retain them or disable them with `APPLICATIONINSIGHTS_SDKSTATS_DISABLED`, and account for their separate reporting behavior in telemetry/privacy documentation.
- **Keep package versions compatible.** Upgrade OpenTelemetry packages and the Azure Monitor exporter together, and check transitive `Microsoft.Extensions.*` version changes for warnings or conflicts.
- **Allow for late-arriving telemetry.** Persisted data can be exported days after the original event; dashboards and alerts should account for that delay.

## Proposed Improvements

1. **ReadyToRun:** not implemented or measured. Publish the CLI with ReadyToRun enabled, then compare startup time and package size against the current build.
2. **Defer/concurrently initialize providers:** not implemented. Initialize telemetry only when needed, or move provider startup off the critical path, while preserving telemetry for commands that use it.
3. **Explicit concurrent bounded shutdown:** not implemented. The branch instead uses the exporter's internal zero network-drain budget. An explicit shutdown path would need to stop providers concurrently and enforce a deadline so disposal cannot hold up exit.
4. **Print notices concurrently with shutdown:** not implemented. Start shutdown before printing version/alias notices so useful output is not delayed by telemetry persistence.
5. **Persist locally instead of waiting for ingestion:** implemented, assuming writable exporter storage. This queues unsent telemetry on disk at exit for a later CLI run to upload, avoiding a network wait during shutdown.
6. **Keep metrics:** implemented. Retain the metrics provider alongside tracing so command-duration and other existing measurements continue to be recorded.

This is a narrow, low-risk fix for the previously demonstrated 1:1 network-delay problem. It does **not** solve general telemetry startup cost, synchronous provider disposal overhead, JIT cost, notice ordering, or the unwritable-storage edge case.
