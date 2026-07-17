// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects;

public sealed class FunctionsProjectResolverTests
{
    private readonly WorkingDirectory _workingDirectory = WorkingDirectory.FromExplicit(Environment.CurrentDirectory);

    [Fact]
    public async Task ResolveProjectAsync_FirstFactoryCreatesProject_ReturnsProject()
    {
        FunctionsProject project = Substitute.For<FunctionsProject>();
        IFunctionsProjectFactory factory = NewFactory(ProjectCreationResults.Created(project, "matched"));
        FunctionsProjectResolver resolver = NewResolver([factory]);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        ProjectResolutionResult.Resolved resolved = result.Should().BeOfType<ProjectResolutionResult.Resolved>().Subject;
        resolved.Project.Should().BeSameAs(project);
        resolved.Message.Should().Be("matched");
    }

    [Fact]
    public async Task ResolveProjectAsync_NotCreated_ContinuesToNextFactory()
    {
        FunctionsProject project = Substitute.For<FunctionsProject>();
        IFunctionsProjectFactory first = NewFactory(ProjectCreationResults.NotCreated("not mine"));
        IFunctionsProjectFactory second = NewFactory(ProjectCreationResults.Created(project, "matched second"));
        FunctionsProjectResolver resolver = NewResolver([first, second]);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        ProjectResolutionResult.Resolved resolved = result.Should().BeOfType<ProjectResolutionResult.Resolved>().Subject;
        resolved.Project.Should().BeSameAs(project);
        resolved.Message.Should().Be("matched second");
        await second.Received(1).TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveProjectAsync_FirstCreated_Wins()
    {
        FunctionsProject firstProject = Substitute.For<FunctionsProject>();
        IFunctionsProjectFactory first = NewFactory(ProjectCreationResults.Created(firstProject, "matched first"));
        IFunctionsProjectFactory second = NewFactory(ProjectCreationResults.Created(
            Substitute.For<FunctionsProject>(),
            "matched second"));
        FunctionsProjectResolver resolver = NewResolver([first, second]);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        ProjectResolutionResult.Resolved resolved = result.Should().BeOfType<ProjectResolutionResult.Resolved>().Subject;
        resolved.Project.Should().BeSameAs(firstProject);
        await second.DidNotReceive().TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveProjectAsync_Failed_StopsResolution()
    {
        FunctionsWorkerResolutionFailure workerFailure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("python"),
            "missing worker");
        ProjectCreationFailure failure = ProjectCreationFailures.WorkerNotResolved(workerFailure, "missing worker");
        IFunctionsProjectFactory first = NewFactory(ProjectCreationResults.Failed(failure));
        IFunctionsProjectFactory second = NewFactory(ProjectCreationResults.Created(
            Substitute.For<FunctionsProject>(),
            "matched second"));
        FunctionsProjectResolver resolver = NewResolver([first, second]);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        ProjectResolutionResult.NotResolved notResolved = result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Subject;
        notResolved.Message.Should().Be("missing worker");
        notResolved.Failure.Should().BeSameAs(failure);
        await second.DidNotReceive().TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveProjectAsync_NoFactoryCreatesProject_ReturnsNotResolved()
    {
        IFunctionsProjectFactory factory = NewFactory(ProjectCreationResults.NotCreated("not mine"));
        FunctionsProjectResolver resolver = NewResolver([factory]);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        ProjectResolutionResult.NotResolved notResolved = result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Subject;
        notResolved.Message.Should().Contain("No installed workload recognized");
    }

    [Fact]
    public async Task ResolveProjectAsync_PassesCreationContextToFactory()
    {
        IFunctionsProjectFactory factory = NewFactory(ProjectCreationResults.NotCreated("not mine"));
        FunctionsProjectResolver resolver = NewResolver([factory]);

        await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        await factory.Received(1).TryCreateProjectAsync(
            Arg.Is<ProjectCreationContext>(context =>
                context.WorkingDirectory == _workingDirectory),
            Arg.Any<CancellationToken>());
    }

    private ProjectResolutionContext CreateContext() => new(_workingDirectory);

    private FunctionsProjectResolver NewResolver(IReadOnlyList<IFunctionsProjectFactory> factories)
        => new(
            factories.Select((factory, index) => new WorkloadProjectFactoryRegistration(
                TestWorkloads.CreateInfo($"Test.Workload.{index}"),
                factory)));

    private static IFunctionsProjectFactory NewFactory(ProjectCreationResult result)
    {
        IFunctionsProjectFactory factory = Substitute.For<IFunctionsProjectFactory>();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(result);
        return factory;
    }
}
