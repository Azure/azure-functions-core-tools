// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class InitExecutionContractTests
{
    private readonly ITemplaterFactory _factory = Substitute.For<ITemplaterFactory>();
    private readonly ITemplateArgumentParser _arguments = new TemplateArgumentParser();
    private readonly IInteractionService _interaction = Substitute.For<IInteractionService>();
    private readonly InitExecutionTargetInspector _targets = Substitute.For<InitExecutionTargetInspector>(new PhysicalFileSystem());
    private int _supportRequests;

    public InitExecutionContractTests()
    {
        _targets.GetDiagnostic(Arg.Any<WorkingDirectory>()).Returns((string?)null);
        _targets.InspectAsync(Arg.Any<WorkingDirectory>(), Arg.Any<CancellationToken>()).Returns(new InitExecutionTarget(InitExecutionTargetState.Empty));
    }

    [Theory]
    [InlineData("factory")]
    [InlineData("arguments")]
    [InlineData("prompts")]
    [InlineData("interaction")]
    [InlineData("renderer")]
    [InlineData("targets")]
    [InlineData("stackCatalog")]
    [InlineData("projectSupport")]
    [InlineData("fileSystem")]
    [InlineData("localSettings")]
    [InlineData("projects")]
    public void Runner_NullDependencies_RejectsProgrammerErrors(string dependency)
    {
        Action act = () => _ = new InitExecutionRunner(
            dependency == "factory" ? null! : _factory,
            dependency == "arguments" ? null! : _arguments,
            dependency == "prompts" ? null! : new(_arguments, _interaction),
            dependency == "interaction" ? null! : _interaction,
            dependency == "renderer" ? null! : new(_interaction),
            dependency == "targets" ? null! : _targets,
            dependency == "stackCatalog" ? null! : () => new([]),
            dependency == "projectSupport" ? null! : () => new(new(new([])), new(new PhysicalFileSystem())),
            dependency == "fileSystem" ? null! : new PhysicalFileSystem(),
            dependency == "localSettings" ? null! : Substitute.For<ILocalSettingsProvider>(),
            dependency == "projects" ? null! : Substitute.For<IFunctionsProjectResolver>());

        act.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Fact]
    public void CommandsAndInteraction_NullDependencies_RejectProgrammerErrors()
    {
        var runner = Substitute.For<IInitExecutionRunner>();
        Action commandRunner = () => _ = new InitExecutionCommand(null!, new(_interaction), new([]));
        Action commandRenderer = () => _ = new InitExecutionCommand(runner, null!, new([]));
        Action availability = () => _ = new InitExecutionCommand(runner, new(_interaction), null!);
        Action renderer = () => _ = new InitExecutionRenderer(null!);
        Action arguments = () => _ = new TemplateExecutionInteraction(null!, _interaction);
        Action interaction = () => _ = new TemplateExecutionInteraction(_arguments, null!);

        commandRunner.Should().Throw<ArgumentNullException>().WithParameterName("runner");
        commandRenderer.Should().Throw<ArgumentNullException>().WithParameterName("renderer");
        availability.Should().Throw<ArgumentNullException>().WithParameterName("availability");
        renderer.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
        arguments.Should().Throw<ArgumentNullException>().WithParameterName("arguments");
        interaction.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }

    [Fact]
    public async Task Execute_NullContractsOrPreCanceled_DoesNotInspectOrOpenFlow()
    {
        var runner = CreateRunner([]);
        var request = Request();
        Func<Task> nullRequest = () => runner.ExecuteAsync(null!, new([]), CancellationToken.None);
        Func<Task> nullAvailability = () => runner.ExecuteAsync(request, null!, CancellationToken.None);
        Func<Task> nullDirectory = () => runner.ExecuteAsync(request with { WorkingDirectory = null! }, new([]), CancellationToken.None);
        Func<Task> nullTokens = () => runner.ExecuteAsync(request with { RawTemplateTokens = null! }, new([]), CancellationToken.None);
        Func<Task> nullAliases = () => runner.ExecuteAsync(request with { ReservedAliases = null! }, new([]), CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> canceled = () => runner.ExecuteAsync(request, new([]), cancellation.Token);

        await nullRequest.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
        await nullAvailability.Should().ThrowAsync<ArgumentNullException>().WithParameterName("availability");
        await nullDirectory.Should().ThrowAsync<ArgumentNullException>();
        await nullTokens.Should().ThrowAsync<ArgumentNullException>();
        await nullAliases.Should().ThrowAsync<ArgumentNullException>();
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        _targets.ReceivedCalls().Should().BeEmpty();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("template", "invalidTemplateReference")]
    [InlineData("name", "invalidName")]
    [InlineData("force", "forceNotQualified")]
    public async Task Execute_InvalidStaticInput_StopsBeforeInspection(string input, string kind)
    {
        var request = Request() with
        {
            TemplateReference = input == "template" ? " " : null,
            Name = input == "name" ? " " : null,
            Force = input == "force",
        };

        var result = await CreateRunner([]).ExecuteAsync(request, new([]), CancellationToken.None);

        result.Kind.Should().Be(kind);
        result.Succeeded.Should().BeFalse();
        _targets.ReceivedCalls().Should().BeEmpty();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NoInstalledStacks_ExplainsSetupWithoutEngine()
    {
        var result = await CreateRunner([]).ExecuteAsync(Request(), new([]), CancellationToken.None);

        result.Kind.Should().Be("noInstalledStacks");
        result.Diagnostics.Single().Should().Contain("func setup");
        _supportRequests.Should().Be(0);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_DuplicateStackOwnership_RefusesWithoutOpeningEngine()
    {
        var first = Metadata("node");
        var second = Metadata("NODE");

        var result = await CreateRunner([first, second]).ExecuteAsync(Request(), new([]), CancellationToken.None);

        result.Kind.Should().Be("duplicateStacks");
        _supportRequests.Should().Be(0);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_ConflictingAliases_PreservesStructuredFailureBeforeSupportOrEnvironment(bool languageAlias)
    {
        var first = Metadata("node");
        var second = Metadata("other");
        if (languageAlias)
        {
            second.SupportedLanguages.Returns(["Python"]);
            second.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["Python"] = ["js"] });
        }
        else
        {
            second.WorkerRuntimeAliases.Returns(["node"]);
        }

        var result = await CreateRunner([first, second]).ExecuteAsync(Request(), new([]), CancellationToken.None);

        result.Kind.Should().Be("duplicateStacks");
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Single().Should().ContainEquivalentOf("conflict");
        _supportRequests.Should().Be(0);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_UnexpectedCatalogProviderFault_IsNotMisclassifiedAsInstalledMetadata()
    {
        InitExecutionRunner runner = new(_factory, _arguments, new(_arguments, _interaction), _interaction, new(_interaction),
            _targets, () => throw new InvalidOperationException("unexpected provider fault"),
            () => throw new InvalidOperationException("support must not be requested"), new PhysicalFileSystem(),
            Substitute.For<ILocalSettingsProvider>(), Substitute.For<IFunctionsProjectResolver>());

        Func<Task> act = () => runner.ExecuteAsync(Request(), new([]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unexpected provider fault");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_UnexpectedSupportProviderFault_PropagatesInsteadOfReturningDuplicateStacks()
    {
        InstalledProjectStackCatalog catalog = new([Metadata("node")]);
        InitExecutionRunner runner = new(_factory, _arguments, new(_arguments, _interaction), _interaction, new(_interaction),
            _targets, () => catalog, () => throw new InvalidOperationException("unexpected support fault"), new PhysicalFileSystem(),
            Substitute.For<ILocalSettingsProvider>(), Substitute.For<IFunctionsProjectResolver>());

        Func<Task> act = () => runner.ExecuteAsync(Request(), new([]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unexpected support fault");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_MalformedCatalogDefinition_PropagatesWithoutMisreportingConflict()
    {
        var invalid = Metadata("node");
        invalid.SupportedLanguages.Returns([]);

        Func<Task> act = () => CreateRunner([invalid]).ExecuteAsync(Request(), new([]), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _supportRequests.Should().Be(0);
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_CatalogResolutionReturnsAfterCancellation_DoesNotRequestSupportOrEnvironment()
    {
        using CancellationTokenSource cancellation = new();
        InstalledProjectStackCatalog catalog = new([Metadata("node")]);
        InitExecutionRunner runner = new(_factory, _arguments, new(_arguments, _interaction), _interaction, new(_interaction),
            _targets, () =>
            {
                cancellation.Cancel();
                return catalog;
            },
            () => throw new InvalidOperationException("support must not be requested"), new PhysicalFileSystem(),
            Substitute.For<ILocalSettingsProvider>(), Substitute.For<IFunctionsProjectResolver>());

        Func<Task> act = () => runner.ExecuteAsync(Request(), new([]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_TargetInspectionFailure_IsStructuredWithoutEngine(bool denied)
    {
        Exception failure = denied ? new UnauthorizedAccessException("controlled inspection") : new IOException("controlled inspection");
        _targets.InspectAsync(Arg.Any<WorkingDirectory>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<InitExecutionTarget>(failure));

        var result = await CreateRunner([]).ExecuteAsync(Request(), new([]), CancellationToken.None);

        result.Kind.Should().Be("invalidTarget");
        result.Diagnostics.Single().Should().Contain("controlled inspection");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_UnexpectedInspectionDefect_IsNotHiddenAsUserError()
    {
        _targets.InspectAsync(Arg.Any<WorkingDirectory>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<InitExecutionTarget>(new InvalidOperationException("unexpected")));

        Func<Task> act = () => CreateRunner([]).ExecuteAsync(Request(), new([]), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unexpected");
        _factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Render_FailedCreationWithPlans_NeverClaimsCompletedProjects()
    {
        TestInteractionService interaction = new();
        var result = InitExecutionResult.Reject("invocationFailed", "Native creation failed.") with
        {
            Preview = new(TemplateInvocationStatus.Success, [new("host.json", TemplateFileChangeKind.Create)])
            {
                ProjectConfigurations = [new(Path.Combine(Path.GetTempPath(), "consumer-project", "host.json"), "node", "JavaScript")],
            },
            Invocation = new(TemplateInvocationStatus.Failed, [new("host.json", TemplateFileChangeKind.Create)])
            {
                ProjectConfigurations = [new(Path.Combine(Path.GetTempPath(), "consumer-project", "host.json"), "node", "JavaScript")],
            },
        };

        int exit = new InitExecutionRenderer(interaction).Render(result, jsonOutput: false);

        exit.Should().Be(1);
        interaction.AllOutput.Should().Contain("Planned effects").And.Contain("Native creation failed")
            .And.NotContain("SUCCESS:").And.NotContain("Configured project:");
    }

    [Fact]
    public void Render_NullResultOrEntries_RejectsBeforeOutput()
    {
        var renderer = new InitExecutionRenderer(_interaction);
        Action result = () => renderer.Render(null!, jsonOutput: false);
        Action entries = () => renderer.RenderUnavailable(null!);
        Action target = () => new InitExecutionTargetInspector(new PhysicalFileSystem()).GetDiagnostic(null!);

        result.Should().Throw<ArgumentNullException>().WithParameterName("result");
        entries.Should().Throw<ArgumentNullException>().WithParameterName("entries");
        target.Should().Throw<ArgumentNullException>().WithParameterName("target");
        _interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_PreCreateConflictWithPlannedChanges_DoesNotClaimCompletedEffects(bool jsonOutput)
    {
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        TemplateFileChange host = new("host.json", TemplateFileChangeKind.Create);
        TemplateFileChange config = new(".func/config.json", TemplateFileChangeKind.Create);
        var result = InitExecutionResult.Reject("invocationFailed", "Existing user configuration was preserved.") with
        {
            Preview = new(TemplateInvocationStatus.Success, [host, config]),
            Invocation = new(TemplateInvocationStatus.FileConflict, [host]),
        };

        int exit = new InitExecutionRenderer(new SpectreInteractionService(new DefaultTheme(), stdout, stderr)).Render(result, jsonOutput);

        exit.Should().Be(1);
        if (jsonOutput)
        {
            using var json = JsonDocument.Parse(stdout.Output);
            json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
            var invocation = json.RootElement.GetProperty("invocation");
            invocation.GetProperty("status").GetString().Should().Be("FileConflict");
            invocation.GetProperty("plannedChanges").EnumerateArray().Select(change => change.GetProperty("path").GetString())
                .Should().Equal("host.json", ".func/config.json");
            invocation.GetProperty("completedChanges").GetArrayLength().Should().Be(0);
            invocation.GetProperty("configuredProjects").GetArrayLength().Should().Be(0);
        }
        else
        {
            stdout.Output.Should().Contain("Planned effects").And.Contain("host.json").And.Contain(".func/config.json")
                .And.NotContain("Completed effects").And.NotContain("Configured project:");
        }
    }

    [Theory]
    [InlineData("Success", 1)]
    [InlineData("ConfigurationFailure", 1)]
    [InlineData("Failed", 0)]
    [InlineData("InvalidArguments", 0)]
    [InlineData("MissingRequiredParameters", 0)]
    [InlineData("FileConflict", 0)]
    [InlineData("TemplateChanged", 0)]
    [InlineData("TemplateUnavailable", 0)]
    [InlineData("InvalidTemplate", 0)]
    [InlineData("EngineFailure", 0)]
    public void Render_PublishedOutcomeCategories_ClaimsOnlyConfirmedEffects(string statusName, int completedCount)
    {
        TemplateInvocationStatus status = Enum.Parse<TemplateInvocationStatus>(statusName);
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        TemplateFileChange effect = new("host.json", TemplateFileChangeKind.Create);
        InitExecutionResult result = new("control", status == TemplateInvocationStatus.Success)
        {
            Preview = new(TemplateInvocationStatus.Success, [effect]),
            Invocation = new(status, [effect]) { HasCommittedOutput = completedCount > 0 },
        };

        int exit = new InitExecutionRenderer(new SpectreInteractionService(new DefaultTheme(), stdout, stderr)).Render(result, jsonOutput: true);

        exit.Should().Be(status == TemplateInvocationStatus.Success ? 0 : 1);
        using var json = JsonDocument.Parse(stdout.Output);
        var invocation = json.RootElement.GetProperty("invocation");
        invocation.GetProperty("plannedChanges").EnumerateArray().Single().GetProperty("path").GetString().Should().Be("host.json");
        invocation.GetProperty("completedChanges").GetArrayLength().Should().Be(completedCount);
        if (completedCount > 0)
        {
            invocation.GetProperty("completedChanges").EnumerateArray().Single().GetProperty("path").GetString().Should().Be("host.json");
        }
    }

    [Fact]
    public async Task Inspector_NullDependencyOrTargetAndPreCanceled_RejectsWithoutReadingFiles()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        Action dependency = () => _ = new InitExecutionTargetInspector(null!);
        var inspector = new InitExecutionTargetInspector(fileSystem);
        Func<Task> target = () => inspector.InspectAsync(null!, CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Func<Task> canceled = () => inspector.InspectAsync(WorkingDirectory.FromCwd(), cancellation.Token);

        dependency.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
        await target.Should().ThrowAsync<ArgumentNullException>().WithParameterName("target");
        await canceled.Should().ThrowAsync<OperationCanceledException>();
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Registration_KeepsPublicCommandsUnchangedAndRunnerInvocationScoped()
    {
        ServiceCollection services = new();

        services.AddTemplatesOrchestrator();

        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(FuncCliCommand));
        services.Single(descriptor => descriptor.ServiceType == typeof(IInitExecutionRunner)).Lifetime.Should().Be(ServiceLifetime.Scoped);
        typeof(InitExecutionCommand).GetInterfaces().Should().NotContain(typeof(IBuiltInCommand));
    }

    [Theory]
    [InlineData("Success", false, 0)]
    [InlineData("ConfigurationFailure", false, 0)]
    [InlineData("Failed", true, 1)]
    [InlineData("EngineFailure", true, 1)]
    [InlineData("FileConflict", true, 1)]
    [InlineData("TemplateChanged", true, 1)]
    public void Render_CompletedEffectsUseRealCommitMarker_NotStatusShape(string statusName, bool committed, int count)
    {
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        InitExecutionResult result = new("invocationFailed", false)
        {
            Invocation = new(Enum.Parse<TemplateInvocationStatus>(statusName), [new("host.json", TemplateFileChangeKind.Create)])
            {
                HasCommittedOutput = committed,
            },
        };

        int exit = new InitExecutionRenderer(new SpectreInteractionService(new DefaultTheme(), stdout, stderr))
            .Render(result, jsonOutput: true);

        exit.Should().Be(1);
        using var document = JsonDocument.Parse(stdout.Output);
        document.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("invocation").GetProperty("completedChanges").GetArrayLength().Should().Be(count);
    }

    [Fact]
    public void ResolvedTemplate_ProjectDiagnosticWithoutOtherRestrictions_CannotBecomeInvocable()
    {
        TemplateCatalogEntry entry = new("invalid", "invalid", null, TemplateType.Project, ["invalid"], null, null, 0, null, false, [], null)
        {
            ConstraintEvaluation = new(TemplateConstraintEvaluationStatus.Eligible, []),
            ProjectConfigurationDiagnostic = "Invalid project declaration.",
        };

        Action act = () => _ = new ResolvedTemplate(entry, Substitute.For<ISelectedTemplateInvoker>());

        act.Should().Throw<ArgumentException>().WithParameterName("entry");
    }

    private InitExecutionRunner CreateRunner(IReadOnlyList<IProjectStack> stacks)
    {
        Lazy<InstalledProjectStackCatalog> catalog = new(() => new(stacks));
        Lazy<TemplateProjectSupport> support = new(() => new(new(catalog.Value), new(new PhysicalFileSystem())));
        return new(_factory, _arguments, new(_arguments, _interaction), _interaction, new(_interaction),
            _targets, () => catalog.Value, () =>
            {
                _supportRequests++;
                return support.Value;
            }, new PhysicalFileSystem(), Substitute.For<ILocalSettingsProvider>(), Substitute.For<IFunctionsProjectResolver>());
    }

    private static IProjectStack Metadata(string stack)
    {
        var metadata = Substitute.For<IProjectStack>();
        metadata.Stack.Returns(stack);
        metadata.DisplayName.Returns(stack);
        metadata.WorkerRuntimeAliases.Returns([]);
        metadata.SupportedLanguages.Returns(["JavaScript"]);
        metadata.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>> { ["JavaScript"] = ["js"] });
        return metadata;
    }

    private static InitExecutionRequest Request() => new(
        WorkingDirectory.FromCwd(), null, null, null, null, false, true, false, false, [], new HashSet<string>());
}
