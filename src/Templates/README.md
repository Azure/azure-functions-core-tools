# Func template packages

Real Microsoft-templating-engine template packages consumed by the `func`
CLI. These are the same `.template.config/template.json` packages the .NET
templating engine (and therefore `dotnet new`) understands, so they can be
exercised end to end offline via a local NuGet feed.

## Packages

| Project | Package id | Version |
| --- | --- | --- |
| `Node/Templates.Node.csproj` | `Microsoft.Azure.Functions.Templates.Node` | `1.0.0` |
| `Python/Templates.Python.csproj` | `Microsoft.Azure.Functions.Templates.Python` | `1.0.0` |

Both packages are **content-only** (no assemblies) and declare **both** func
NuGet package types:

```xml
<PackageType>FuncItemTemplates;FuncAppTemplates</PackageType>
```

`FuncItemTemplates` marks item templates (`func new`) and `FuncAppTemplates`
marks project templates (`func init`); the CLI's template discovery and the
search index use these types to identify func template packages. Each template
lays out at the **package root** in standard engine layout:

```
<TemplateName>/.template.config/template.json
```

## Templates

### Node (`Microsoft.Azure.Functions.Templates.Node`)

| Template dir | Type | shortNames | Language |
| --- | --- | --- | --- |
| `HttpTrigger-JavaScript` | item | `http`, `HttpTrigger`, `HttpTrigger-JavaScript` | javascript |
| `HttpTrigger-TypeScript` | item | `http`, `HttpTrigger`, `HttpTrigger-TypeScript` | typescript |
| `TimerTrigger-JavaScript` | item | `timer`, `TimerTrigger`, `TimerTrigger-JavaScript` | javascript |
| `TimerTrigger-TypeScript` | item | `timer`, `TimerTrigger`, `TimerTrigger-TypeScript` | typescript |
| `EmptyProject-JavaScript` | project | `empty`, `EmptyFunctionProject-JavaScript` | javascript |
| `EmptyProject-TypeScript` | project | `empty`, `EmptyFunctionProject-TypeScript` | typescript |

The `http` item templates carry a `func-extension-bundle` constraint
(`[4.0.0, )`) to demonstrate bundle gating end to end.

### Python (`Microsoft.Azure.Functions.Templates.Python`)

| Template dir | Type | shortNames | Notes |
| --- | --- | --- | --- |
| `HttpTrigger` | item | `http`, `HttpTrigger`, `HttpTrigger-Python` | Append flow: one staged `__snippet__.py` + a func-owned post action |
| `EmptyProject` | project | `empty`, `EmptyFunctionProject-Python` | Reproduces `func init` output (incl. `function_app.py`) |

The Python `http` template uses the **append** shape: it stages a single
`__snippet__.py` and declares a func-owned post action that appends the snippet
into the target app file. The post action id is recorded in
[`../../openspec/changes/adopt-ms-template-engine/FUNC-POST-ACTIONS.md`](../../openspec/changes/adopt-ms-template-engine/FUNC-POST-ACTIONS.md);
the dispatcher implementation must use the same GUID.

Every item template also ships a `func.host.json` alongside `template.json`
(engine-inert) that carries the CLI-facing symbol metadata (`symbolInfo[]`) and
the `functionName` validator.

## Build the local feed

`eng/scripts/build-local-template-feed.ps1` packs both projects and publishes
the `.nupkg` files into a local feed directory (a flat folder of `.nupkg` files
is a valid NuGet source). It is idempotent — re-running repacks in place.

```powershell
# Default feed: <repo>/artifacts/local-template-feed
pwsh ./eng/scripts/build-local-template-feed.ps1

# Custom location, wiping previously published template packages first
pwsh ./eng/scripts/build-local-template-feed.ps1 -FeedDirectory C:/feeds/func -Clean
```

## Point the CLI at the feed

Use the `--source` option to point `func new` at the local feed for offline
install/search:

```powershell
func new --search http --source ./artifacts/local-template-feed
func new --install Microsoft.Azure.Functions.Templates.Node --source ./artifacts/local-template-feed
```

Because these are standard engine packages, you can also validate them directly
with `dotnet new`:

```powershell
dotnet new install ./artifacts/local-template-feed/Microsoft.Azure.Functions.Templates.Node.1.0.0.nupkg
dotnet new list
dotnet new uninstall Microsoft.Azure.Functions.Templates.Node
```
