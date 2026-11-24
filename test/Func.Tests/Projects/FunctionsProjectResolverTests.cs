// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects;

public sealed class FunctionsProjectResolverTests
{
    private readonly WorkingDirectory _directory = WorkingDirectory.FromExplicit(Environment.CurrentDirectory);
    private readonly IProjectStackConfiguration _config = Substitute.For<IProjectStackConfiguration>();
    private readonly TestProjectStack _go = new("go", "Go", "native", new LanguageDescriptor("go", "Go"));
    private readonly TestProjectStack _other = new("comet", "Go", "comet-host", new LanguageDescriptor("script", "Script"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveProjectAsync_StackSignal_OnlyRunsOwningFactory(bool hasConfig)
    {
        var otherFactory = Recognized(_other);
        var goFactory = Recognized(_go);
        FunctionsProjectResolver resolver = Create(hasConfig ? new(true, StackIdentifier: "GO") : new(WorkerRuntime: "NATIVE"), otherFactory, goFactory);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(new(_directory), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.Stack.Should().BeSameAs(_go);
        await goFactory.Registration.Factory.Received(1).TryCreateProjectAsync(new(_directory), TestContext.Current.CancellationToken);
        await otherFactory.Registration.Factory.DidNotReceiveWithAnyArgs().TryCreateProjectAsync(default!, TestContext.Current.CancellationToken);
        await _config.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResolveProjectAsync_WrongSourceSelector_RejectsBeforeFingerprinting(bool hasConfig)
    {
        var factory = Recognized(_go);
        FunctionsProjectResolver resolver = Create(hasConfig ? new(true, StackIdentifier: "native") : new(WorkerRuntime: "go"), factory);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(new(_directory), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Which.Message.Should().Contain(hasConfig ? ProjectStackSettings.StackSource : ProjectStackSettings.WorkerRuntimeSource);
        await factory.Registration.Factory.DidNotReceiveWithAnyArgs().TryCreateProjectAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResolveProjectAsync_AdoptingWithOnlyOtherFingerprint_ReturnsConflictNotUnrecognized()
    {
        FunctionsProjectResolver resolver = Create(new(), Factory(_go, ProjectCreationResults.NotCreated("no match")), Recognized(_other));

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(new(_directory, _go), TestContext.Current.CancellationToken);

        var error = result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Subject;
        error.IsUnrecognized.Should().BeFalse();
        error.Message.Should().Contain("conflicts").And.Contain("go").And.Contain("comet");
    }

    [Fact]
    public async Task ResolveProjectAsync_AmbiguousFingerprints_ReturnsBothStackIds()
    {
        FunctionsProjectResolver resolver = Create(new(), Recognized(_other), Recognized(_go));

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(new(_directory), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Which.Message.Should().Contain("both 'comet' and 'go'");
    }

    [Fact]
    public async Task ResolveProjectAsync_WithoutFingerprints_IsUnrecognized()
    {
        FunctionsProjectResolver resolver = Create(new(), Factory(_go, ProjectCreationResults.NotCreated("not mine")));

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(new(_directory), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Which.IsUnrecognized.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveProjectAsync_FactoryFailure_IsNotAnUnrecognizedProject()
    {
        ProjectCreationFailure failure = ProjectCreationFailures.WorkerNotResolved(FunctionsWorkerResolutionFailures.NotInstalled(new FunctionsWorkerId("go"), "missing worker"));
        FunctionsProjectResolver resolver = Create(new(), Factory(_go, ProjectCreationResults.Failed(failure)));

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(new(_directory), TestContext.Current.CancellationToken);

        var notResolved = result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Subject;
        notResolved.Failure.Should().BeSameAs(failure);
        notResolved.IsUnrecognized.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveProjectAsync_ConflictingPersistedAndDetectedLanguage_ReturnsBothSources()
    {
        TestProjectStack node = new("node", "Node", "node", new LanguageDescriptor("js", "JavaScript"), new LanguageDescriptor("ts", "TypeScript"));
        FunctionsProjectResolver resolver = Create(new(true, "node", "js"), Recognized(node, node.SupportedLanguages[1]));

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(new(_directory), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Which.Message.Should().Contain(ProjectStackSettings.LanguageSource).And.Contain("detected project language 'ts'");
    }

    private FunctionsProjectResolver Create(ProjectStackSettings settings, params (IProjectStack Stack, WorkloadProjectFactoryRegistration Registration)[] factories)
    {
        _config.Read(_directory.Info).Returns(settings);
        return new(factories.Select(f => f.Registration), new(factories.Select(f => f.Stack)), _config);
    }

    private static (IProjectStack Stack, WorkloadProjectFactoryRegistration Registration) Recognized(IProjectStack stack, LanguageDescriptor? language = null)
    {
        FunctionsProject project = Substitute.For<FunctionsProject>();
        project.Stack.Returns(stack);
        project.Language.Returns(language);
        return Factory(stack, ProjectCreationResults.Created(project, "matched"));
    }

    private static (IProjectStack Stack, WorkloadProjectFactoryRegistration Registration) Factory(IProjectStack stack, ProjectCreationResult result)
    {
        IFunctionsProjectFactory factory = Substitute.For<IFunctionsProjectFactory>();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>()).Returns(result);
        return (stack, new(TestWorkloads.CreateInfo($"Contoso.{stack.Descriptor.StackIdentifier}") with { Stack = stack.Descriptor }, factory));
    }
}
