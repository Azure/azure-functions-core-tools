# `func new` — Template-Specific Parameter Options

How does a workload surface trigger-specific inputs (HTTP `--authlevel`, Blob `--path` + `--connection`, Timer `--schedule`, …) on `func new`?

Three options, in order from "smallest abstraction" to "largest abstraction".

---

## Option 1 — Pure consistency with `IProjectInitializer.GetInitOptions`

The workload returns a flat `IReadOnlyList<Option>` covering every option across every template it owns. `NewCommand` attaches them all the same way `InitCommand` attaches init options today.

### Workload code

```csharp
internal sealed class NodeFunctionCreator : IFunctionCreator
{
    public string Stack => "node";

    public Option<string> AuthLevelOption { get; } = new("--authlevel", "-a")
        { Description = "Authorization level (http template only): function|anonymous|admin" };

    public Option<string> PathOption { get; } = new("--path")
        { Description = "Blob/queue path (blob/queue templates only)" };

    public Option<string> ConnectionOption { get; } = new("--connection")
        { Description = "Storage connection setting (blob/queue/cosmos templates)" };

    public Option<string> ScheduleOption { get; } = new("--schedule")
        { Description = "CRON expression (timer template only)" };

    // …one Option per parameter across every template the workload supports

    public IReadOnlyList<Option> GetNewOptions() =>
        [AuthLevelOption, PathOption, ConnectionOption, ScheduleOption /* … */];

    public async Task<NewResult> CreateAsync(NewContext ctx, ParseResult parse, CancellationToken ct)
    {
        var template = parse.GetValue<string>("--template");

        if (template == "http")
        {
            var auth = parse.GetValue(AuthLevelOption);
            if (parse.GetValue(PathOption) is not null)
                throw new InvalidOperationException("--path is not valid for the http template.");
            // scaffold http function with `auth`
        }
        else if (template == "blob")
        {
            var path = parse.GetValue(PathOption)
                ?? throw new InvalidOperationException("--path is required for blob.");
            // …
        }
        // …
    }
}
```

### `func new --help` output

```
Options:
  -n, --name           Function name
  -t, --template       Template name
      --force          Overwrite existing files
  -a, --authlevel      Authorization level (http template only): function|anonymous|admin
      --path           Blob/queue path (blob/queue templates only)
      --connection     Storage connection setting (blob/queue/cosmos templates)
      --schedule       CRON expression (timer template only)
      --queue-name     Queue name (queue template only)
      --database-name  Cosmos database name (cosmosdb template only)
  … (continues — every option for every template across every installed workload)
```

### Trade-offs

- ✅ Identical surface to `IProjectInitializer`. Zero new abstraction.
- ✅ Workloads keep full System.CommandLine flexibility (custom parsers, completions, default factories).
- ❌ `func new --help` is a junk drawer.
- ❌ Each workload reimplements "is this option valid for this template?" / "is this option required for this template?" validation.
- ❌ Cross-workload option-name collisions still possible with no centralized story.

---

## Option 2 — Flat options, but `TemplateDescriptor` declares its options

Same `GetNewOptions()` mechanism as Option 1, plus a small descriptor so the CLI can scope `--help` to the chosen template and validate required/relevance centrally.

### Abstraction additions

```csharp
public sealed record TemplateDescriptor(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<string> RequiredOptionNames,   // ["--path", "--connection"]
    IReadOnlyList<string> OptionalOptionNames);  // ["--authlevel"]

public interface IFunctionCreator
{
    string Stack { get; }
    bool CanHandle(string stack);
    IReadOnlyList<Option> GetNewOptions();
    Task<IReadOnlyList<TemplateDescriptor>> GetTemplatesAsync(CancellationToken ct);
    Task<NewResult> CreateAsync(NewContext ctx, ParseResult parse, CancellationToken ct);
}
```

### Workload code

`GetNewOptions()` and `CreateAsync()` look the same as Option 1, plus:

