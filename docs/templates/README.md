# CLI v5 Templates

These documents describe the accepted Template Engine component model, its discovery and selection foundation, and the bounded .NET integration candidate.

- [Component model](Templates-EndToEnd-Object-Model-Review.md) — the six accepted responsibilities and intended command journeys.
- [Architecture](architecture.md) — ownership boundaries, installed-template selection, generation, and publication.
- [Implementation](implementation.md) — the discovery foundation, .NET integration candidate, validation evidence, and limitations.
- [Packaging and acquisition](packaging-and-acquisition.md) — workload supply, registration, and deferred package lifecycle.

`func init` selects project templates. `func new` selects item templates. Quickstarts remain project templates, not a third template kind. The .NET candidate uses Microsoft.TemplateEngine for selection and private generation, then publishes through CLI-owned guarded writing. Other Stacks remain gated until their own content and runtime journeys qualify.
