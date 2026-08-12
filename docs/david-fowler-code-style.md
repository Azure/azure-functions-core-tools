# David Fowler Code and Review Style

This is a reusable, evidence-based reference for approximating the engineering and review preferences David Fowler (`@davidfowl`) demonstrates in public GitHub work. It is not an official style guide, and it should not override a repository's established conventions.

## Research scope and confidence

The research sampled roughly 25 pull requests Fowler authored or reviewed, his public guidance repositories, and recent sample code in:

- [`davidfowl/AspNetCoreDiagnosticScenarios`](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios)
- [`davidfowl/DotNetCodingPatterns`](https://github.com/davidfowl/DotNetCodingPatterns)
- [`dotnet/aspnetcore`](https://github.com/dotnet/aspnetcore)
- [`dotnet/runtime`](https://github.com/dotnet/runtime)
- [`davidfowl/aspire-ai-chat-demo`](https://github.com/davidfowl/aspire-ai-chat-demo)
- [`davidfowl/TcpEcho`](https://github.com/davidfowl/TcpEcho)
- [`davidfowl/BedrockFramework`](https://github.com/davidfowl/BedrockFramework)

The sample emphasizes API and architecture design, ASP.NET Core internals, allocation-sensitive code, and asynchronous correctness. Fowler's prescriptive guidance documents provide unusually strong evidence for his async and ASP.NET Core preferences.

Confidence labels used below:

- **Explicit**: Fowler directly stated the preference in guidance, a pull request, or a review.
- **Observed**: The pattern appears in code he authored.
- **Inferred**: The pattern recurs across evidence, but he did not state it as a general rule.

## Short reusable checklist

When writing or reviewing code in this style:

1. Use `async` and `await` through the call stack. Do not block on tasks with `.Result` or `.Wait()`.
2. Avoid `async void` except for true event handlers. Make fire-and-forget work explicit and preserve its dependencies safely.
3. Do not capture `HttpContext`, scoped services, or mutable ambient state across background or parallel work.
4. Create `TaskCompletionSource<T>` with `TaskCreationOptions.RunContinuationsAsynchronously`.
5. Minimize API and implementation surface. Challenge abstractions, options, allocations, and special cases.
6. Measure performance changes and explain the mechanism, allocation impact, and tradeoffs.
7. Remove allocations and repeated decisions from hot paths using spans, stack storage, pooling, and cached state when justified.
8. Preserve externally observable behavior and compatibility, including logging and telemetry identities.
9. Explain non-obvious concurrency, lifetime, and ownership rules in comments.
10. Test the actual changed path, especially races, parallel execution, and allocation-sensitive behavior.

## Formatting and naming

### Follow established .NET layout

**Observed, medium confidence.** Fowler's samples and repositories consistently use four-space indentation, Allman braces, and braces around control-flow bodies. This is visible throughout [`AsyncGuidance.md`](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AsyncGuidance.md) and is configured in the [`BedrockFramework` editor settings](https://github.com/davidfowl/BedrockFramework/blob/587e46dd760e6c24c302d87aa83cde06c7ea5316/.editorconfig).

No sampled review stated a universal brace or argument-wrapping rule. Treat repository formatting as authoritative rather than attributing every standard .NET convention personally to Fowler.

### Use conventional, direct names

**Observed, high confidence.** Code uses standard .NET naming: `PascalCase` for types and members, `camelCase` for locals and parameters, and names that describe the operation or represented concept. Representative examples include `ProcessLinesAsync`, `TryReadLine`, and `ProcessLine` in the [`TcpEcho` server](https://github.com/davidfowl/TcpEcho/blob/master/src/Server/Program.cs).

Review naming suggestions are often extremely concise. For example, Fowler proposed `OnBlockDisposed` with no additional ceremony in a [memory-pool review](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2051486044).

### Adopt modern language features when they clarify the design

**Observed, high confidence.** Recent sample code uses primary constructors, collection expressions, switch expressions, records, and current framework APIs. The [`aspire-ai-chat-demo`](https://github.com/davidfowl/aspire-ai-chat-demo/tree/8213c6aa064b035bf0bca28e31cef03e085e8a7d) demonstrates these choices in application code.

Modern syntax is a means to express the design directly, not a reason to rewrite otherwise clear code.

## API and architecture

### Demand a concrete reason for surface area

**Explicit and observed, high confidence.** Fowler frequently challenges whether a feature or abstraction earns its cost. His proposal to remove default logging scopes weighs their broad cost against limited default value and explicitly accepts a compatibility break after making that tradeoff visible ([discussion](https://github.com/dotnet/aspnetcore/pull/44873#issuecomment-1646837983)).

The practical questions are:

- Why does this exist?
- Can it be deleted?
- Who needs this flexibility?
- Is the default cost justified?
- Does the abstraction hide lifetime, ownership, or allocation behavior?

### Design extension points for credible evolution

**Explicit, high confidence.** Minimal surface does not mean making evolution impossible. In review of a memory-pool factory, Fowler asked whether a fixed choice would later be regretted and suggested an options object when future options were credible ([one](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2090592724), [two](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2090593562)).

This produces a deliberate balance: reject speculative machinery, but preserve an evolution path where the domain already suggests one.

### Prefer idiomatic dependency injection patterns

**Explicit, high confidence.** [`DotNetCodingPatterns`](https://github.com/davidfowl/DotNetCodingPatterns/blob/73e22b74fed353827f6ff98ba55169ea1518f689/1.md) catalogs reusable patterns such as:

- generic types as factories;
- lazy service resolution;
- registering one implementation for multiple service interfaces;
- precompiled `ActivatorUtilities` factories;
- generic static caches where the runtime type system can replace a dictionary keyed by `Type`.

These patterns make lifetime and construction behavior explicit while reusing the framework's existing model.

### Keep application composition direct

**Observed, high confidence.** Current sample applications use route groups, small minimal-API handlers, DI-bound parameters, `TryParse` binding, records, and extension methods on framework builders. See [`ChatApi.cs`](https://github.com/davidfowl/aspire-ai-chat-demo/blob/8213c6aa064b035bf0bca28e31cef03e085e8a7d/ChatApi/ChatApi.cs).

### Treat telemetry names and identifiers as contracts

**Inferred, medium confidence.** Logging event names, event IDs, and meter names can be consumed externally. Related design reviews preserve existing identifiers and add new ones instead of silently repurposing old contracts. Fowler's approval of the resulting SignalR design was terse once area-owner review had resolved the details ([review](https://github.com/dotnet/aspnetcore/pull/64098#pullrequestreview-4333157603)).

## Async and concurrency

### Use async all the way

**Explicit, high confidence.** Fowler's [`AsyncGuidance.md`](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AsyncGuidance.md#warning-sync-over-async) catalogs multiple forms of sync-over-async and their failure modes. Avoid `.Result`, `.Wait()`, and wrappers that move blocking to another thread.

The guidance also distinguishes environments carefully. ASP.NET Core does not have the classic `SynchronizationContext` deadlock behavior, but blocking still risks thread-pool starvation and reduced scalability.

### Prefer `async` and `await` when behavior matters

**Explicit, high confidence.** Directly returning a task can be faster, but it changes when exceptions are observed and can lose useful async state-machine behavior. Fowler's guidance recommends `async` and `await` as the default when those semantics matter ([guidance](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AsyncGuidance.md#prefer-asyncawait-over-directly-returning-task)).

### Do not use `async void` for background work

**Explicit, high confidence.** The guidance describes `async void` in ASP.NET Core application code as unsafe because failures cannot be observed normally and may crash the process. Use a task-returning method and make the scheduling decision explicit ([guidance](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AsyncGuidance.md#async-void)).

Current sample code follows this pattern with an explicitly discarded `Task.Run` result and a comment explaining why work starts independently ([source](https://github.com/davidfowl/aspire-ai-chat-demo/blob/8213c6aa064b035bf0bca28e31cef03e085e8a7d/ChatApi/Services/ChatStreamingCoordinator.cs)).

### Respect request and service lifetimes

**Explicit, high confidence.** [`AspNetCoreGuidance.md`](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AspNetCoreGuidance.md) repeatedly warns against:

- storing `IHttpContextAccessor.HttpContext` in a field;
- using `HttpContext` concurrently;
- capturing `HttpContext` in background work;
- capturing a scoped service after its request scope ends.

Copy immutable values before crossing a lifetime boundary. When background work needs scoped services, create a fresh scope for that work.

### Run task continuations asynchronously

**Explicit, high confidence.** Always create `TaskCompletionSource<T>` with `TaskCreationOptions.RunContinuationsAsynchronously`. Otherwise, completing the source may run arbitrary continuation code inline, introducing reentrancy, deadlocks, thread-pool starvation, or state corruption ([guidance](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AsyncGuidance.md#always-create-taskcompletionsourcet-with-taskcreationoptionsruncontinuationsasynchronously)).

### Treat ambient state as shared mutable state

**Explicit and observed, high confidence.** Set `AsyncLocal`-backed state as late as possible and reason about execution-context flow. A runtime fix moved assignment immediately before application execution and added parallel regression coverage to prevent state leaking between invocations ([PR 54133](https://github.com/dotnet/runtime/pull/54133)).

### Make race fixes narrow and auditable

**Observed, high confidence.** A `PipeReader` cancellation race fix documented its low-risk scope, limited behavior changes to synchronous completion, and added assertions around existing invariants ([PR 61500](https://github.com/dotnet/runtime/pull/61500)).

## Performance style

### Measure and explain the mechanism

**Explicit and observed, high confidence.** Performance pull requests commonly provide:

- the affected scenario and hot path;
- before-and-after throughput, latency, or allocation data;
- the mechanism responsible for the change;
- fallback behavior and tradeoffs.

The `ValueTask` pooling change includes detailed crank output and allocation profiles ([PR 68457](https://github.com/dotnet/runtime/pull/68457)). A routing change shows the removed allocation directly in profiler output ([PR 49579](https://github.com/dotnet/aspnetcore/pull/49579)).

### Optimize the common path without losing the fallback

**Observed, high confidence.** Representative techniques include:

- stack-backed inline storage for the common number of routing candidates, with heap fallback for larger sets ([PR 49579](https://github.com/dotnet/aspnetcore/pull/49579));
- lazy list allocation only when SignalR stream IDs are present ([PR 41344](https://github.com/dotnet/aspnetcore/pull/41344));
- pooled async method builders on high-frequency pipeline operations ([PR 68457](https://github.com/dotnet/runtime/pull/68457));
- generic fast paths that avoid boxing primitive and enum log arguments ([PR 88560](https://github.com/dotnet/runtime/pull/88560));
- caching a stable decision once rather than repeating reference comparisons on every call ([PR 54555](https://github.com/dotnet/runtime/pull/54555)).

### Notice closures and hidden allocations

**Explicit, high confidence.** Fowler calls out closure allocation even when it may not block the change, and proposes passing explicit state to registrations instead ([review](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2051486539)).

The review posture matters: identify the real cost, state its likely significance, and avoid overstating a small optimization.

### Do not let speed silently change semantics

**Explicit, high confidence.** A faster task-return pattern is not automatically better if exception timing or stack behavior changes. Likewise, pooling and stack storage must preserve lifetime, ownership, cleanup, cancellation, and fallback behavior.

## Correctness and defensive reasoning

### Model ownership and lifetime explicitly

**Inferred, high confidence.** Many reviewed designs concern who owns a buffer, task, scope, context, or callback and when that resource stops being valid. Make disposal and completion observable where callers need to coordinate with them. In memory-pool review, Fowler suggested `IAsyncDisposable` and awaiting captured work rather than leaving its lifetime implicit ([review](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2051486406)).

### Preserve eager validation when useful

**Observed, medium confidence.** In an `IAsyncEnumerable<T>` implementation, a synchronous outer method wraps a local async iterator so argument validation happens when the method is called rather than on the first `MoveNextAsync` ([source](https://github.com/davidfowl/aspire-ai-chat-demo/blob/8213c6aa064b035bf0bca28e31cef03e085e8a7d/ChatApi/ChatHub.cs)).

### Expose uncertainty and risk

**Observed, medium confidence.** Pull request descriptions state compatibility risks, acknowledge when performance validation is incomplete, and explain why a patch is believed to be narrow. This lets reviewers evaluate evidence rather than infer confidence from polished prose.

## Comments and documentation

### Explain non-obvious behavior

**Explicit and observed, high confidence.** Comments should explain concurrency protocols, buffer recycling, lifetime rules, and surprising performance choices. Fowler asked for an explanatory comment when a nontrivial recycling design was otherwise difficult to reconstruct ([review](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2090594599)).

### Be candid about shortcuts

**Observed, high confidence.** Current sample code uses direct comments such as `This is inefficient` and leaves a focused TODO where configuration is intentionally hardcoded ([source](https://github.com/davidfowl/aspire-ai-chat-demo/blob/8213c6aa064b035bf0bca28e31cef03e085e8a7d/ChatApi/Services/ChatStreamingCoordinator.cs)).

Such comments are useful when they identify a real tradeoff or follow-up. Do not narrate obvious syntax.

## Tests

### Reproduce the actual failure mode

**Observed, high confidence.** Concurrency fixes include concurrent tests, not only sequential functional tests. The `AsyncLocal` race fix exercises 30 parallel invocations and checks that they complete without leaking state or hanging ([PR 54133](https://github.com/dotnet/runtime/pull/54133)).

### Assert performance-relevant behavior directly

**Observed, high confidence.** An allocation optimization verifies that the empty case reuses `Array.Empty<string>()` with identity comparison, not merely equivalent contents ([PR 41344](https://github.com/dotnet/aspnetcore/pull/41344)).

### Test shared infrastructure as shared

**Explicit, medium confidence.** Thread-safety and factory changes should prove behavior across the sharing boundary. A memory-pool review requested explicit coverage for shared meter and factory instances rather than assuming single-consumer behavior ([review](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2098969775)).

## Review posture

### Ask short questions that expose the design

**Observed, high confidence.** Characteristic review comments are concise:

- "If you could use a channel as an implementation detail this low in the stack, would you?" ([review](https://github.com/dotnet/runtime/pull/100316#discussion_r1545526384))
- "Is this Task.Yield but skipping the sync context?" ([review](https://github.com/dotnet/runtime/pull/87067#discussion_r1215280841))
- "Are we going to regret not making this configurable?" ([review](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2090592724))
- "Is that uncommon?" ([review](https://github.com/dotnet/aspnetcore/pull/61554#discussion_r2090595289))

The question usually targets the hidden cost, analogy, assumption, or future constraint. It gives the author room to supply context before prescribing a solution.

### Keep small concerns proportional

**Observed, high confidence.** Fowler often labels a possible allocation as potentially minor, uses a suggestion rather than a mandate, or reacts briefly when a compromise is uncomfortable. Correctness and architectural concerns receive deeper discussion; a small concern need not become a blocking essay.

### Approve tersely when area review is sufficient

**Observed, medium confidence.** Once design questions are resolved and trusted area owners have completed detailed review, approval may be as short as `LGTM` ([example](https://github.com/dotnet/aspnetcore/pull/64098#pullrequestreview-4333157603)).

## Reusable review questions

### Async and correctness

- Is any path blocking on asynchronous work?
- Can a continuation run inline while shared state is inconsistent?
- Does background work outlive its request, context, scope, or buffer?
- Can ambient state leak into another invocation?
- Does exception timing change if this task or iterator is returned directly?
- Does the test exercise the actual race concurrently?

### Performance

- Is this on a hot path, and is the impact measured?
- Is there a closure, boxing conversion, array, list, or task allocation hidden here?
- Can the common case use stack or pooled storage with a correct fallback?
- Can this decision be computed once rather than per call?
- Does the optimization preserve cancellation, cleanup, ownership, and observable behavior?

### API and design

- Why does this surface exist?
- Can it be deleted or expressed with an existing framework pattern?
- Is configurability needed now, or is there credible evidence it will be needed?
- Are lifetimes and ownership clear to callers?
- Does this preserve logging, telemetry, and wire compatibility?

### Documentation and tests

- Does the comment explain why the protocol works?
- Is an admitted shortcut bounded and actionable?
- Does the test prove the changed production path?
- Does an allocation claim have an allocation-sensitive assertion or measurement?

## Limits of this guide

- This is a representative sample, not an exhaustive analysis of Fowler's public work.
- Async and ASP.NET Core guidance has the strongest evidence because Fowler published explicit recommendations.
- Formatting evidence is mostly repository configuration and authored examples, not direct review mandates.
- Some recent pull requests use coding agents. Those changes are evidence of Fowler's review preferences only when his review is visible, not proof that he authored every line.
- Standard .NET repository conventions should not be mistaken for unique personal preferences.
- Preferences and framework guidance evolve. Recheck current sources before applying older advice as a universal rule.

## Primary evidence index

### Guidance and patterns

- [`AsyncGuidance.md`](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AsyncGuidance.md)
- [`AspNetCoreGuidance.md`](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/bcc7ca394eeb7113da6b3071f738ee5361b67db3/AspNetCoreGuidance.md)
- [`DotNetCodingPatterns`](https://github.com/davidfowl/DotNetCodingPatterns/blob/73e22b74fed353827f6ff98ba55169ea1518f689/1.md)

### Performance

- [Route matching allocations, `aspnetcore#49579`](https://github.com/dotnet/aspnetcore/pull/49579)
- [SignalR invocation allocations, `aspnetcore#41344`](https://github.com/dotnet/aspnetcore/pull/41344)
- [`ValueTask` pooling, `runtime#68457`](https://github.com/dotnet/runtime/pull/68457)
- [Logging boxing reduction, `runtime#88560`](https://github.com/dotnet/runtime/pull/88560)
- [`IsRootScope` fast path, `runtime#54555`](https://github.com/dotnet/runtime/pull/54555)

### Correctness and concurrency

- [`PipeReader` cancellation race, `runtime#61500`](https://github.com/dotnet/runtime/pull/61500)
- [`AsyncLocal` assignment race, `runtime#54133`](https://github.com/dotnet/runtime/pull/54133)

### Design and review

- [`Task.WhenEach`, `runtime#100316`](https://github.com/dotnet/runtime/pull/100316)
- [`ConfigureAwaitOptions`, `runtime#87067`](https://github.com/dotnet/runtime/pull/87067)
- [Default logging scopes, `aspnetcore#44873`](https://github.com/dotnet/aspnetcore/pull/44873)
- [`IMemoryPoolFactory`, `aspnetcore#61554`](https://github.com/dotnet/aspnetcore/pull/61554)

### Representative current code

- [`davidfowl/aspire-ai-chat-demo`](https://github.com/davidfowl/aspire-ai-chat-demo/tree/8213c6aa064b035bf0bca28e31cef03e085e8a7d)
- [`davidfowl/TcpEcho`](https://github.com/davidfowl/TcpEcho)
- [`davidfowl/BedrockFramework`](https://github.com/davidfowl/BedrockFramework)
