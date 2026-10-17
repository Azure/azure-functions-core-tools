# Native Java project template

This archive is bundled inside the Java stack workload, not published on a
separate feed. It packages the existing Maven POM, sample HTTP function,
settings, and ignore files unchanged. Both POM name placeholders use the
initializer's artifact-name normalization, including its fallback.

Project creation does not invoke Maven; the existing Java host preparation
owns that build. Extension bundle options and mandatory CLI project
configuration are retained. The current public initializer and registration
remain unchanged.

The opt-in CLI-owned Java project provider validates the versioned
`provenance/java/project.json` source inventory and supplies no preparation
operation. Mandatory project configuration still uses the shared planner and
writer; no Maven invocation is added to initialization.

Direct tests use the mounted archive and concrete provider to check exact
source admission, unchanged invocation, native creation and canonical CLI
configuration. Changed provenance or source is refused. These tests do not
run Maven or qualify a Java build, host or deployment.
