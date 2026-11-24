// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PostActions;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.PostActions;

public sealed class WorkloadPostActionProcessorTests : IDisposable
{
    private static readonly Guid _actionId = Guid.NewGuid();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-workload-action-tests-" + Guid.NewGuid().ToString("N"));
    private readonly IInteractionService _interaction = Substitute.For<IInteractionService>();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Collectible workload assemblies can remain locked until the test runner releases its references.
        }
        catch (IOException)
        {
            // Best-effort cleanup; a lingering workload handle must not mask the test outcome.
        }
    }

    [Theory]
    [InlineData("node", "TypeScript", false)]
    [InlineData("NODE-ALIAS", "TS", false)]
    [InlineData(" node ", " TypeScript ", false)]
    [InlineData("python", "Python", false)]
    [InlineData("node", "TypeScript", true)]
    public async Task ProcessPostActionsAsync_DuplicateIds_SelectsEligibleProcessorRegardlessOfRegistrationOrder(
        string stack, string language, bool reverseWorkloads)
    {
        var node = Processor();
        var python = Processor("python", "Python");
        ServiceCollection services = Services();
        if (reverseWorkloads)
        {
            Register(services, python);
            Register(services, node);
        }
        else
        {
            Register(services, node);
            Register(services, python);
        }

        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        var status = await templater.ProcessPostActionsAsync(
            Template(stack, language), Result(), false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        node.Received(1).CanProcess(Arg.Is<PostActionSelectionContext>(context =>
            context.Stack == stack.Trim() && context.Language == language.Trim()));
        python.Received(1).CanProcess(Arg.Is<PostActionSelectionContext>(context =>
            context.Stack == stack.Trim() && context.Language == language.Trim()));
        bool isPython = stack == "python";
        await (isPython ? python : node).Received(1).ProcessAsync(
            Arg.Is<PostActionContext>(context => context.ActionId == _actionId), TestContext.Current.CancellationToken);
        (isPython ? node : python).ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == "ProcessAsync");
    }

    [Theory]
    [InlineData(null, "TypeScript")]
    [InlineData("node", null)]
    [InlineData("", "TypeScript")]
    [InlineData("  ", "TypeScript")]
    [InlineData("node", " ")]
    [InlineData("unknown", "TypeScript")]
    [InlineData("node", "Python")]
    public async Task ProcessPostActionsAsync_MissingOrUnmatchedTags_FailsWithManualInstructions(string? stack, string? language)
    {
        var processor = Processor();
        ServiceCollection services = Services();
        Register(services, processor);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        var status = await templater.ProcessPostActionsAsync(
            Template(stack, language), Result(), false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.Received().WriteLine("Manual instructions: Complete this manually.");
        _interaction.Received().WriteLine("  Restore dependencies");
        string expectedStack = stack?.Trim() ?? string.Empty;
        string expectedLanguage = language?.Trim() ?? string.Empty;
        processor.Received(1).CanProcess(Arg.Is<PostActionSelectionContext>(context =>
            context.Stack == expectedStack && context.Language == expectedLanguage));
        _interaction.Received().WriteError(Arg.Is<string>(message =>
            message.StartsWith("No workload processor matches", StringComparison.Ordinal)));

        await processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessPostActionsAsync_StackServicesNotResolved_ProcessorCanContributeWithoutOwningStack()
    {
        ServiceCollection services = Services();
        services.AddSingleton<IProjectStack>(_ => throw new InvalidOperationException("Stack resolution is unnecessary."));
        var processor = Processor();
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());
        builder.AddTemplateComponent(processor);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        var status = await templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), Result(), false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        await processor.Received(1).ProcessAsync(Arg.Any<PostActionContext>(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProcessPostActionsAsync_AmbiguousWorkloads_FailsWithoutExecutingEither()
    {
        ServiceCollection services = Services();
        var first = Processor();
        var second = Processor();
        Register(services, first, "First.Workload");
        Register(services, second, "Second.Workload");
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        var status = await templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), Result(), false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.Received().WriteError(Arg.Is<string>(message =>
            message.StartsWith("Multiple workload processors", StringComparison.Ordinal)
            && message.Contains("First.Workload", StringComparison.Ordinal)
            && message.Contains("Second.Workload", StringComparison.Ordinal)
            && message.Contains(_actionId.ToString(), StringComparison.Ordinal)));
        first.Received(1).CanProcess(Arg.Any<PostActionSelectionContext>());
        second.Received(1).CanProcess(Arg.Any<PostActionSelectionContext>());
        await first.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        await second.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessPostActionsAsync_DuplicateEligibleRegistrationsWithinWorkload_FailsWithoutExecuting(bool sameInstance)
    {
        ServiceCollection services = Services();
        var first = Processor();
        var second = sameInstance ? first : Processor();
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());
        builder.AddTemplateComponent(first);
        builder.AddTemplateComponent(second);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        var status = await templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), Result(), false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        first.Received(sameInstance ? 2 : 1).CanProcess(Arg.Any<PostActionSelectionContext>());
        await first.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        await second.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessPostActionsAsync_CoreAlwaysWins_WithoutEvaluatingWorkloadPredicate(bool coreFirst)
    {
        ServiceCollection services = Services();
        var core = Substitute.For<ICorePostActionProcessor>();
        core.Id.Returns(_actionId);
        core.ProcessAsync(Arg.Any<EnginePostActionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        if (coreFirst)
        {
            services.AddSingleton(core);
            services.AddTemplateComponent<ICorePostActionProcessor, ICorePostActionProcessor>();
        }

        var workload = Processor();
        workload.CanProcess(Arg.Any<PostActionSelectionContext>())
            .Returns(_ => throw new InvalidOperationException("Must not evaluate a shadowed predicate."));
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo());
        builder.AddTemplateComponent(workload);
        if (!coreFirst)
        {
            services.AddSingleton(core);
            services.AddTemplateComponent<ICorePostActionProcessor, ICorePostActionProcessor>();
        }

        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        var status = await templater.ProcessPostActionsAsync(
            Template(null, null), Result(), false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Success);
        await core.Received(1).ProcessAsync(Arg.Any<EnginePostActionContext>(), TestContext.Current.CancellationToken);
        workload.DidNotReceiveWithAnyArgs().CanProcess(default!);
        await workload.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, true)]
    public async Task ProcessPostActionsAsync_WorkloadResult_PreservesDispatcherBehavior(
        bool succeeds, bool continueOnError, bool dryRun, bool expectedSuccess)
    {
        ServiceCollection services = Services();
        var processor = Processor();
        processor.ProcessAsync(Arg.Any<PostActionContext>(), Arg.Any<CancellationToken>()).Returns(succeeds);
        Register(services, processor);
        var ineligible = Processor("python", "Python");
        Register(services, ineligible);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        var status = await templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), Result(continueOnError), dryRun, TestContext.Current.CancellationToken);

        status.Should().Be(expectedSuccess ? PostActionExecutionStatus.Success : PostActionExecutionStatus.Failure);
        await ineligible.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        if (dryRun)
        {
            processor.DidNotReceiveWithAnyArgs().CanProcess(default!);
            await processor.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        }
        else
        {
            await processor.Received(1).ProcessAsync(Arg.Any<PostActionContext>(), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ProcessPostActionsAsync_PredicateThrows_PropagatesWithoutExecutingAnyProcessor()
    {
        ServiceCollection services = Services();
        var first = Processor();
        var second = Processor();
        var exception = new InvalidOperationException("Predicate defect");
        second.CanProcess(Arg.Any<PostActionSelectionContext>()).Returns(_ => throw exception);
        Register(services, first);
        Register(services, second);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        Func<Task> act = () => templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), Result(), false, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        await first.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
        await second.DidNotReceiveWithAnyArgs().ProcessAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessPostActionsAsync_SelectionFails_HonorsContinueOnError(bool ambiguous)
    {
        ServiceCollection services = Services();
        Register(services, Processor(ambiguous ? "node" : "python", ambiguous ? "TypeScript" : "Python"));
        if (ambiguous)
        {
            Register(services, Processor());
        }

        var nextId = Guid.NewGuid();
        var next = Substitute.For<ICorePostActionProcessor>();
        next.Id.Returns(nextId);
        next.ProcessAsync(Arg.Any<EnginePostActionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(next);
        services.AddTemplateComponent<ICorePostActionProcessor, ICorePostActionProcessor>();
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());
        var result = Result(continueOnError: true);
        var creation = result.CreationResult!;
        var firstAction = creation.PostActions[0];
        var nextAction = Substitute.For<IPostAction>();
        nextAction.ActionId.Returns(nextId);
        creation.PostActions.Returns([firstAction, nextAction]);

        var status = await templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), result, false, TestContext.Current.CancellationToken);

        status.Should().Be(PostActionExecutionStatus.Failure);
        _interaction.Received().WriteLine("Manual instructions: Complete this manually.");
        await next.Received(1).ProcessAsync(Arg.Any<EnginePostActionContext>(), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessPostActionsAsync_WorkloadThrows_PropagatesException(bool cancellation)
    {
        ServiceCollection services = Services();
        var processor = Processor();
        Exception exception = cancellation
            ? new OperationCanceledException(TestContext.Current.CancellationToken)
            : new InvalidOperationException("Workload defect");
        processor.ProcessAsync(Arg.Any<PostActionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(exception));
        Register(services, processor);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());

        Func<Task> act = () => templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), Result(), false, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task ProcessPostActionsAsync_ProjectsIndependentReadOnlyContext()
    {
        ServiceCollection services = Services();
        var processor = Processor();
        Register(services, processor);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());
        var result = Result(continueOnError: true);
        var arguments = new Dictionary<string, string> { ["command"] = "restore" };
        result.CreationResult!.PostActions[0].Args.Returns(arguments);
        ICreationPath output = Substitute.For<ICreationPath>();
        output.Path.Returns("Function.cs");
        result.CreationResult.PrimaryOutputs.Returns([output]);
        var changes = Enum.GetValues<ChangeKind>().Select(kind =>
        {
            var change = Substitute.For<IFileChange>();
            change.TargetRelativePath.Returns(kind == ChangeKind.Create
                ? "./Function.cs"
                : Path.Combine("src", $"{kind}.txt"));
            change.ChangeKind.Returns(kind);
            return change;
        }).ToArray();
        result.CreationEffects!.FileChanges.Returns(changes);
        PostActionContext? captured = null;
        processor.ProcessAsync(Arg.Do<PostActionContext>(context => captured = context), Arg.Any<CancellationToken>()).Returns(true);

        await templater.ProcessPostActionsAsync(
            Template("node", "TypeScript"), result, false, TestContext.Current.CancellationToken);
        arguments["command"] = "changed";
        output.Path.Returns("changed");
        changes[0].TargetRelativePath.Returns("changed");

        captured.Should().NotBeNull();
        captured.ActionId.Should().Be(_actionId);
        captured.Arguments.Should().Contain("command", "restore");
        captured.OutputBasePath.Should().Be(_root);
        captured.Description.Should().Be("Restore dependencies");
        captured.ManualInstructions.Should().Be("Complete this manually.");
        captured.ContinueOnError.Should().BeTrue();
        captured.PrimaryOutputs.Should().Equal("Function.cs");
        PostActionFileChange[] expectedChanges =
        [
            new("Function.cs", PostActionFileChangeKind.Create),
            new("src/Delete.txt", PostActionFileChangeKind.Delete),
            new("src/Overwrite.txt", PostActionFileChangeKind.Overwrite),
            new("src/Change.txt", PostActionFileChangeKind.Change),
        ];
        captured.FileChanges.Should().BeEquivalentTo(expectedChanges);
        FluentActions.Invoking(() => ((IDictionary<string, string>)captured.Arguments).Add("mutate", "value"))
            .Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => ((IList<string>)captured.PrimaryOutputs).Add("mutate"))
            .Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => ((IList<PostActionFileChange>)captured.FileChanges).Clear())
            .Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisterWorkloadsAsync_IsolatedWorkload_RegistersAndExecutesPublicContract(bool configureFails)
    {
        var paths = new WorkloadPathsOptions(Path.Combine(_root, "workloads"));
        var store = new WorkloadStore(paths);
        const string fixtureName = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default";
        string installDirectory = paths.GetInstallDirectory("Fixture.PostActions", "1.0.0");
        Directory.CreateDirectory(installDirectory);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "tools", "any", fixtureName + ".dll"),
            Path.Combine(installDirectory, fixtureName + ".dll"));

        await store.SaveWorkloadAsync(
            new WorkloadEntry
            {
                PackageId = "Fixture.PostActions",
                PackageVersion = "1.0.0",
                EntryPoint = new EntryPointSpec
                {
                    AssemblyPath = fixtureName + ".dll",
                    Type = "Azure.Functions.Cli.Workloads.Tests.Fixtures.Default.PostActionWorkload" + (configureFails ? "+Failing" : ""),
                },
            },
            TestContext.Current.CancellationToken);
        ServiceCollection services = Services();
        await WorkloadRegistration.RegisterWorkloadsAsync(services, paths, _interaction, TestContext.Current.CancellationToken);
        await using var provider = services.BuildServiceProvider();
        using var templater = provider.GetRequiredService<ITemplaterFactory>().Create(Context());
        var result = Result();
        var registration = provider.GetServices<WorkloadTemplateComponentRegistration>().Single();
        result.CreationResult!.PostActions[0].ActionId.Returns(registration.Component.Id);
        result.CreationResult.PostActions[0].Args.Returns(new Dictionary<string, string> { ["fixture"] = "run" });

        try
        {
            var status = await templater.ProcessPostActionsAsync(
                Template("fixture", "Fixture"), result, false, TestContext.Current.CancellationToken);

            status.Should().Be(PostActionExecutionStatus.Success);
            registration.Workload.LoadContext.Should().NotBeNull();
            System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(registration.Component.GetType().Assembly)
                .Should().BeSameAs(registration.Workload.LoadContext);
            registration.Component.GetType().Assembly.GetReferencedAssemblies()
                .Should().NotContain(assembly => assembly.Name!.StartsWith("Microsoft.TemplateEngine", StringComparison.Ordinal));
            provider.GetServices<IProjectStack>().Should().BeEmpty();
            if (configureFails)
            {
                _interaction.Received().WriteWarning(Arg.Is<string>(message => message.Contains("Fixture failure after registration.")));
            }
        }
        finally
        {
            registration.Workload.LoadContext.Unload();
        }
    }

    [Fact]
    public void Context_InvalidArguments_Throws()
    {
        FluentActions.Invoking(() => new PostActionContext(_actionId, null!, null, null, false, _root, [], []))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new PostActionContext(_actionId, new Dictionary<string, string>(), null, null, false, "", [], []))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PostActionContext(_actionId, new Dictionary<string, string>(), null, null, false, _root, null!, []))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new PostActionContext(_actionId, new Dictionary<string, string>(), null, null, false, _root, [], null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new PostActionContext(_actionId, new Dictionary<string, string>(), null, null, false, _root, [""], []))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PostActionContext(_actionId, new Dictionary<string, string>(), null, null, false, _root, [], [null!]))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new PostActionFileChange("", PostActionFileChangeKind.Create))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PostActionFileChange("file", (PostActionFileChangeKind)100))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null, "C#")]
    [InlineData("dotnet", null)]
    public void SelectionContext_InvalidTags_Throws(string? stack, string? language)
    {
        Action act = () => _ = new PostActionSelectionContext(stack!, language!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("", "C#")]
    [InlineData("dotnet", "")]
    [InlineData(" ", "C#")]
    [InlineData("dotnet", " ")]
    public void SelectionContext_EmptyTags_ArePreservedAsMissing(string stack, string language)
    {
        PostActionSelectionContext context = new(stack, language);

        context.Stack.Should().Be(stack.Trim());
        context.Language.Should().Be(language.Trim());
    }

    private ServiceCollection Services()
    {
        ServiceCollection services = new();
        services.AddSingleton(_interaction);
        services.AddSingleton(Substitute.For<IWorkloadTemplateSource>());
        services.AddSingleton(new CliConfigurationPathsOptions(_root));
        services.AddTemplating();
        return services;
    }

    private TemplateEngineContext Context() => new(WorkingDirectory.FromExplicit(_root));

    private static IPostActionProcessor Processor(string stack = "node", string language = "TypeScript")
    {
        var processor = Substitute.For<IPostActionProcessor>();
        processor.Id.Returns(_actionId);
        processor.CanProcess(Arg.Any<PostActionSelectionContext>()).Returns(call =>
        {
            var context = call.Arg<PostActionSelectionContext>();
            return (string.Equals(context.Stack, stack, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(context.Stack, stack + "-alias", StringComparison.OrdinalIgnoreCase))
                && (string.Equals(context.Language, language, StringComparison.OrdinalIgnoreCase)
                    || (language == "TypeScript" && string.Equals(context.Language, "ts", StringComparison.OrdinalIgnoreCase)));
        });
        processor.ProcessAsync(Arg.Any<PostActionContext>(), Arg.Any<CancellationToken>()).Returns(true);
        return processor;
    }

    private static void Register(IServiceCollection services, IPostActionProcessor processor, string? packageId = null)
    {
        var builder = new DefaultFunctionsCliBuilder(services, TestWorkloads.CreateInfo(packageId ?? "Workload." + Guid.NewGuid()));
        builder.AddTemplateComponent(processor);
    }

    private static ITemplateInfo Template(string? stack, string? language)
    {
        var template = Substitute.For<ITemplateInfo>();
        Dictionary<string, string> tags = [];
        if (stack is not null)
        {
            tags["stack"] = stack;
        }

        if (language is not null)
        {
            tags["language"] = language;
        }

        template.TagsCollection.Returns(tags);
        return template;
    }

    private ITemplateCreationResult Result(bool continueOnError = false)
    {
        var action = Substitute.For<IPostAction>();
        action.ActionId.Returns(_actionId);
        action.Description.Returns("Restore dependencies");
        action.ManualInstructions.Returns("Complete this manually.");
        action.ContinueOnError.Returns(continueOnError);
        action.Args.Returns(new Dictionary<string, string>());
        var creation = Substitute.For<ICreationResult>();
        creation.PostActions.Returns([action]);
        var effects = Substitute.For<ICreationEffects>();
        effects.CreationResult.Returns(creation);
        var result = Substitute.For<ITemplateCreationResult>();
        result.CreationEffects.Returns(effects);
        result.CreationResult.Returns(creation);
        result.OutputBaseDirectory.Returns(_root);
        return result;
    }
}
