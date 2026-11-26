// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PostActions;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.PostActions;

public sealed class PostActionDispatcherRunScriptTests : IDisposable
{
    private static readonly Guid _otherActionId = Guid.NewGuid();

    private readonly string _root;
    private readonly ConsentInteractionService _interaction = new();
    private readonly ICorePostActionProcessor _runScriptProcessor = Substitute.For<ICorePostActionProcessor>();
    private readonly ICorePostActionProcessor _otherProcessor = Substitute.For<ICorePostActionProcessor>();
    private readonly ITemplateInfo _template = Substitute.For<ITemplateInfo>();
    private readonly Templater _templater;
    private readonly PostActionDispatcher _dispatcher;

    public PostActionDispatcherRunScriptTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-run-script-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _runScriptProcessor.Id.Returns(PostActionDispatcher.RunScriptActionId);
        _runScriptProcessor.ProcessAsync(Arg.Any<EnginePostActionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        _otherProcessor.Id.Returns(_otherActionId);
        _otherProcessor.ProcessAsync(Arg.Any<EnginePostActionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        _templater = new(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            Path.Combine(_root, "hive"),
            [CreateComponent(_runScriptProcessor), CreateComponent(_otherProcessor)]);
        _dispatcher = new(_interaction);
    }

    public void Dispose()
    {
        _templater.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp directory doesn't affect test results.
        }
    }

    [Fact]
    public async Task ProcessAsync_AllowYes_RunsScriptWithoutPrompt()
    {
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.Yes);

        status.Should().Be(PostActionExecutionStatus.Success);
        await _runScriptProcessor.ReceivedWithAnyArgs(1).ProcessAsync(default!, TestContext.Current.CancellationToken);
        _interaction.Lines.Should().NotContain(line => line.StartsWith("CONFIRM:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_AllowNo_DeclinesWithCommandAndInstructions()
    {
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.No);

        status.Should().Be(PostActionExecutionStatus.Cancelled);
        await _runScriptProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        _interaction.Lines.Should().ContainInOrder(
            "WARNING: Execution of 'Run script' post action is not allowed.",
            "  Restore tools",
            "Manual instructions: Run setup yourself.",
            "Actual command: setup.cmd --restore");
        _interaction.Lines.Should().NotContain(line => line.StartsWith("CONFIRM:", StringComparison.Ordinal));
        _interaction.Lines.Should().NotContain("ERROR: Post action failed.");
    }

    [Fact]
    public async Task ProcessAsync_PromptConfirmed_ShowsCommandThenRunsScript()
    {
        _interaction.Interactive = true;
        _interaction.ConfirmAnswer = true;
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.Prompt);

        status.Should().Be(PostActionExecutionStatus.Success);
        await _runScriptProcessor.ReceivedWithAnyArgs(1).ProcessAsync(default!, TestContext.Current.CancellationToken);
        _interaction.Lines.Should().ContainInOrder(
            "Template is configured to run the following action:",
            "  Restore tools",
            "Actual command: setup.cmd --restore",
            "CONFIRM: Do you want to run this action? (default: False, unavailable: False)");
    }

    [Fact]
    public async Task ProcessAsync_PromptDeclined_ReturnsCancelledWithInstructions()
    {
        _interaction.Interactive = true;
        _interaction.ConfirmAnswer = false;
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.Prompt);

