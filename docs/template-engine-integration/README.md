# template-engine-integration

Define the context-bound Microsoft.TemplateEngine integration layer for listing, resolving, and invoking installed templates.

## Contract boundaries

[Project compatibility checks for function templates](../template-engine-constraints/design.md)
are a separate read-only boundary. Their public stack-reporting contracts and
internal compatibility results do not expose Microsoft.TemplateEngine types.

The proposal, design and task list in this directory preserve the earlier
engine-integration plan. They are not evidence of completed resolution,
preparation or command integration. See the
[Templates overview](../templating-system/design.md) for contract navigation
and historical scope, and
[PR #5767](https://github.com/Azure/azure-functions-core-tools/pull/5767)
for Foundation implementation and validation evidence.
