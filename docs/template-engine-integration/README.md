# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

## Current status

The common v5 base already contains engine context/session/host models,
parameter/catalog projections, and raw workload/bundle constraint helpers.
The detailed proposal, design and task list describe a broader execution path;
they are not proof that resolution, preparation, or command cutover is complete.

The first P0 implementation adds [project compatibility checks for function templates](../template-engine-constraints/design.md)
outside this engine integration. It neither modifies engine behavior nor wires
the new matcher into current commands. Runtime facts and admission contracts
do not expose Microsoft.TemplateEngine types.

See the existing [Templates design and implementation status](../templating-system/design.md)
for current scope, historical shared-hive assumptions, and remaining private
registration, captured-output and prepared-operation work.
