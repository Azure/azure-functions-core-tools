// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PostActions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.PostActions;

public sealed class PostActionDispatcherTests : IDisposable
{
    private static readonly Guid _knownActionId = Guid.NewGuid();

    private readonly string _root;
    private readonly IInteractionService _interaction = Substitute.For<IInteractionService>();
    private readonly IPostActionProcessor _processor = Substitute.For<IPostActionProcessor>();
    private readonly Templater _templater;
    private readonly PostActionDispatcher _dispatcher;

    public PostActionDispatcherTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-post-action-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _processor.Id.Returns(_knownActionId);
        _processor.ProcessAsync(Arg.Any<PostActionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        _templater = new(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            Path.Combine(_root, "hive"),
            [new Component(_processor)]);
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
    public void Constructor_NullInteraction_Throws()
    {
        Action act = () => _ = new PostActionDispatcher(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ProcessAsync_NotDryRunWithoutCreationResult_Throws()
    {
        ITemplateCreationResult result = CreateResult([]);
        result.CreationResult.Returns((ICreationResult?)null);

        Func<Task> act = () => _dispatcher.ProcessAsync(_templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ProcessAsync_NoPostActions_ReturnsSuccessWithoutOutput()
    {
        ITemplateCreationResult result = CreateResult([]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_DryRun_ListsActionsWithoutProcessing()
    {
        IPostAction action = CreateAction(_knownActionId, description: "Restore packages");
        ITemplateCreationResult result = CreateResult([], dryRunActions: [action]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: true, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        _interaction.Received().WriteLine("Action would have been taken automatically:");
        _interaction.Received().WriteLine("  Restore packages");
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_RegisteredProcessor_ProcessesActionWithContext()
    {
        IPostAction action = CreateAction(_knownActionId);
        ITemplateCreationResult result = CreateResult([action]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        await _processor.Received(1).ProcessAsync(
            Arg.Is<PostActionContext>(context =>
                context.Action == action
                && context.Environment == _templater.Settings
                && context.CreationEffects == result.CreationEffects
                && context.CreationResult == result.CreationResult
                && context.OutputBasePath == _root),
            TestContext.Current.CancellationToken);
        _interaction.DidNotReceiveWithAnyArgs().WriteError(default!);
    }

    [Fact]
    public async Task ProcessAsync_ProcessorFails_ReturnsFailureAndShowsInstructions()
    {
        _processor.ProcessAsync(Arg.Any<PostActionContext>(), Arg.Any<CancellationToken>()).Returns(false);
        ITemplateCreationResult result = CreateResult(
            [CreateAction(_knownActionId, description: "Restore packages", instructions: "Do it by hand.")]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.Received().WriteError("Post action failed.");
        _interaction.Received().WriteLine("  Restore packages");
        _interaction.Received().WriteLine("Manual instructions: Do it by hand.");
    }

    [Fact]
    public async Task ProcessAsync_ProcessorFailsWithoutInstructions_ShowsDescription()
    {
        _processor.ProcessAsync(Arg.Any<PostActionContext>(), Arg.Any<CancellationToken>()).Returns(false);
        ITemplateCreationResult result = CreateResult([CreateAction(_knownActionId, description: "Restore packages")]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.Received().WriteError("Post action failed.");
        _interaction.Received().WriteLine("  Restore packages");
        _interaction.DidNotReceive().WriteLine(Arg.Is<string>(text => text.StartsWith("Manual instructions", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ProcessAsync_ProcessorNotFound_ReturnsFailureAndShowsInstructions()
    {
        var unknown = Guid.NewGuid();
        ITemplateCreationResult result = CreateResult(
            [CreateAction(unknown, description: "Add a package", instructions: "Add the package yourself.")]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.Received().WriteError($"Post action '{unknown}' is not supported.");
        _interaction.Received().WriteLine("  Add a package");
        _interaction.Received().WriteLine("Manual instructions: Add the package yourself.");
    }

    [Fact]
    public async Task ProcessAsync_ProcessorNotFoundWithoutInstructions_WritesNoInstructions()
    {
        ITemplateCreationResult result = CreateResult([CreateAction(Guid.NewGuid())]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.DidNotReceive().WriteLine(Arg.Is<string>(text => text.StartsWith("Manual instructions", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ProcessAsync_FailureWithoutContinueOnError_StopsLaterActions()
    {
        ITemplateCreationResult result = CreateResult([CreateAction(Guid.NewGuid()), CreateAction(_knownActionId)]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_FailureWithContinueOnError_ContinuesAndReturnsSuccess()
    {
        ITemplateCreationResult result = CreateResult(
            [CreateAction(Guid.NewGuid(), continueOnError: true), CreateAction(_knownActionId)]);

        PostActionExecutionStatus status = await _dispatcher.ProcessAsync(
            _templater, result, isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        await _processor.ReceivedWithAnyArgs(1).ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessAsync_Cancelled_Throws()
    {
        ITemplateCreationResult result = CreateResult([CreateAction(_knownActionId)]);
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = () => _dispatcher.ProcessAsync(_templater, result, isDryRun: false, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public async Task ProcessPostActionsAsync_RegisteredDispatcher_InvokesWithSuppliedArguments(
        bool processorSucceeds, bool isDryRun, bool expectedSuccess)
    {
        ServiceCollection services = new();
        services.AddSingleton(_interaction);
        services.AddSingleton(Substitute.For<IWorkloadTemplateSource>());
        services.AddTemplating();
        services.AddSingleton(_processor);
        services.AddTemplateComponent<IPostActionProcessor, IPostActionProcessor>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using Templater templater = new(
            _templater.Context, Path.Combine(_root, "extension-hive"), provider.GetServices<ITemplateComponent>());
        _processor.ProcessAsync(Arg.Any<PostActionContext>(), Arg.Any<CancellationToken>()).Returns(processorSucceeds);
        IPostAction action = CreateAction(_knownActionId, description: "Restore packages");
        ITemplateCreationResult result = CreateResult([action], dryRunActions: [action]);

        PostActionExecutionStatus status = await templater.ProcessPostActionsAsync(
            result, isDryRun, TestContext.Current.CancellationToken);

        status.Should().Be(expectedSuccess ? PostActionExecutionStatus.Success : PostActionExecutionStatus.Failure);
        if (isDryRun)
        {
            _interaction.Received().WriteLine("  Restore packages");
            await _processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        }
        else
        {
            await _processor.Received(1).ProcessAsync(
                Arg.Is<PostActionContext>(context => context.Environment == templater.Settings && context.Action == action),
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void ProcessPostActionsAsync_MissingDispatcher_Throws()
    {
        Action act = () => _templater.ProcessPostActionsAsync(
            CreateResult([]), isDryRun: false, TestContext.Current.CancellationToken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddTemplating*");
    }

    [Fact]
    public void ProcessPostActionsAsync_NullTemplater_Throws()
    {
        Action act = () => ((Templater)null!).ProcessPostActionsAsync(
            CreateResult([]), isDryRun: false, TestContext.Current.CancellationToken);

        act.Should().Throw<ArgumentNullException>().WithParameterName("templater");
    }

    [Fact]
    public void ProcessPostActionsAsync_NullCreationResult_Throws()
    {
        Action act = () => _templater.ProcessPostActionsAsync(
            null!, isDryRun: false, TestContext.Current.CancellationToken);

        act.Should().Throw<ArgumentNullException>().WithParameterName("creationResult");
    }

    private static IPostAction CreateAction(
        Guid actionId, string? description = null, string? instructions = null, bool continueOnError = false)
    {
        IPostAction action = Substitute.For<IPostAction>();
        action.ActionId.Returns(actionId);
        action.Description.Returns(description);
        action.ManualInstructions.Returns(instructions);
        action.ContinueOnError.Returns(continueOnError);
        action.Args.Returns(new Dictionary<string, string>());
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

    private sealed class Component(IPostActionProcessor processor) : ITemplateComponent
    {
        public Type InterfaceType => typeof(IPostActionProcessor);

        public IIdentifiedComponent Instance => processor;
    }
}
