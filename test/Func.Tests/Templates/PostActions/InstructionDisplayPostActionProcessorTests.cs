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

public sealed class InstructionDisplayPostActionProcessorTests : IDisposable
{
    private readonly string _root;
    private readonly TestInteractionService _interaction = new();
    private readonly InstructionDisplayPostActionProcessor _processor;

    public InstructionDisplayPostActionProcessorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-instruction-post-action-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _processor = new(_interaction);
    }

    public void Dispose()
    {
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
    public void Id_IsInstructionDisplayActionId()
    {
        _processor.Id.Should().Be(new Guid("AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C"));
    }

    [Fact]
    public void Constructor_NullInteraction_Throws()
    {
        Action act = () => _ = new InstructionDisplayPostActionProcessor(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task ProcessAsync_NullContext_Throws()
    {
        Func<Task> act = () => _processor.ProcessAsync(null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ProcessAsync_WithoutExecutable_DisplaysDescriptionAndInstructions()
    {
        IPostAction action = CreateAction("Open the readme", "Read README.md", new Dictionary<string, string> { ["args"] = "ignored" });

        bool result = await _processor.ProcessAsync(CreateContext(action), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        _interaction.Lines.Should().Equal("Description: Open the readme", "Manual instructions: Read README.md");
    }

    [Fact]
    public async Task ProcessAsync_WithExecutableAndArgs_DisplaysCommand()
    {
        IPostAction action = CreateAction(
            "Restore", "Run restore", new Dictionary<string, string> { ["executable"] = "dotnet", ["args"] = "restore" });

        bool result = await _processor.ProcessAsync(CreateContext(action), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        _interaction.Lines.Should().Equal("Description: Restore", "Manual instructions: Run restore", "Actual command: dotnet restore");
    }

    [Fact]
    public async Task ProcessAsync_WithExecutableOnly_DisplaysCommandWithoutArgs()
    {
        IPostAction action = CreateAction("Build", "Run build", new Dictionary<string, string> { ["executable"] = "make" });

        await _processor.ProcessAsync(CreateContext(action), TestContext.Current.CancellationToken);

        _interaction.Lines.Should().HaveElementAt(2, "Actual command: make ");
    }

    [Fact]
    public async Task ProcessAsync_ArgumentKeysAreCaseSensitive()
    {
        IPostAction action = CreateAction("d", "i", new Dictionary<string, string>(StringComparer.Ordinal) { ["Executable"] = "dotnet" });

        await _processor.ProcessAsync(CreateContext(action), TestContext.Current.CancellationToken);

        _interaction.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessAsync_NullArgsAndText_DisplaysEmptyValues()
    {
        IPostAction action = CreateAction(null, null, null);

        bool result = await _processor.ProcessAsync(CreateContext(action), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        _interaction.Lines.Should().Equal("Description: ", "Manual instructions: ");
    }

    [Fact]
    public async Task ProcessAsync_Cancelled_ThrowsWithoutOutput()
    {
        IPostAction action = CreateAction("d", "i", null);

        Func<Task> act = () => _processor.ProcessAsync(CreateContext(action), new CancellationToken(canceled: true));

        await act.Should().ThrowAsync<OperationCanceledException>();
        _interaction.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatcher_RegisteredByAddTemplating_DisplaysInstructionsOnceAndSucceeds()
    {
        ServiceCollection services = new();
        services.AddSingleton<IInteractionService>(_interaction);
        services.AddSingleton(Substitute.For<IWorkloadTemplateSource>());
        services.AddTemplating();
        await using ServiceProvider provider = services.BuildServiceProvider();
        using Templater templater = new(
            new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)),
            Path.Combine(_root, "hive"),
            provider.GetRequiredService<IFuncTemplateComponentProvider>().Get());
        IPostAction action = CreateAction(
            "Restore", "Run restore", new Dictionary<string, string> { ["executable"] = "dotnet", ["args"] = "restore" });
        action.ActionId.Returns(InstructionDisplayPostActionProcessor.ActionId);

        PostActionExecutionStatus status = await templater.ProcessPostActionsAsync(
            Substitute.For<ITemplateInfo>(), CreateResult(action), isDryRun: false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        _interaction.Lines.Should().ContainSingle(line => line == "Manual instructions: Run restore");
        _interaction.Lines.Should().ContainSingle(line => line == "Actual command: dotnet restore");
        _interaction.Lines.Should().NotContain(line => line.StartsWith("ERROR:", StringComparison.Ordinal));
    }

    private static IPostAction CreateAction(string? description, string? instructions, IReadOnlyDictionary<string, string>? args)
    {
        IPostAction action = Substitute.For<IPostAction>();
        action.ActionId.Returns(InstructionDisplayPostActionProcessor.ActionId);
        action.Description.Returns(description);
        action.ManualInstructions.Returns(instructions);
        action.Args.Returns(args);
        return action;
    }

    private EnginePostActionContext CreateContext(IPostAction action) => new(
        Substitute.For<IEngineEnvironmentSettings>(),
        action,
        Substitute.For<ICreationEffects>(),
        Substitute.For<ICreationResult>(),
        _root,
        Substitute.For<ITemplateInfo>());

    private ITemplateCreationResult CreateResult(IPostAction action)
    {
        ICreationResult creation = Substitute.For<ICreationResult>();
        creation.PostActions.Returns([action]);
        ITemplateCreationResult result = Substitute.For<ITemplateCreationResult>();
        result.CreationResult.Returns(creation);
        result.CreationEffects.Returns(Substitute.For<ICreationEffects>());
        result.OutputBaseDirectory.Returns(_root);
        return result;
    }
}
