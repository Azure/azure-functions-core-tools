# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

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
Concurrent mutation of template sources or the output
filesystem is not qualified, and no transactional rollback is promised.

Resolved bundle versions are validated when constructing the command context;
host.json version ranges are not resolved versions. Bundle constraint input comes
only from that context. The availability snapshot captures workloads, not another
copy of bundle identity or version. A malformed folder-mounted template is
reported as an unavailable candidate without preventing healthy catalog entries
from being listed.
