# Native Go project template

This archive is bundled inside the Go stack workload, not published on a separate
feed. It packages the stack's existing worker SDK sources without copying or
rewriting them. The native template retains module-name normalization, the
`native` worker setting, and the extension bundle options.

The `SkipGoModTidy` parameter describes the existing init preparation option;
the template engine does not execute Go. Default `go mod tidy` preparation must
be supplied by the shared workflow before public activation. The current public
initializer and its registration remain unchanged.

The archive includes `provenance/go/project.json`, generated from the exact
packaged source hashes and package version. The opt-in CLI-owned Go workflow
provider validates that inventory, native identity and configuration before
using a host-compiled copy of the shared preparation helper. It is not resolved
from a physical workload's DI container.
