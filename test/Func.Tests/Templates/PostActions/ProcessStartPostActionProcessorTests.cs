// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Common.Processes;
using Azure.Functions.Cli.Templates.PostActions;
using Microsoft.TemplateEngine.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Azure.Functions.Cli.Tests.Templates.PostActions;

public sealed class ProcessStartPostActionProcessorTests
{
    private static readonly string _outputPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "func-run-script-output"));

    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly TestInteractionService _interaction = new();
    private readonly ProcessStartPostActionProcessor _processor;

    public ProcessStartPostActionProcessorTests()
    {
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(0, string.Empty, string.Empty, TimedOut: false, ExecutableNotFound: false));
        _processor = new(_runner, _fileSystem, _interaction);
    }

    [Fact]
    public void Id_IsRunScriptActionId()
    {
        _processor.Id.Should().Be(new Guid("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2"));
        PostActionDispatcher.RunScriptActionId.Should().Be(_processor.Id);
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        Action noRunner = () => _ = new ProcessStartPostActionProcessor(null!, _fileSystem, _interaction);
        Action noFileSystem = () => _ = new ProcessStartPostActionProcessor(_runner, null!, _interaction);
        Action noInteraction = () => _ = new ProcessStartPostActionProcessor(_runner, _fileSystem, null!);

        noRunner.Should().Throw<ArgumentNullException>().WithParameterName("processRunner");
        noFileSystem.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
        noInteraction.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ProcessAsync_MissingExecutable_ReturnsFalseWithoutRunning(string? executable)
    {
        Dictionary<string, string> args = [];
        if (executable is not null)
        {
            args["executable"] = executable;
        }

        bool result = await _processor.ProcessAsync(CreateContext(args), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _interaction.Lines.Should().Contain(
            "ERROR: Failed to run the command: argument 'executable' is missing in post action configuration.");
        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_ExecutableInOutputDirectory_RunsItFromOutputDirectory()
    {
        string local = Path.Combine(_outputPath, "setup.cmd");
        _fileSystem.DirectoryExists(_outputPath).Returns(true);
        _fileSystem.FileExists(local).Returns(true);

        bool result = await _processor.ProcessAsync(
            CreateContext(new() { ["executable"] = "setup.cmd", ["args"] = "--flag \"quoted value\"" }),
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        await _runner.Received(1).RunAsync(
            Arg.Is<ProcessRunRequest>(request =>
                request.FileName == local
                && request.Arguments.Count == 0
                && request.ArgumentString == "--flag \"quoted value\""
                && request.WorkingDirectory == _outputPath
                && request.Timeout == Timeout.InfiniteTimeSpan
                && request.RedirectStandardOutput
                && request.RedirectStandardError),
            TestContext.Current.CancellationToken);
        _interaction.Lines.Should().ContainInOrder(
            "Running command 'setup.cmd --flag \"quoted value\"'...", "SUCCESS: Command succeeded.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ProcessAsync_ExecutableNotInOutputDirectory_UsesBareNameForPathLookup(bool directoryExists, bool fileExists)
    {
        _fileSystem.DirectoryExists(_outputPath).Returns(directoryExists);
        _fileSystem.FileExists(Arg.Any<string>()).Returns(fileExists);

        bool result = await _processor.ProcessAsync(CreateContext(new() { ["executable"] = "git" }), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        await _runner.Received(1).RunAsync(
            Arg.Is<ProcessRunRequest>(request => request.FileName == "git" && request.ArgumentString == null),
            TestContext.Current.CancellationToken);
        _interaction.Lines.Should().Contain("Running command 'git'...");
    }

    [Fact]
    public async Task ProcessAsync_RelativeOutputPath_UsesFullPathAsWorkingDirectory()
    {
        bool result = await _processor.ProcessAsync(
            CreateContext(new() { ["executable"] = "git" }, outputBasePath: "relative-output"),
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        await _runner.Received(1).RunAsync(
            Arg.Is<ProcessRunRequest>(request => request.WorkingDirectory == Path.GetFullPath("relative-output")),
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("false", "FALSE", false, false)]
    [InlineData("true", "no", true, true)]
    [InlineData(null, "False", true, false)]
    public async Task ProcessAsync_RedirectArguments_OptOutOnlyWithFalse(
        string? stdout, string? stderr, bool expectedStdout, bool expectedStderr)
    {
        Dictionary<string, string> args = new() { ["executable"] = "tool" };
        if (stdout is not null)
        {
            args["redirectStandardOutput"] = stdout;
        }

        if (stderr is not null)
        {
            args["redirectStandardError"] = stderr;
        }

        await _processor.ProcessAsync(CreateContext(args), TestContext.Current.CancellationToken);

        await _runner.Received(1).RunAsync(
            Arg.Is<ProcessRunRequest>(request =>
                request.RedirectStandardOutput == expectedStdout && request.RedirectStandardError == expectedStderr),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_NonzeroExit_ReturnsFalseAndWritesCapturedOutput()
    {
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(3, "some output", string.Empty, TimedOut: false, ExecutableNotFound: false));

        bool result = await _processor.ProcessAsync(CreateContext(new() { ["executable"] = "tool" }), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _interaction.Lines.Should().ContainInOrder(
            "ERROR: Command failed with exit code 3.", "Output from command:", "StdOut:", "some output", "StdErr:", "(empty)");
        _interaction.Lines.Should().NotContain("SUCCESS: Command succeeded.");
    }

    [Fact]
    public async Task ProcessAsync_NonzeroExitWithoutRedirection_WritesNoCapturedOutput()
    {
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(1, string.Empty, string.Empty, TimedOut: false, ExecutableNotFound: false));

        bool result = await _processor.ProcessAsync(
            CreateContext(new()
            {
                ["executable"] = "tool",
                ["redirectStandardOutput"] = "false",
                ["redirectStandardError"] = "false",
            }),
            TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _interaction.Lines.Should().Contain("ERROR: Command failed with exit code 1.");
        _interaction.Lines.Should().NotContain("Output from command:");
    }

    [Fact]
    public async Task ProcessAsync_ExecutableNotFound_ReturnsFalse()
    {
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessOutcome(null, string.Empty, string.Empty, TimedOut: false, ExecutableNotFound: true));

        bool result = await _processor.ProcessAsync(CreateContext(new() { ["executable"] = "missing" }), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _interaction.Lines.Should().ContainInOrder(
            "ERROR: Command failed.", $"The executable 'missing' was not found in '{_outputPath}' or on PATH.");
    }

    [Fact]
    public async Task ProcessAsync_StartFails_ReturnsFalse()
    {
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new Win32Exception(5, "Access is denied."));

        bool result = await _processor.ProcessAsync(CreateContext(new() { ["executable"] = "tool" }), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _interaction.Lines.Should().ContainInOrder("ERROR: Command failed.", "Access is denied.");
    }

    [Fact]
    public async Task ProcessAsync_CancelledWhileRunning_Throws()
    {
        using CancellationTokenSource cts = new();
        _runner.RunAsync(Arg.Any<ProcessRunRequest>(), cts.Token).ThrowsAsync(new OperationCanceledException(cts.Token));

        Func<Task> act = () => _processor.ProcessAsync(CreateContext(new() { ["executable"] = "tool" }), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessAsync_AlreadyCancelled_ThrowsWithoutRunning()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => _processor.ProcessAsync(CreateContext(new() { ["executable"] = "tool" }), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, TestContext.Current.CancellationToken);
    }

    private static EnginePostActionContext CreateContext(Dictionary<string, string> args, string? outputBasePath = null)
    {
        IPostAction action = Substitute.For<IPostAction>();
        action.ActionId.Returns(ProcessStartPostActionProcessor.ActionProcessorId);
        action.Args.Returns(args);
        return new(
            Substitute.For<IEngineEnvironmentSettings>(),
            action,
            Substitute.For<ICreationEffects>(),
            Substitute.For<ICreationResult>(),
            outputBasePath ?? _outputPath,
            Substitute.For<ITemplateInfo>());
    }
}
