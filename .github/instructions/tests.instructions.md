---
applyTo: "test/**/*.cs"
description: "Test conventions for this repo: xUnit + NSubstitute, AAA, CLI-friendly fakes."
---

# Test Instructions

These rules apply to every C# file under `test/`. They complement
`dotnet.instructions.md`. If anything here disagrees with `AGENTS.md`,
**`AGENTS.md` wins**.

## Stack

- **xUnit** as the test framework. **NSubstitute** for mocking. Use
  **AwesomeAssertions** for fluent assertions (the maintained fork of
  FluentAssertions, which moved to a paid licence). No MSTest, no Moq, no
  FluentAssertions.
- Common test packages are enforced by `test/Directory.Build.props`. Don't
  re-declare `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
  or `NSubstitute` in individual test csprojs.

## Structure

- Follow **AAA** (Arrange / Act / Assert), one logical action per test.
- Cover both success and failure paths. Include argument-validation tests for
  public/`internal` APIs.
- Tests live in `test/<Project>.Tests/` mirroring the source layout
  (`test/Func.Tests/Commands/...` for `src/Func/Commands/...`).

## Substituting CLI seams

- Substitute `IInteractionService` to assert what the CLI rendered, without a
  real terminal.
- Substitute the dotnet CLI wrapper using the **`FakeDotnetCliRunner`** pattern
  (see existing dotnet workload tests for reference). Don't shell out to a real
  `dotnet` process from unit tests.
- Substitute filesystem, environment, clock, and process abstractions; never
  rely on the real machine state in unit tests.

## Async tests

- Use `async Task` test methods (not `async void`).
- Pass a `CancellationToken` through to the system under test where the
  production API takes one. `TestContext.Current.CancellationToken` is fine
  when available; otherwise a fresh `CancellationTokenSource`.

## Naming

- Test methods: `MethodUnderTest_StateUnderTest_ExpectedBehaviour` or a clear
  sentence form. Match the surrounding file.
- Fixture / collection names should reflect the shared resource, not the test
  framework primitive.
