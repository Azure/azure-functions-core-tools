# CLI v5 Templates

These documents describe the Template Engine discovery and selection foundation and the responsibilities that remain for later command integration.

- [Component model](Templates-EndToEnd-Object-Model-Review.md) — responsibility boundaries and the discovery/selection contract.
- [Architecture](architecture.md) — installed-template ownership, selection, constraints, and later integration gates.
- [Implementation](implementation.md) — the PR #5872 scope, behavior, tests, and current limitations.
- [Workload template supply and registration](packaging-and-acquisition.md) — the proposed package, readiness, and source-lifetime contract.

`func init` selects project templates. `func new` selects item templates. Quickstarts remain project templates, not a third template kind. PR #5872 lists and resolves installed templates; creation and writing files belong to later integration work.
