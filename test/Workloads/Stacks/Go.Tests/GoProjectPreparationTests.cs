// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Go.Tests;

public sealed class GoProjectPreparationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task PrepareAsync_InvalidDirectory_RejectsBeforeToolExecution(string? path)
    {
        ControlledPreparation preparation = new();

        Func<Task> action = () => preparation.PrepareAsync(path!, false, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        preparation.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_Skip_DoesNotExecuteTool()
    {
        ControlledPreparation preparation = new();

        GoProjectPreparationResult result = await preparation.PrepareAsync("project", true, CancellationToken.None);

        result.Skipped.Should().BeTrue();
        result.ExitCode.Should().BeNull();
        preparation.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(17, "module resolution failed")]
    [InlineData(-1, "Go executable could not be started")]
    public async Task PrepareAsync_ToolOutcome_PreservesExitAndStderr(int exitCode, string stderr)
    {
        ControlledPreparation preparation = new() { Result = (exitCode, stderr) };
        using CancellationTokenSource cancellation = new();

        GoProjectPreparationResult result = await preparation.PrepareAsync("project", false, cancellation.Token);

        result.Skipped.Should().BeFalse();
        result.ExitCode.Should().Be(exitCode);
        result.StandardError.Should().Be(stderr);
        preparation.Directory.Should().Be("project");
        preparation.Token.Should().Be(cancellation.Token);
        preparation.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_PreCancelled_RejectsWithoutToolExecution(bool skip)
    {
        ControlledPreparation preparation = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> action = () => preparation.PrepareAsync("project", skip, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        preparation.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PrepareAsync_CancelledLegacyCallback_DoesNotReturnSuccessShapedResult()
    {
        using CancellationTokenSource cancellation = new();
        ControlledPreparation preparation = new() { DuringExecution = cancellation.Cancel };

        Func<Task> action = () => preparation.PrepareAsync("project", false, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class ControlledPreparation : GoProjectPreparation
    {
        public int Calls { get; private set; }
        public string? Directory { get; private set; }
        public CancellationToken Token { get; private set; }
        public (int ExitCode, string Stderr) Result { get; init; }
        public Action? DuringExecution { get; init; }

        public override Task<(int ExitCode, string Stderr)> RunGoModTidyAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            Directory = workingDirectory;
            Token = cancellationToken;
            DuringExecution?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
