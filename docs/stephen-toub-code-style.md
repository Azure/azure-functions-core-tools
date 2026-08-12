# Stephen Toub Code and Review Style

This is a reusable, evidence-based reference for approximating the engineering and review preferences Stephen Toub (`@stephentoub`) demonstrates in public GitHub work. It is not an official style guide, and it should not override a repository's established conventions.

## Research scope and confidence

The research sampled roughly 50 review threads across more than 15 pull requests, plus authored code in:

- [`dotnet/runtime`](https://github.com/dotnet/runtime)
- [`dotnet/aspnetcore`](https://github.com/dotnet/aspnetcore)
- [`dotnet/machinelearning`](https://github.com/dotnet/machinelearning)
- [`dotnet/extensions`](https://github.com/dotnet/extensions)

The sample emphasizes recent .NET runtime and library work, with older ASP.NET Core and ML.NET changes included to check whether the same patterns recur. Recent PRs often contain Copilot-generated changes directed and reviewed by Toub. Those are useful evidence for his review preferences, but they are not treated as proof that he personally typed the resulting code.

Confidence labels used below:

- **Explicit**: Toub directly stated the preference in review.
- **Observed**: The pattern appears in code he authored.
- **Inferred**: the pattern recurs across evidence, but he did not state it as a general rule.

## Short reusable checklist

When writing or reviewing code in this style:

1. Preserve correctness first, including races, endianness, exception behavior, and unusual input sizes.
2. Require braces around `if`, `else`, loop, and similar bodies, including one-line bodies.
3. Follow .NET naming conventions: `PascalCase` members and types, `camelCase` locals and parameters, `_camelCase` instance fields, and `s_camelCase` static fields.
4. Keep control flow direct. Remove redundant locals, branches, conversions, wrappers, and duplicated implementations.
5. Avoid allocations and repeated work on hot paths. Prefer spans, pooling, static local functions, specialized primitives, and one-time decisions over per-call branching.
6. Do not trade maintainability for speculative or microscopic performance. Measure meaningful changes.
7. Minimize API and implementation surface. Ask whether a member, constructor, abstraction, special case, or custom helper can be deleted.
8. Use comments to explain rationale, invariants, tradeoffs, or surprising behavior. Keep comments synchronized with code.
9. Use `Debug.Assert` only for implementation bugs, not reachable caller mistakes.
10. Add focused regression tests, negative cases, boundary cases, and end-to-end coverage. Reuse theory data rather than duplicating it.

## Formatting and layout

### Braces

**Explicit, high confidence.** Always put control-flow bodies in braces, even when a body contains only one statement. He requested braces for every one-line `for`, `while`, `if`, and `else` body in a tensors PR ([review](https://github.com/dotnet/runtime/pull/124225#discussion_r2789496497)). He separately marked missing braces as review nits in stream implementations ([one](https://github.com/dotnet/runtime/pull/126669#discussion_r3055004253), [two](https://github.com/dotnet/runtime/pull/126669#discussion_r3055025064)).

### Indentation, spacing, and blank lines

**Observed, medium confidence.**

- Use four-space indentation and Allman braces, consistent with .NET repositories.
- Put spaces around binary operators and after commas.
- Separate logical blocks with blank lines rather than compressing unrelated operations.
- Prefer readable vertical layout for multi-part expressions. In authored ASP.NET Core code, a Boolean return is placed on following lines with `||` leading the continuation ([source](https://github.com/dotnet/aspnetcore/blob/9c7748cfad19930ed17b1d3c2dab9242b000d752/src/Shared/HttpRuleParser.cs#L185-L197)).
- Target-typed construction is welcome when the type is evident. He explicitly requested `new()` or `new(comparer)` instead of repeating generic types ([review](https://github.com/dotnet/runtime/pull/125001#discussion_r2867298405)).

### Parameter and argument wrapping

**Observed, low to medium confidence.** The reviewed sample did not contain a direct rule such as "one parameter per line." Do not invent one.

The stronger pattern is semantic:

- Keep short signatures and calls on one line when readable.
- Wrap complex expressions at logical operators or argument boundaries.
- Let the existing file's layout convention decide ambiguous cases.
- Prefer restructuring an unwieldy call or branch over applying arbitrary line wrapping.

For example, his code commonly formats a simple conditional return as:

```csharp
return condition ?
    whenTrue :
    whenFalse;
```

He also recommends structural rewrites when they make the decision space explicit, such as switching on a tuple instead of maintaining a chain of related conditions ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2723042764)).

### Expression bodies, ternaries, and compactness

**Observed, medium confidence.** Expression-bodied members and ternaries are appropriate for genuinely simple logic. Compactness is not a goal by itself. Keep braces and linear control flow when compression would hide mutations, exception paths, or invariants.

## Naming

### Core conventions

**Explicit or repeatedly observed, high confidence.**

| Element | Pattern | Examples |
|---|---|---|
| Types and methods | `PascalCase` | `CreateThread`, `TryDequeue`, `GetRank` |
| Locals and parameters | `camelCase` | `requiredLength`, `storedOutputDisabled` |
| Private instance fields | `_camelCase` | `_lock`, `_queue`, `_availableThreads` |
| Static fields | `s_camelCase` | Explicitly stated in [this review](https://github.com/dotnet/runtime/pull/123041#discussion_r2723056765) |
| Constants | `PascalCase` | `MaxAttempts`, `NumInputs`, `Seed` |

### Name for meaning, not mechanics

**Observed, medium confidence.**

- Prefer a name that describes the represented concept, such as `dataByteLength` rather than the less precise `totalByteLength`.
- Introduce a clear local when it removes repeated, hard-to-read pointer arithmetic, as in `dataPtr` ([review thread](https://github.com/dotnet/runtime/pull/126201#discussion_r3004029923)).
- Remove a local when it merely renames a simple expression or makes captures more expensive. He said, "We don't need a local for `flush`" ([review](https://github.com/dotnet/runtime/pull/126669#discussion_r3055040041)) and rejected captured locals when recomputing cheap offsets was less expensive ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2677690258)).
- Keep suffix capitalization consistent, such as `u` versus `UL` ([review](https://github.com/dotnet/runtime/pull/86655#discussion_r1235944887)).

## Structure and control flow

### Prefer the simplest complete design

**Explicit, high confidence.** Repeated review questions are variants of:

- Why does this exist?
- Can this be deleted?
- Is the special case worth its code?
- Is there already a framework primitive for this?

Examples include deleting a rare wildcard special case ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2715182556)), challenging more than 1,000 lines of duplicated source-generator models ([review](https://github.com/dotnet/runtime/pull/125438#discussion_r3032864253)), and questioning constructors and speculative API surface in `Microsoft.Extensions.AI` ([PR](https://github.com/dotnet/extensions/pull/7420)).

### Make decisions once

**Explicit, high confidence.** Avoid a captured flag and branch on every delegate invocation. Select the correct delegate once and keep its hot path branch-free ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2677813855)). Likewise, avoid two delegate calls where one predicate can perform the full operation ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2677513731)).

### Prefer proven framework primitives

**Observed, high confidence.**

- Replace bespoke awaiters with `ConfigureAwaitOptions` when the framework provides the needed semantics ([ASP.NET Core PR 48722](https://github.com/dotnet/aspnetcore/pull/48722)).
- Use `SearchValues<T>` for repeated matching instead of ad hoc scans ([character review](https://github.com/dotnet/runtime/pull/123041#discussion_r2677524711), [string review](https://github.com/dotnet/runtime/pull/123922#discussion_r2766266624)).
- Use span slicing instead of allocating substrings ([review](https://github.com/dotnet/runtime/pull/123922#discussion_r2766267914)).
- Prefer an idiomatic scope or `try/finally` when it makes once-only resource release obvious ([review](https://github.com/dotnet/runtime/pull/127462#discussion_r3153587521)).

### Avoid exceptions for expected control flow

**Observed, high confidence.** An authored ML.NET change replaced `BlockingCollection.Take` plus `InvalidOperationException` handling with `TryTake` and explicit result handling ([PR 2138](https://github.com/dotnet/machinelearning/pull/2138)).

## Performance style

### Measure and explain

**Observed, high confidence.** Authored performance PRs commonly include:

- the reason for the change;
- benchmark source or enough detail to reproduce it;
- before-and-after timing;
- allocation measurements;
- tradeoffs and affected scenarios.

Representative examples include HTTP date parsing ([ASP.NET Core PR 47040](https://github.com/dotnet/aspnetcore/pull/47040)), renderer synchronization ([ASP.NET Core PR 48720](https://github.com/dotnet/aspnetcore/pull/48720)), and byte-pair encoding ([ML.NET PR 7017](https://github.com/dotnet/machinelearning/pull/7017)).

He asks whether added overhead is measurable, including an extra syscall ([review](https://github.com/dotnet/runtime/pull/125872#discussion_r2969520733)) and an extra allocated lock object ([review](https://github.com/dotnet/runtime/pull/104575#discussion_r1669329778)).

### Allocation avoidance

**Explicit and observed, high confidence.**

- Prefer one contiguous native allocation over an intermediate managed collection plus many native allocations. Compute the size, allocate once, then populate ([review](https://github.com/dotnet/runtime/pull/126201#discussion_r3001397329)).
- Avoid allocating an intermediate array when the source dictionary can be consumed directly ([review](https://github.com/dotnet/runtime/pull/126201#discussion_r3001389092)).
- Use stack allocation for small bounded data and `ArrayPool<T>` for larger data, then slice to the required length ([ML.NET source](https://github.com/dotnet/machinelearning/blob/b50995c28aec951ecb047ecce0dc88a87d856f0d/src/Microsoft.ML.Tokenizers/Utils/BytePairEncoder.cs#L23-L47)).
- Use `static` local functions plus explicit tuple state to avoid closure allocation ([ASP.NET Core source](https://github.com/dotnet/aspnetcore/blob/8282661cf4e953c6ff5b9b4f1cf7e5d59b1613a7/src/Components/Components/src/Rendering/RendererSynchronizationContext.cs#L29-L78)).
- Remove unnecessary conversions such as explicit `.AsSpan()` when implicit conversion already produces the correct API shape ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2677810133)).

### Performance does not excuse unjustified complexity

**Explicit, high confidence.** He distinguishes meaningful improvements from unexplained workarounds and speculative churn. In a JSON change, he preferred reverting a `ReadOnlySpan` to `Span` workaround until the underlying JIT issue was understood ([review](https://github.com/dotnet/runtime/pull/115702#discussion_r2112442203), [clarification](https://github.com/dotnet/runtime/pull/115702#discussion_r2112480767)). In a source-generator design discussion, he objected to the maintenance cost of parallel models even though the proposal targeted performance ([review](https://github.com/dotnet/runtime/pull/125438#discussion_r3032864253)).

The practical rule is: optimize important paths, prove the benefit, and account for maintenance cost, code size, risk, and less common scenarios.

## Correctness and defensive reasoning

### Review the states between the obvious states

**Explicit, high confidence.** His reviews repeatedly probe what happens:

- when a collection changes between two passes ([review](https://github.com/dotnet/runtime/pull/126201#discussion_r3004097672));
- when more mounts appear between sizing and retrieval calls ([review](https://github.com/dotnet/runtime/pull/122637#discussion_r2631029150));
- when `errno` might be changed during cleanup ([review](https://github.com/dotnet/runtime/pull/122637#discussion_r2631034793));
- when a continuation is attached before a result is set ([review](https://github.com/dotnet/runtime/pull/127256#discussion_r3154713944));
- on both little-endian and big-endian systems ([review](https://github.com/dotnet/runtime/pull/126259#discussion_r3005604176));
- when multiple inputs are invalid and validation order changes the observable exception.

### Use checked arithmetic for allocation sizes

**Observed, high confidence.** Allocation size computation should use checked arithmetic so overflow cannot silently produce an undersized buffer. This was required in the native `argv` and `envp` work ([review](https://github.com/dotnet/runtime/pull/126201#discussion_r3003144092)).

### Asserts are for implementation bugs

**Explicit, high confidence.** `Debug.Assert` should represent a condition reachable only through an implementation defect. It should not diagnose invalid use of public surface area ([review](https://github.com/dotnet/runtime/pull/127256#discussion_r3154225398)). Use argument validation or an exception when external inputs can violate the condition.

### Preserve externally observable behavior

**Inferred, high confidence.** Refactoring must account for exception type, exception precedence, ordering, synchronization, and generated-code behavior, not just successful results. When multiple implementations exist, such as interpreted, compiled, and source-generated regex paths, update and test them consistently.

## Async and concurrency

### Treat memory ordering as part of the algorithm

**Explicit and observed, high confidence.**

- Question every volatile read, exchange, and compare-exchange in terms of publication and allowed interleavings.
- Use `Interlocked.Exchange` rather than a more complicated read and compare-exchange sequence when unconditional overwrite is the real operation ([review](https://github.com/dotnet/runtime/pull/127256#discussion_r3154758040)).
- Do not retain `volatile` by superstition. Toub's broad cleanup explains when publication and data-dependent reads already supply the required ordering, while retaining `volatile` where it gates other shared state or participates in multi-writer protocols ([PR 125274](https://github.com/dotnet/runtime/pull/125274)).
- Prefer `RunContinuationsAsynchronously` or equivalent semantics when inline continuations could create reentrancy or deadlock hazards.

### Optimize async only with semantics intact

**Observed, high confidence.** His renderer synchronization change used `AsyncTaskMethodBuilder` and static local functions to reduce allocations, but it also included a substantial comment explaining deadlock and queue-ordering constraints ([source](https://github.com/dotnet/aspnetcore/blob/8282661cf4e953c6ff5b9b4f1cf7e5d59b1613a7/src/Components/Components/src/Rendering/RendererSynchronizationContext.cs#L29-L78)).

## Comments and documentation

### Explain why

**Observed, high confidence.** Good comments capture:

- a non-obvious invariant;
- why a tempting alternative is wrong;
- benchmark-derived thresholds;
- compatibility or platform constraints;
- the reason for a workaround, ideally with an issue link.

For example, a regex unrolling comment was changed from calling a limit arbitrary to explaining the measured scalar-versus-vector tradeoff ([PR 126092](https://github.com/dotnet/runtime/pull/126092)).

### Do not preserve noise

**Explicit, high confidence.** A comment or marker that is not validated, is incomplete, or misleadingly suggests stronger guarantees is harmful. He rejected widespread `unchecked` annotations as unmaintainable noise because they could not comprehensively document every overflow assumption ([review](https://github.com/dotnet/runtime/pull/125799#discussion_r2963483752)).

### Keep comments synchronized

**Explicit, high confidence.** Remove stale comments and TODOs, or explain their disposition. He flagged a wildcard comment that no longer described the code ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2723047569)) and questioned abandoned commented-out code and TODOs ([review](https://github.com/dotnet/runtime/pull/86655#discussion_r1236195471)).

## Tests

### Test the changed path

**Explicit, high confidence.** A helper test is insufficient when the production change is in an end-to-end path. He requested tests that created actual files, enumerated them, and validated results rather than only testing a matcher helper ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2722815409)).

### Cover positive, negative, boundary, and platform cases

**Explicit, high confidence.**

- Add cases proving an optimization does not trigger when it should not ([review](https://github.com/dotnet/runtime/pull/126179#discussion_r3001049900)).
- Exercise many shapes and sizes, preferably with theories to reduce duplication ([review](https://github.com/dotnet/runtime/pull/124225#discussion_r2789505468)).
- Consider architecture width, endianness, cancellation, completion ordering, empty values, and concurrent execution.
- Avoid global observations in tests when unrelated tests can run concurrently. Serialize or isolate the process when necessary ([review](https://github.com/dotnet/runtime/pull/125872#discussion_r2969519660)).

### Reuse test data

**Explicit, high confidence.** Reference existing theory data instead of copying it into a new test suite ([review](https://github.com/dotnet/runtime/pull/123041#discussion_r2722958536)). Place new tests alongside existing tests for the same operation unless repository organization clearly calls for another file ([review](https://github.com/dotnet/runtime/pull/126259#discussion_r3005128623)).

## Review posture

### Ask for rationale before prescribing

**Inferred, high confidence.** The dominant review form is a short question:

- "Why is this necessary?"
- "Is it measurable?"
- "What happens if the state changes here?"
- "Can we delete this?"
- "Does this test actually exercise the change?"

This style gives the author room to reveal context, while making the expected evidence clear. When the answer exposes a flaw, he often supplies a concrete, minimal alternative.

### Apply feedback consistently

**Explicit, medium confidence.** When a pattern is wrong in one location, inspect equivalent locations rather than fixing only the commented line. His comments often include "same elsewhere in the PR," and he requests matching fixes across related methods ([example](https://github.com/dotnet/runtime/pull/124225#discussion_r2789496497)).

### Distinguish blocking issues from nits

**Observed, medium confidence.** Formatting comments are often labeled `nit`; correctness, performance regressions, races, and API complexity are discussed in terms of consequences and evidence.

## Reusable review questions

### Correctness

- What happens at zero, one, maximum size, overflow, and partial completion?
- Can the source mutate between sizing and consumption?
- Does cleanup preserve the original error?
- Are all architectures, endiannesses, and supported implementations equivalent?
- Did observable exception behavior change?

### Performance

- Is the affected path hot?
- What allocation, call, branch, copy, syscall, or bounds check was added?
- Can the decision be made once instead of per invocation?
- Is there a span, pooled buffer, `SearchValues<T>`, or existing framework primitive that fits?
- Is the improvement measured, and does it justify complexity and risk?

### Design

- Can this member, constructor, type, special case, or wrapper be deleted?
- Does this duplicate an existing model or implementation?
- Is the API based on a demonstrated use case?
- Will parallel implementations remain synchronized?

### Tests and maintainability

- Does the test exercise the actual changed path?
- Are negative and boundary cases covered?
- Can existing theory data be reused?
- Is the test deterministic and isolated from concurrent activity?
- Does every comment still explain the current code?

## Limits of this guide

- This is a representative sample, not an exhaustive analysis of thousands of contributions.
- Formatting evidence is strongest for braces and naming. There was no explicit, broadly stated parameter-wrapping rule in the sampled reviews.
- Repository conventions take precedence. Some observed choices reflect local .NET repository rules rather than a unique personal preference.
- Copilot-directed PRs reveal review and design preferences, but should not be used alone to attribute exact typing or formatting choices to Toub.
- Preferences can evolve with new C# and .NET features. Recheck recent work before treating a low-confidence observation as a rule.

## Primary evidence index

### Review-heavy runtime PRs

- [File-system pattern specialization, PR 123041](https://github.com/dotnet/runtime/pull/123041)
- [Native process argument and environment allocation, PR 126201](https://github.com/dotnet/runtime/pull/126201)
- [BigInteger native-width rewrite, PR 125799](https://github.com/dotnet/runtime/pull/125799)
- [ManualResetValueTaskSource and AsyncOperation races, PR 127256](https://github.com/dotnet/runtime/pull/127256)
- [Stream wrappers, PR 126669](https://github.com/dotnet/runtime/pull/126669)
- [DriveInfo thread safety, PR 122637](https://github.com/dotnet/runtime/pull/122637)
- [BigInteger platform semantics, PR 126259](https://github.com/dotnet/runtime/pull/126259)

### Authored code outside runtime

- [ASP.NET Core HTTP date parsing, PR 47040](https://github.com/dotnet/aspnetcore/pull/47040)
- [ASP.NET Core renderer synchronization, PR 48720](https://github.com/dotnet/aspnetcore/pull/48720)
- [ASP.NET Core custom awaiter cleanup, PR 48722](https://github.com/dotnet/aspnetcore/pull/48722)
- [ASP.NET Core header splitting, PR 54808](https://github.com/dotnet/aspnetcore/pull/54808)
- [ML.NET thread reuse, PR 2152](https://github.com/dotnet/machinelearning/pull/2152)
- [ML.NET exception-free batch retrieval, PR 2138](https://github.com/dotnet/machinelearning/pull/2138)
- [ML.NET byte-pair encoding performance, PR 7017](https://github.com/dotnet/machinelearning/pull/7017)
