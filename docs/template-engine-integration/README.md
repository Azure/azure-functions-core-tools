# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

## Project Runtime Qualification

The explicit internal `CreateProject` flow adds mandatory CLI-owned configuration to installed project-template creation. Existing `Create` calls remain item-only. The project flow requires supplied availability, installed `IProjectStack` metadata from the existing stack catalog, and paired planner/writer support.

This is staged internal support: production commands do not call `CreateProject`, and the planner and writer are not registered in production DI. Existing public `init` and `new` routes remain unchanged.

Language-alias metadata keys are matched case-insensitively, consistent with the installed catalog, while configuration retains the selected stack's canonical language spelling.

The current subset supports unconditional literal primary-output paths, the default source layout, content parameters and installed stack/language aliases. It rejects source remapping, topology conditions, filename-renaming symbols, supplemental configuration and ordinary post-actions before creation. Resolved `host.json` roots are inventoried independently of configuration declarations. Every root must have exactly one configuration action, and template content cannot author `.func/config.json`.

A configuration anchor can be another generated primary-output file when its resolved parent is an independently discovered host root. An anchor outside those roots is rejected before creation.

Host roots and configuration anchors must correspond to preview create, change or overwrite effects. A declared but ungenerated `host.json`, or a deletion effect, cannot establish a Functions project root.

Preview reports template and configuration effects without writing. Creation finalizes configuration in declaration order through the existing atomic writer. A finalization failure preserves generated files and completed configuration writes, reports completed roots and fails the invocation. Existing user configuration is refused even with file-conflict force; adoption and repair remain owned by the existing initializer.

Configuration occupancy checks use the writer's injected `IFileSystem` for both files and directories. An occupied destination before creation returns `FileConflict` without generating a new project root. Configuration is committed with an atomic create-only rename, so a destination that appears after the checks is not replaced. That late collision returns `ConfigurationFailure`, preserving user bytes, generated output and any earlier completed configurations. Legacy initializer/repair writes retain their overwrite-capable behavior.

The project flow retains mounted-type checks and the native runtime's selected-configuration, topology and output-boundary validation. Creation results report native creation-call effects plus only completed configuration writes, not unwritten preview configuration paths.

The fixture action ID `00e33184-ffc7-43ef-bec0-b1684df8ad56` remains proposed, not a stable public authoring contract. These tests qualify controlled runtime fixtures and configuration compatibility, not production template packages or a public command switch. Ordinary restores, reusable native evaluation, wider topology, live-hive leases and hostile concurrent filesystem mutation remain outside this qualified subset.

## Qualified item invocation

The current runtime grants item invocation only when the current mounted template
type is recognized and agrees with its cached descriptor. An installed folder whose
type changed before selection remains unavailable, with a diagnostic to correct
the type and refresh the installed template. Changes after selection still require
selection to be rerun.

The pinned TemplateEngine reevaluates symbols for preview, creation effects, and
actual creation; its creation effects are not a frozen output plan. This item flow
therefore supports parameter-driven file renames, `name`/`sourceName` renames,
static source mappings, and source/modifier conditions independent of non-parameter
symbols. Generated, derived, computed, and bound symbols cannot drive output
topology, including indirectly through conditions or native generated aliases.
Condition qualification uses populated, unconditionally enabled parameter names
and their value forms, using mounted parameter definitions, host defaults, and
native value conversion. Native name selection honors an empty name and
`preferDefaultName`; an explicitly declared `name` is an ordinary parameter,
not automatically populated from the invocation name. Absent or conditionally
disabled parameters cannot hide a non-parameter dependency by declaring a longer
name or value-form alias.
Unsupported topology returns `InvalidTemplate` before native preview or writes,
even with force. Content-only randomness remains supported.

Preview and file-conflict results describe proposed changes. Successful creation
uses native creation effects and checks that the reported paths exist (or were
deleted), rather than returning the earlier preview as if it had been written.
Creation failures do not report preview paths as completed effects; partial output
is still possible if the native engine fails after writing. Existing links below
the requested output directory are rejected; the requested directory and its
ancestors may themselves be linked, as with macOS temporary directories.
Both item and project invocations now reject an existing file where an output
directory ancestor is required, returning `Failed` before creation rather than
letting the native write fail later.
Concurrent mutation of template sources or the output
filesystem is not qualified, and no transactional rollback is promised.

Resolved bundle versions are validated when constructing the command context;
host.json version ranges are not resolved versions. Bundle constraint input comes
only from that context. The availability snapshot captures workloads, not another
copy of bundle identity or version. A malformed folder-mounted template is
reported as an unavailable candidate without preventing healthy catalog entries
from being listed.
