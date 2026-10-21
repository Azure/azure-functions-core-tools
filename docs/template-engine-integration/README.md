# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

## Project Runtime Qualification

The explicit internal `CreateProject` flow adds mandatory CLI-owned configuration to installed project-template creation. Existing `Create` calls remain item-only. The project flow requires supplied availability, installed `IProjectStack` metadata from the existing stack catalog, and paired planner/writer support.

This is staged internal support: production commands do not call `CreateProject`, and the planner and writer are not registered in production DI. Existing public `init` and `new` routes remain unchanged.

Language-alias metadata keys are matched case-insensitively, consistent with the installed catalog, while configuration retains the selected stack's canonical language spelling.

The current subset supports unconditional literal primary-output paths, the default source layout, content parameters and installed stack/language aliases. It rejects source remapping, topology conditions, filename-renaming symbols, supplemental configuration and ordinary post-actions before creation. Resolved `host.json` roots are inventoried independently of configuration declarations. Every root must have exactly one configuration action, and template content cannot author `.func/config.json`.

A configuration anchor can be another generated primary-output file when its resolved parent is an independently discovered host root. An anchor outside those roots is rejected before creation.

Host roots and configuration anchors must correspond to regular files in the captured native output image, not just reported native effects. Native placeholder directory effects cannot establish a `host.json` root or serve as a primary-output configuration anchor. Directory effects used in either role are rejected before real installation. A declared but ungenerated `host.json` cannot establish a Functions project root.

Preview reports template and configuration effects without writing. Creation finalizes configuration in declaration order through the existing atomic writer. A finalization failure preserves generated files and completed configuration writes, reports completed roots and fails the invocation. Existing user configuration is refused even with file-conflict force; adoption and repair remain owned by the existing initializer.

Configuration occupancy checks use the writer's injected `IFileSystem` for both files and directories. An occupied destination before creation returns `FileConflict` without generating a new project root. Configuration is committed with an atomic create-only rename, so a destination that appears after the checks is not replaced. That late collision returns `ConfigurationFailure`, preserving user bytes, generated output and any earlier completed configurations. Legacy initializer/repair writes retain their overwrite-capable behavior.

After a configuration write commits, its root and file change are recorded before cancellation is observed again. Cancellation after the final configuration commit returns the completed result. If another plan remains, cancellation stops before that plan starts. Cancellation or failure during configuration preserves the installed native output, completed configuration writes and internal committed-output marker. The result never reports deferred configuration as committed. Cancellation before any real installation still propagates normally.

The project policy runs on the captured native image and resolved primary outputs before real installation, without rerendering or dispatching arbitrary native post-actions. The complete captured directory inventory remains available for path, link, kind and source checks, including existing directory no-ops omitted from reported changes. Creation results report installed native effects plus only completed configuration writes, not unwritten preview configuration paths. The session retains native lifetime ownership and the invocation coordinator owns the optional project policy.

The fixture action ID `00e33184-ffc7-43ef-bec0-b1684df8ad56` remains proposed, not a stable public authoring contract. These tests qualify controlled runtime fixtures and configuration compatibility, not production template packages or a public command switch. Ordinary restores, wider project topology, live-hive leases and concurrent filesystem mutation remain outside this qualified subset.

## Qualified item invocation

The current runtime grants item invocation only when the current mounted template
type is recognized and agrees with its cached descriptor. An installed folder whose
type changed before selection remains unavailable, with a diagnostic to correct
the type and refresh the installed template. Changes after selection still require
selection to be rerun.

Invocable parameter definitions and func host aliases come from the current
mounted configuration, not the persisted engine descriptor. Native evaluation
receives an input dataset built from those same mounted definitions.
Folder-template parameter edits between flows therefore take effect without
refreshing the shared hive. The selected snapshot also captures and compares func
host content separately, so alias edits after selection require rerunning selection
without changing the raw template.json SHA used by source provenance.

Resolved items hold an explicit invoker bound to the owning command session.
The invocation coordinator owns admission, preview and creation ordering, while
the session retains native environment and disposal ownership. A captured snapshot
owns a cloned JSON view shared by host constraint, default and post-action checks.
Fresh mounted reads still produce new snapshots for source revalidation. Native
model parsing remains separate from the shared host JSON view.

Mounted primary parameter declarations without an authored default remain unset
in the projected command schema. This prevents an engine-synthesized scalar
default such as integer zero from becoming explicit input and overriding a
native generated fallback. Authored defaults, including zero, false and empty
strings, remain intact. Additional configuration and parameters not declared in
the primary configuration retain the engine's merged defaults.

Each invocation renders through the native engine into an owned in-memory output
image, retaining the real logical output, fallback name and command context.
Native parameter, form, condition and generated-symbol evaluation determines the
actual output. The engine still evaluates effects and creation separately, so its
prepass paths are not treated as the written-file inventory. Captured files,
completed streams and directory creation determine the image instead.

Generated, derived, computed and bound output paths are supported only when their
actual captured paths pass the CLI's containment, link, kind and real-target
conflict checks. Native force consent is not CLI force authorization. Preview
captures an image without installing real output. Separate preview and apply
invocations reevaluate the template and can produce different generated names;
each apply admits and installs its own captured image without another render.
Item post-actions, native deletion, metadata mutations and private-output
timestamp inspection remain unavailable through this capture path. Filesystem routing is not a sandbox for arbitrary
extension code or process, environment and network access.

Apply revalidates mounted source identity and real target state before installing
the captured bytes. Incoming files are materialized privately on the destination
volume, then moved into place with create-only or explicitly forced semantics.
Default and custom empty-directory placeholders remain directory effects; empty
regular files remain files. Existing directories are retained without an overwrite
effect or a force requirement. Final paths and kinds are checked before success.
Linked children are refused, while the requested directory and its ancestors may
themselves be linked, as with macOS temporary directories. Concurrent filesystem
mutation and crash-atomic multi-file installation are not qualified.

Preview and pre-install file conflicts describe proposed changes, not completed
output. Cancellation before real installation discards the image. Failure or cancellation
after partial installation reports the completed real file and directory entries,
without rolling them back. Once all output is installed, late cancellation does
not erase its successful effects. A successful private native render alone is
never reported as completed real output.

Resolved bundle versions are validated when constructing the command context;
host.json version ranges are not resolved versions. Bundle constraint input comes
only from that context. The availability snapshot captures workloads, not another
copy of bundle identity or version. A malformed folder-mounted template is
reported as an unavailable candidate without preventing healthy catalog entries
from being listed.