```csharp
public Task<IReadOnlyList<TemplateDescriptor>> GetTemplatesAsync(CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<TemplateDescriptor>>(
    [
        new("http",  "HTTP trigger",  "...", RequiredOptionNames: [],                          OptionalOptionNames: ["--authlevel"]),
        new("blob",  "Blob trigger",  "...", RequiredOptionNames: ["--path", "--connection"],  OptionalOptionNames: []),
        new("timer", "Timer trigger", "...", RequiredOptionNames: ["--schedule"],              OptionalOptionNames: []),
    ]);
```

`CreateAsync` can now trust the CLI already validated relevance + required-ness:

```csharp
public async Task<NewResult> CreateAsync(NewContext ctx, ParseResult parse, CancellationToken ct)
{
    var template = parse.GetValue<string>("--template");
    if (template == "http") { /* read AuthLevelOption, scaffold */ }
    // …
}
```

### CLI behavior

- `func new --help` (no template selected): only built-in options + a hint to pass `--template <name> --help`.
- `func new --template http --help`: only `--authlevel` (plus built-ins).
- `func new --template blob` without `--path`: rejected before the workload is invoked.
- `func templates list` renders the descriptors directly.

### Trade-offs

- ✅ Scoped `--help` per template.
- ✅ Centralized required/relevance validation.
- ✅ Workload still owns `Option` instances and keeps System.CommandLine flexibility.
- ⚠️ Two-pass parse (peek `--template`, then attach that template's options). Workable with System.CommandLine but adds plumbing.
- ⚠️ Options and descriptors must stay in sync inside the workload (a unit-test concern, not a runtime concern).

---

## Option 3 — Schema-driven `TemplateDescriptor` (CLI owns binding)

Workload declares parameters via metadata, CLI binds them to options, and the workload never touches `ParseResult` for template params.

### Abstraction additions

```csharp
public sealed record TemplateParameter(
    string Name,                                  // "authlevel"
    string Description,
    Type ValueType,                               // typeof(string), typeof(int)
    bool Required,
    object? DefaultValue,
    IReadOnlyList<string>? AllowedValues);        // ["function","anonymous","admin"]

public sealed record TemplateDescriptor(
    string Name,
    string DisplayName,
    string Description,
    IReadOnlyList<TemplateParameter> Parameters);

public interface IFunctionCreator
{
    string Stack { get; }
    bool CanHandle(string stack);
    Task<IReadOnlyList<TemplateDescriptor>> GetTemplatesAsync(CancellationToken ct);
    Task<NewResult> CreateAsync(
        NewContext context,
        IReadOnlyDictionary<string, object?> parameterValues,   // resolved by CLI
        CancellationToken ct);
}
```

### Workload code

```csharp
public Task<IReadOnlyList<TemplateDescriptor>> GetTemplatesAsync(CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<TemplateDescriptor>>(
    [
        new("http", "HTTP trigger", "...",
        [
            new("authlevel", "Authorization level", typeof(string),
                Required: false, DefaultValue: "function",
                AllowedValues: ["function","anonymous","admin"]),
        ]),
        new("blob", "Blob trigger", "...",
        [
            new("path",       "Blob path",       typeof(string), Required: true,  DefaultValue: null,                  AllowedValues: null),
            new("connection", "Connection name", typeof(string), Required: false, DefaultValue: "AzureWebJobsStorage", AllowedValues: null),
        ]),
    ]);

public async Task<NewResult> CreateAsync(NewContext ctx, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
{
    var template = ctx.TemplateName!;
    if (template == "http")
    {
        var auth = (string)values["authlevel"]!;
        // scaffold
    }
    // …
}
```

### CLI behavior

- Builds System.CommandLine `Option`s dynamically from each `TemplateParameter` for `--help` rendering.
- Validates required + allowed values before invoking the workload.
- In interactive mode, prompts for missing required parameters.
- Workload never reads `ParseResult` for template params — receives a typed dictionary.
- `func templates list` renders full parameter info for free.

### Trade-offs

- ✅ Best UX (uniform validation, prompts, help formatting across all workloads).
- ✅ Single source of truth — no duplication between `Option` instances and template metadata.
- ✅ `func templates list` is rich without extra work.
- ❌ Largest abstraction surface; the descriptor is the contract and is hard to evolve.
- ❌ Workloads lose System.CommandLine features (custom parsers, completions, complex default factories) unless the schema grows to support them.
- ❌ Loosely typed `IReadOnlyDictionary<string, object?>` pushes casting into every workload.

---

## Summary

| | Abstraction surface | `--help` quality | Validation location | Workload flexibility |
|---|---|---|---|---|
| **1. Pure consistency** | None added | Junk drawer | Each workload | Full |
| **2. Flat + descriptor** | Small (`TemplateDescriptor` with name lists) | Scoped per template | CLI (relevance/required) + workload (semantics) | Full |
| **3. Schema-driven** | Large (parameter schema) | Scoped per template | CLI (almost all) | Reduced (schema-bounded) |

---

## Orthogonal concern: cross-stack template contribution

The three options above are about **how** a workload exposes its template parameters. A separate but related question is **which workloads contribute templates** to a given `func new` invocation.

### The companion-workload case

Some workloads are stack-specific and own their projects: Python, Node, .NET-isolated each register `IProjectDetector` and claim directories matching their shape. Other workloads are **companions**: Durable Functions, for example, doesn't own a project, but adds templates that apply on top of Python, Node, and .NET-isolated projects.

Per spec §5.1, `IProjectDetector` and `ITemplateProvider` are independent contribution points. A workload can register either, both, or neither:

| Workload kind        | `IProjectDetector` | `ITemplateProvider` | Example                |
|----------------------|--------------------|---------------------|------------------------|
| Stack owner          | ✅                  | ✅                   | Python, Node, .NET     |
| Companion (cross-stack) | ❌               | ✅                   | Durable, extension packs |
| Command-only         | ❌                  | ❌ (uses `IExternalCommand`) | `func kubernetes`, `func azure` |

### Implications for `func new`

- **Ownership** is single-winner. The `IWorkloadResolver` (spec §5.2) picks the one workload that owns the current directory: Python wins for a `requirements.txt` project, Node wins for a Functions-shaped `package.json`, etc. Companions abstain because they don't register `IProjectDetector`.

- **Template enumeration** is multi-contributor. `func new` injects `IEnumerable<ITemplateProvider>` and aggregates from every installed workload, then filters by the resolved owner's stack.

A workload's `ITemplateProvider` returns templates tagged with the stacks they apply to:

```csharp
internal sealed class DurableTemplates : ITemplateProvider
{
    public IReadOnlyList<TemplateDescriptor> GetTemplates() =>
    [
        new("durable-orchestration", Stacks: ["python", "node", "dotnet-isolated"], /* … */),
        new("durable-activity",      Stacks: ["python", "node", "dotnet-isolated"], /* … */),
        new("durable-entity",        Stacks: ["dotnet-isolated"], /* … */),
    ];
}
```

`func new` in a Python project then shows:

- HttpTrigger, TimerTrigger, BlobTrigger, … (from Python workload, `Stacks: ["python"]`)
- DurableOrchestration, DurableActivity (from Durable workload, filtered in because tagged `python`)
- *Not* DurableEntity (tagged `dotnet-isolated` only)

### Impact on the three options above

- **Option 1.** Flat options compound across workloads. A Python + Durable install adds Durable's options to the Python options. The "junk drawer" problem gets worse, not better, in companion scenarios. ❌
- **Option 2.** The descriptor's `Stack`/`Stacks` field becomes load-bearing: both for ownership-vs-companion routing in `func new` *and* for filtering options to the right scope. ✅ Falls out naturally.
- **Option 3.** Same as option 2, plus the CLI owns parameter binding per template — companion templates render identically to owner templates. ✅ Cleanest.

This pushes mildly in favour of option 2 or 3 over option 1: the moment companions exist, a per-template descriptor with stack metadata stops being optional.

### Out of scope here

- Whether a template applies to a stack is currently modelled as a string-list match. More sophisticated rules (e.g. "applies to any Python project where the Durable extension is referenced in `requirements.txt`") are a separate design.
- How companions discover *which other workloads are installed* — and whether they need to — is a separate concern. The current design says they don't: templates are pre-filtered by stack tag, not by inspecting peers.
