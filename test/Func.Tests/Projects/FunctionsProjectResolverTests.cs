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
    private const string InspectionFailureMessage =
        "The project directory could not be inspected. Check that it exists and is accessible, then try again.";

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

        result.Should().BeOfType<ProjectResolutionResult.Resolved>().Which.Project.Should().BeSameAs(firstProject);
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
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        FunctionsProjectResolver resolver = NewResolver([first, second], fileSystem);
        string directory = Path.Combine(Path.GetTempPath(), $"func-project-failed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            ProjectResolutionResult result = await resolver.ResolveProjectAsync(
                new ProjectResolutionContext(WorkingDirectory.FromExplicit(directory)),
                CancellationToken.None);

            ProjectResolutionResult.NotResolved notResolved = result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Subject;
            notResolved.Message.Should().Be("missing worker");
            notResolved.Failure.Should().BeSameAs(failure);
            await second.DidNotReceive().TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>());
            fileSystem.DidNotReceiveWithAnyArgs().DirectoryExists(default!);
            fileSystem.DidNotReceiveWithAnyArgs().DirectoryHasEntries(default!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveProjectAsync_NoFactoryCreatesProject_ReturnsNotResolved()
    {
        IFunctionsProjectFactory factory = NewFactory(ProjectCreationResults.NotCreated("not mine"));
        FunctionsProjectResolver resolver = NewResolver([factory]);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        result.Should().BeOfType<ProjectResolutionResult.NotResolved>()
            .Which.Message.Should().Contain("No installed workload recognized");
    }

    [Fact]
    public async Task ResolveProjectAsync_EmptyDirectoryWithInstalledFactory_ReturnsUninitialized()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-project-uninitialized-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            IFunctionsProjectFactory factory = NewFactory(ProjectCreationResults.NotCreated("not mine"));
            FunctionsProjectResolver resolver = NewResolver([factory]);

            ProjectResolutionResult result = await resolver.ResolveProjectAsync(
                new ProjectResolutionContext(WorkingDirectory.FromExplicit(directory)),
                CancellationToken.None);

            ProjectResolutionResult.Uninitialized uninitialized = result.Should().BeOfType<ProjectResolutionResult.Uninitialized>().Subject;
            uninitialized.Message.Should().Be(
                "This directory is not an initialized Azure Functions project. Run 'func init' to initialize a Functions project.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveProjectAsync_EmptyDirectoryWithoutInstalledFactories_RemainsNotResolved()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-project-no-workload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            IFileSystem fileSystem = Substitute.For<IFileSystem>();
            FunctionsProjectResolver resolver = NewResolver([], fileSystem);

            ProjectResolutionResult result = await resolver.ResolveProjectAsync(
                new ProjectResolutionContext(WorkingDirectory.FromExplicit(directory)),
                CancellationToken.None);

            result.Should().BeOfType<ProjectResolutionResult.NotResolved>()
                .Which.Message.Should().Contain("Run 'func setup'");
            fileSystem.DidNotReceiveWithAnyArgs().DirectoryExists(default!);
            fileSystem.DidNotReceiveWithAnyArgs().DirectoryHasEntries(default!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveProjectAsync_NonemptyDirectoryWithDeclinedFactory_RemainsNotResolved(bool directoryEntry)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-project-nonempty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            if (directoryEntry)
            {
                Directory.CreateDirectory(Path.Combine(directory, ".git"));
            }
            else
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "unrecognized.json"), "{");
            }

            FunctionsProjectResolver resolver = NewResolver([NewFactory(ProjectCreationResults.NotCreated("not mine"))]);

            ProjectResolutionResult result = await resolver.ResolveProjectAsync(
                new ProjectResolutionContext(WorkingDirectory.FromExplicit(directory)),
                CancellationToken.None);

            result.Should().BeOfType<ProjectResolutionResult.NotResolved>()
                .Which.Message.Should().Contain("Run 'func setup'");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveProjectAsync_MissingDirectoryWithDeclinedFactory_RemainsNotResolved()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-project-missing-{Guid.NewGuid():N}");
        FunctionsProjectResolver resolver = NewResolver([NewFactory(ProjectCreationResults.NotCreated("not mine"))]);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(
            new ProjectResolutionContext(WorkingDirectory.FromExplicit(directory)),
            CancellationToken.None);

        result.Should().BeOfType<ProjectResolutionResult.NotResolved>()
            .Which.Message.Should().Be(InspectionFailureMessage);
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveProjectAsync_FactoryInspectionFailure_IsNotClassifiedAsUninitialized()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-project-inaccessible-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            IFunctionsProjectFactory factory = Substitute.For<IFunctionsProjectFactory>();
            factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<ProjectCreationResult>(new UnauthorizedAccessException("inspection denied")));
            FunctionsProjectResolver resolver = NewResolver([factory]);

            UnauthorizedAccessException exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                resolver.ResolveProjectAsync(
                    new ProjectResolutionContext(WorkingDirectory.FromExplicit(directory)),
                    CancellationToken.None));

            exception.Message.Should().Be("inspection denied");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResolveProjectAsync_DirectoryInspectionFails_ReturnsActionableNotResolved(int failureKind)
    {
        Exception failure = failureKind switch
        {
            0 => new UnauthorizedAccessException("access denied"),
            1 => new DirectoryNotFoundException("directory disappeared"),
            2 => new IOException("inspection failed"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
        };
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        string path = _workingDirectory.Info.FullName;
        fileSystem.DirectoryHasEntries(path).Returns(_ => throw failure);
        FunctionsProjectResolver resolver = NewResolver([NewFactory(ProjectCreationResults.NotCreated("not mine"))], fileSystem);

        ProjectResolutionResult result = await resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None);

        result.Should().BeOfType<ProjectResolutionResult.NotResolved>().Which.Message.Should().Be(InspectionFailureMessage);
        fileSystem.Received(1).DirectoryHasEntries(path);
    }

    [Fact]
    public async Task ResolveProjectAsync_PreCanceledWithoutFactories_PropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        FunctionsProjectResolver resolver = NewResolver([], fileSystem);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveProjectAsync(CreateContext(), cancellation.Token));

        fileSystem.DidNotReceiveWithAnyArgs().DirectoryHasEntries(default!);
    }

    [Fact]
    public async Task ResolveProjectAsync_CanceledAfterFactoryDeclines_PropagatesBeforeInspection()
    {
        using CancellationTokenSource cancellation = new();
        IFunctionsProjectFactory factory = Substitute.For<IFunctionsProjectFactory>();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult(ProjectCreationResults.NotCreated("not mine"));
            });
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        FunctionsProjectResolver resolver = NewResolver([factory], fileSystem);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveProjectAsync(CreateContext(), cancellation.Token));

        fileSystem.DidNotReceiveWithAnyArgs().DirectoryHasEntries(default!);
    }

    [Fact]
    public async Task ResolveProjectAsync_CanceledDuringInspection_PropagatesInsteadOfUninitialized()
    {
        using CancellationTokenSource cancellation = new();
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryHasEntries(_workingDirectory.Info.FullName).Returns(_ =>
        {
            cancellation.Cancel();
            return false;
        });
        FunctionsProjectResolver resolver = NewResolver([NewFactory(ProjectCreationResults.NotCreated("not mine"))], fileSystem);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveProjectAsync(CreateContext(), cancellation.Token));
    }

    [Fact]
    public async Task ResolveProjectAsync_InspectionThrowsCancellation_Propagates()
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryHasEntries(_workingDirectory.Info.FullName).Returns(_ => throw new OperationCanceledException());
        FunctionsProjectResolver resolver = NewResolver([NewFactory(ProjectCreationResults.NotCreated("not mine"))], fileSystem);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None));
    }

    [Fact]
    public async Task ResolveProjectAsync_UnexpectedInspectionFailure_Propagates()
    {
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        fileSystem.DirectoryHasEntries(_workingDirectory.Info.FullName).Returns(_ => throw new InvalidOperationException("unexpected"));
        FunctionsProjectResolver resolver = NewResolver([NewFactory(ProjectCreationResults.NotCreated("not mine"))], fileSystem);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None));
        exception.Message.Should().Be("unexpected");
    }

    [Fact]
    public async Task ResolveProjectAsync_NullFactoryResult_DoesNotClassifyAsUninitialized()
    {
        IFunctionsProjectFactory factory = Substitute.For<IFunctionsProjectFactory>();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProjectCreationResult>(null!));
        IFileSystem fileSystem = Substitute.For<IFileSystem>();
        FunctionsProjectResolver resolver = NewResolver([factory], fileSystem);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveProjectAsync(CreateContext(), CancellationToken.None));
        fileSystem.DidNotReceiveWithAnyArgs().DirectoryHasEntries(default!);
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        Action act = () => _ = new FunctionsProjectResolver([], null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
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

    private FunctionsProjectResolver NewResolver(IReadOnlyList<IFunctionsProjectFactory> factories, IFileSystem? fileSystem = null)
        => new(
            factories.Select((factory, index) => new WorkloadProjectFactoryRegistration(
                TestWorkloads.CreateInfo($"Test.Workload.{index}"),
                factory)),
            fileSystem ?? new PhysicalFileSystem());

    private static IFunctionsProjectFactory NewFactory(ProjectCreationResult result)
    {
        IFunctionsProjectFactory factory = Substitute.For<IFunctionsProjectFactory>();
        factory.TryCreateProjectAsync(Arg.Any<ProjectCreationContext>(), Arg.Any<CancellationToken>())
            .Returns(result);
        return factory;
    }
}
