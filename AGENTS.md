# Agent Instructions — Azure Functions Core Tools (vNext)

Guidance for AI coding agents working in this repository. Keep changes aligned with the
patterns below. The goal of this branch line is a cleaner, testable, DI-first CLI — the
current `main` branch contains design choices we are explicitly moving away from.

## Context

- **Repo:** Azure Functions Core Tools (`func`), a cross-platform .NET CLI.
- **vNext intent:** Restructure the CLI around dependency injection, small focused
  components, and high testability. Treat patterns in `main` as legacy unless they are
  deliberately being kept.
- **Languages:** Primarily C# / .NET. MSBuild props files control versioning.

## Core Design Principles

### DI-first

- All composition goes through `Microsoft.Extensions.DependencyInjection`. Build the
  service collection at startup; do not introduce new service locators or ambient
  containers (the legacy Autofac container in `main` is being phased out — do not
  expand it).
- Inject dependencies via **constructor injection**. No property injection, no
  service-locator lookups inside methods, no `IServiceProvider.GetService` calls
  outside composition root / framework integration points.
- Prefer `IOptions<T>` / `IOptionsMonitor<T>` for configuration. Bind from
  `IConfiguration` at the composition root.
- Pick the narrowest sensible lifetime: `Singleton` for stateless/shared,
  `Scoped` for per-command/per-request work, `Transient` only when justified.

### Avoid static classes

- Do **not** introduce new `static` classes for behavior. Static state and static
  helpers are the main reason `main` is hard to test.
- Static is acceptable only for:
  - Pure, side-effect-free utility functions (e.g. string parsing) with no
    dependencies that would otherwise be injected.
  - Constants and `extension` method holders.
  - `Program.Main`.
- Anything that touches I/O, the filesystem, the environment, the network, the clock,
  or process state must be a regular class behind an interface (or abstract base) so
  it can be substituted in tests.

### Testability is non-negotiable

- Every new class must be reachable from a test — through an interface, a concrete
  type with injectable dependencies, an `IOptions<T>`, or similar. If you can't see
  how to test it, the design is wrong; restructure before adding more code.
- Wrap non-deterministic dependencies (clock, environment variables, filesystem,
  process launching, HTTP) behind interfaces. Do not call `Environment.*`,
  `File.*`, `DateTime.Now`, `Process.Start`, etc. directly from business logic.
- Keep methods focused and side-effect-light so they can be unit-tested without
  spinning up the full CLI.

### Visibility

- New types are **`internal`** by default. Only make a type `public` when there is a
  clear, documented reason (e.g. it is part of a published API surface). Prefer
  `InternalsVisibleTo` for test access over widening visibility.
- Same rule for members: prefer `private` / `internal`, widen only when needed.

## Patterns to Prefer

- Small, single-responsibility classes over "manager" / "helper" god-objects.
- Constructor parameter validation via `ArgumentNullException.ThrowIfNull` (or guard
  clauses) on injected dependencies.
- `async`/`await` for all I/O. Propagate `CancellationToken` through the call chain.
- Strongly-typed options classes bound from configuration; validate them at startup.
- Structured logging via `Microsoft.Extensions.Logging` (`ILogger<T>`), not
  `Console.WriteLine` for diagnostics.

## Patterns to Avoid (legacy in `main`)

- New `static` helper classes / static mutable state.
- Direct use of the Autofac container or other service locators in business logic.
- `new`-ing up dependencies that have I/O or process side effects inside business
  classes.
- Public types/members that exist only because tests needed access — use
  `InternalsVisibleTo` instead.
- Reading `Environment.GetEnvironmentVariable` / `Environment.CurrentDirectory` /
  `File.*` directly from logic that should be unit-testable.

## Coding Style and Changes

- Match the style of the file you're editing.
- Keep changes minimal and surgical — resolve the problem cleanly without sweeping
  rewrites of unrelated code.
- User-visible behavior changes must be called out explicitly (in PR description /
  release notes). Don't slip them in.
- Only edit files necessary for the change. Do **not** run `dotnet format` across
  unrelated files or make drive-by formatting changes.
- Prefer **file-scoped namespaces** for new files.
- Don't leave unused `using` directives.
- Follow the existing `stylecop.json` / analyzer rules. Don't disable analyzers to
  silence warnings — fix the underlying issue.

## Building and Testing

- Build: `dotnet build` from repo root.
- Test: `dotnet test`. Run a quick build/test pass after non-trivial changes.
- Non-trivial changes should include test changes. If a fix is hard to test, that
  is usually a sign the design needs adjusting (see *Testability* above).
- When adding a `[Fact(Skip = "…")]`, point `Skip` at a specific issue link.
- Don't introduce new test frameworks or runners; use what the test projects already
  use.

## Documentation

- When opening a PR, scan the repo for docs (`README.md`, files under `docs/`,
  `CONTRIBUTING.md`) that may need updating to reflect the change.
- Don't manually edit generated docs. If a file is generated, update its source.

## Versioning & Release

- Versioning and release notes follow `.github/instructions/release.instructions.md`.
  Do not duplicate that guidance here.

## Reference

Reusable playbooks live in `.github/skills/<skill-name>/SKILL.md` (per the
[Agent Skills](https://agentskills.io) spec). Claude Code picks them up via
symlinks under `.claude/skills/`. When working on .NET code in this repo,
consult:

- `.github/skills/dotnet-best-practices/` — coding patterns to apply.
- `.github/skills/dotnet-design-pattern-review/` — review checklist.

Where any skill and this file disagree, **AGENTS.md wins** for this repository.
