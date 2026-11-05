# CLI v5 Templates

These documents describe the approved architecture and implementation status for one Template Engine in Azure Functions CLI v5. `func init` selects project templates; `func new` selects item templates. Quickstarts are project templates, not a third template kind.

Existing Stack initializers remain in use. The PR 1 foundation adds installed-template catalog and exact-selection capabilities without changing command behavior. Template-backed command paths are not activated.

- [Architecture](architecture.md) — component ownership, template selection, the production-content authoring contract, generation, and publication requirements.
- [Implementation](implementation.md) — current foundation, deferred work, and validation status.

Implementation proceeds through small, reviewable increments. A complete Node.js `func init` / `func new` / Functions Host journey is not a prerequisite for the first object-model foundation. Earlier designs and investigations remain in the [archive](archive/).
