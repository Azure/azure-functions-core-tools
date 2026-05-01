# Azure Functions Core Tools — Design Docs

This branch hosts design and proposal documents for Azure Functions Core Tools. It is intentionally separate from `main` and has no shared history with the source code.

## Layout

- `proposed/` — In-progress proposals open for discussion. Drafts live here while a design is being shaped and reviewed.
- `accepted/` — Designs that have been reviewed, accepted and implemented into the product. Once a proposal is approved, move it from `proposed/` to `accepted/`.

## Workflow

1. Add a new design under `proposed/` (e.g. `proposed/my-feature.md`).
2. Open a PR against the `docs` branch for review.
3. After the design is part of the product (implemented), move the file to `accepted/` in a follow-up PR.