        status.Should().Be(PostActionExecutionStatus.Cancelled);
        await _runScriptProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        _interaction.Lines.Should().ContainInOrder(
            "CONFIRM: Do you want to run this action? (default: False, unavailable: False)",
            "WARNING: Execution of 'Run script' post action was declined.",
            "Manual instructions: Run setup yourself.",
            "Actual command: setup.cmd --restore");
    }

    [Fact]
    public async Task ProcessAsync_PromptWithoutInteractiveConsole_DeclinesWithoutPrompting()
    {
        _interaction.Interactive = false;
        _interaction.ConfirmAnswer = true;
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.Prompt);

        status.Should().Be(PostActionExecutionStatus.Cancelled);
        await _runScriptProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        _interaction.Lines.Should().ContainInOrder(
            "WARNING: Execution of 'Run script' post action was declined because confirmation is unavailable.",
            "  Restore tools",
            "Manual instructions: Run setup yourself.",
            "Actual command: setup.cmd --restore");
        _interaction.Lines.Should().NotContain(line => line.StartsWith("CONFIRM:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_PromptCancelled_Throws()
    {
        _interaction.Interactive = true;
        using CancellationTokenSource cts = new();
        _interaction.OnConfirm = cts.Cancel;
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        Func<Task> act = () => _dispatcher.ProcessAsync(_templater, _template, result, isDryRun: false, AllowRunScripts.Prompt, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _runScriptProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ProcessAsync_DefaultPolicy_PromptsOnlyWhenInteractive(bool isInteractive, bool expectPrompt)
    {
        _interaction.Interactive = isInteractive;
        _interaction.ConfirmAnswer = true;
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, _template, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(expectPrompt ? PostActionExecutionStatus.Success : PostActionExecutionStatus.Cancelled);
        _interaction.Lines.Any(line => line.StartsWith("CONFIRM:", StringComparison.Ordinal)).Should().Be(expectPrompt);
    }

    [Fact]
    public async Task ProcessAsync_DeclinedWithContinueOnError_ContinuesLaterActions()
    {
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction(continueOnError: true), CreateAction(_otherActionId)]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.No);

        status.Should().Be(PostActionExecutionStatus.Cancelled);
        await _otherProcessor.ReceivedWithAnyArgs(1).ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_DeclinedWithoutContinueOnError_StopsLaterActions()
    {
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction(), CreateAction(_otherActionId)]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.No);

        status.Should().Be(PostActionExecutionStatus.Cancelled);
        await _otherProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_ScriptFails_ReturnsFailure()
    {
        _runScriptProcessor.ProcessAsync(Arg.Any<EnginePostActionContext>(), Arg.Any<CancellationToken>()).Returns(false);
        ITemplateCreationResult result = CreateResult([CreateRunScriptAction()]);

        PostActionExecutionStatus status = await ProcessAsync(result, AllowRunScripts.Yes);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.Lines.Should().Contain("ERROR: Post action failed.");
    }

    [Fact]
    public async Task ProcessAsync_DryRun_ListsScriptWithoutConsentOrExecution()
    {
        IPostAction action = CreateRunScriptAction();
        ITemplateCreationResult result = CreateResult([], dryRunActions: [action]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, _template, result, isDryRun: true, AllowRunScripts.Prompt, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        await _runScriptProcessor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        _interaction.Lines.Should().NotContain(line => line.StartsWith("CONFIRM:", StringComparison.Ordinal));
    }

    private Task<PostActionExecutionStatus> ProcessAsync(ITemplateCreationResult result, AllowRunScripts allowRunScripts)
        => _dispatcher.ProcessAsync(_templater, _template, result, isDryRun: false, allowRunScripts, TestContext.Current.CancellationToken);

    private static ICoreTemplateComponent CreateComponent(ICorePostActionProcessor processor)
    {
        ICoreTemplateComponent component = Substitute.For<ICoreTemplateComponent>();
        component.InterfaceType.Returns(typeof(ICorePostActionProcessor));
        component.Instance.Returns(processor);
        return component;
    }

    private static IPostAction CreateRunScriptAction(bool continueOnError = false)
        => CreateAction(
            PostActionDispatcher.RunScriptActionId,
            new Dictionary<string, string> { ["executable"] = "setup.cmd", ["args"] = "--restore" },
            "Restore tools",
            "Run setup yourself.",
            continueOnError);

    private static IPostAction CreateAction(
        Guid actionId,
        Dictionary<string, string>? args = null,
        string? description = null,
        string? instructions = null,
        bool continueOnError = false)
    {
        IPostAction action = Substitute.For<IPostAction>();
        action.ActionId.Returns(actionId);
        action.Args.Returns(args ?? []);
        action.Description.Returns(description);
        action.ManualInstructions.Returns(instructions);
        action.ContinueOnError.Returns(continueOnError);
        return action;
    }

    private ITemplateCreationResult CreateResult(IReadOnlyList<IPostAction> actions, IReadOnlyList<IPostAction>? dryRunActions = null)
    {
        ICreationResult creation = Substitute.For<ICreationResult>();
        creation.PostActions.Returns(actions);
        ICreationResult dryRunCreation = Substitute.For<ICreationResult>();
        dryRunCreation.PostActions.Returns(dryRunActions ?? []);
        ICreationEffects effects = Substitute.For<ICreationEffects>();
        effects.CreationResult.Returns(dryRunCreation);

        ITemplateCreationResult result = Substitute.For<ITemplateCreationResult>();
        result.CreationResult.Returns(creation);
        result.CreationEffects.Returns(effects);
        result.OutputBaseDirectory.Returns(_root);
        return result;
    }

    private sealed class ConsentInteractionService : TestInteractionService
    {
        public bool Interactive { get; set; }

        public bool ConfirmAnswer { get; set; }

        public Action? OnConfirm { get; set; }

        public override bool IsInteractive => Interactive;

        public override Task<bool> ConfirmAsync(
            string prompt, bool defaultValue, bool whenInputUnavailable, CancellationToken cancellationToken = default)
        {
            WriteLine($"CONFIRM: {prompt} (default: {defaultValue}, unavailable: {whenInputUnavailable})");
            OnConfirm?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ConfirmAnswer);
        }
    }
}
