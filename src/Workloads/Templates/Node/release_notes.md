# Azure.Functions.Cli.Workloads.Templates.Node

## 1.0.0

- Function names containing characters that are not valid JavaScript/TypeScript identifiers (e.g. `HttpTrigger-Node`) are now sanitized before being emitted as `function` declarations and handler references, so generated sources parse cleanly (#5202).
- Initial scaffold of the Node.js templates workload (v2 template-engine schema, Node v4 programming model). 33 templates statically authored in-repo under `content/v2/`. No CDN download of templates at pack time. **Per-channel template subsetting** is on: pack fetches `bin/extensions.json` from the active channel's latest listed bundle (CDN `index.json` + HTTP-Range zip extraction; only listed versions consumed) and filters the template set against the committed `_bindings.json` map (Templates Workload Spec §4.3 / §6.1). Cross-reference at this revision: stable 4.32.0 → 31/33, preview 4.42.0 → 33/33, experimental 4.6.0 → 31/33 (McpPromptTrigger JS+TS dropped where `mcpPromptTrigger` binding isn't yet shipped).
