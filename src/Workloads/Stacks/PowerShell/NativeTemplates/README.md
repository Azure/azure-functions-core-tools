# Native PowerShell project template

This archive is bundled inside the PowerShell stack workload, not published on a
separate feed. It packages the existing profile, settings, ignore file, and
commented managed-dependency requirements without rewriting them.

Runtime versions remain `7.4` (default) and `7.6`. Managed dependencies default
to false. The native engine conditionally creates `host.json`'s
managed-dependency section but performs no gallery lookup.

The owned `requirements.psd1` source is packaged under `.template.config` and
is not emitted by the base template. A trusted workload-owned conditional
create-file effect must resolve the Az major version and preview/write the
requirements only when requested. A failed best-effort lookup keeps
`MAJOR_VERSION` commented out. This shared preparation contract remains a
prerequisite for public activation; no source or filename guard is broadened.
The current public initializer and registration remain unchanged.

`provenance/powershell/project.json` binds the exact packaged source inventory
and version to the native identity. An opt-in CLI-owned workflow provider uses
the same source-linked helper and requirements resource as the physical stack,
without loading its preparation services from another DI container. Preview
resolves the optional Az version once and freezes the exact requirements
content and diagnostics. Completion rechecks source, target and canonical
configuration, then atomically creates that same file without replacement.

Direct tests exercise this production provider and materializer on mounted
archives, including both runtimes, no helper lookup/read when disabled,
source/parameter/preview drift, occupied destinations, atomic write failure
and cancellation. Managed-true cases substitute only the optional version
lookup; they do not qualify PSGallery or acquire Az modules.
