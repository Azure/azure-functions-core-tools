# CLI v5 Templates

These documents describe the component model, supporting architecture, and implementation status for one Template Engine in Azure Functions CLI v5. In the intended command journeys, `func init` selects project templates and `func new` selects item templates. Quickstarts are project templates, not a third template kind.

Existing Stack initializers remain in use. PR #5872 proposes installed-template catalog and exact-selection capabilities without changing command behavior. Template-backed command paths are not activated.

- [Component model](Templates-EndToEnd-Object-Model-Review.md) — implementation authority for responsibilities and the intended command journeys.
- [Architecture](architecture.md) — supporting design for template selection, content, generation, and publication.
- [Implementation](implementation.md) — foundation scope, deferred work, and validation status.
- [Packaging and acquisition](packaging-and-acquisition.md) — proposed cross-Stack content and workload contract.

Implementation proceeds through small, reviewable increments. A complete Node.js `func init` / `func new` / Functions Host journey is not a prerequisite for the first object-model foundation. Earlier designs and investigations remain in the [archive](archive/).
