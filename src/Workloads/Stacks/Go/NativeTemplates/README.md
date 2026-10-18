# Native Go project template

This archive is bundled inside the Go stack workload, not published on a separate
feed. It packages the stack's existing worker SDK sources without copying or
rewriting them. The native template retains module-name normalization, the
`native` worker setting, and the extension bundle options.

Both native generation and the physical initializer derive the module name from
the project name, or the directory name when omitted. They trim whitespace,
lowercase invariantly, replace ASCII spaces with hyphens, and retain only ASCII
letters, digits, underscores, and hyphens. Empty results, leading hyphens,
`go`, `toolchain`, and Go-disallowed Windows device names (`con`, `prn`, `aux`,
`nul`, `com1`-`com9`, `lpt1`-`lpt9`) use `app`. Unicode directory paths remain
supported; their names are not copied verbatim into `go.mod`.

The `SkipGoModTidy` parameter describes the existing init preparation option;
the template engine does not execute Go. Default `go mod tidy` preparation must
be supplied by the shared workflow before public activation. The current public
initializer and its registration remain unchanged.

The archive includes `provenance/go/project.json`, generated from the exact
packaged source hashes and package version. The opt-in CLI-owned Go workflow
provider validates that inventory, native identity and configuration before
using a host-compiled copy of the shared preparation helper. It is not resolved
from a physical workload's DI container.

Direct provider tests mount the packed archive and exercise the production
preparation after native creation and canonical CLI configuration. They cover
omitted versus explicit skip, source and preview drift, tool diagnostics and
cancellation with committed files retained. Controlled tool results are not
proof that a real Go module cache can resolve the worker SDK.
