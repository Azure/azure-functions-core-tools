# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

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
