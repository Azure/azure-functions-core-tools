// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Containers;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Node;
using Azure.Functions.Cli.Workloads.Python;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Containers;

public sealed class RealContainerArtifactProviderTests
{
    [Theory]
    [InlineData("python")]
    [InlineData("node")]
    public async Task GenerateAsync_RealWorkloadProvider_WritesAllFlexByocArtifacts(string stack)
    {
        string projectPath = Path.Combine(Path.GetTempPath(), $"func-container-provider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectPath);

        try
        {
            ServiceCollection services = new();
            var builder = new TestFunctionsCliBuilder(services);
            if (stack == "python")
            {
                new PythonWorkload().Configure(builder);
                File.WriteAllText(Path.Combine(projectPath, "requirements.txt"), string.Empty);
            }
            else
            {
                new NodeWorkload().Configure(builder);
                File.WriteAllText(Path.Combine(projectPath, "package.json"), """{"name":"sample"}""");
                File.WriteAllText(Path.Combine(projectPath, "index.js"), string.Empty);
            }

            IFunctionsProjectFactory factory = builder.ProjectFactories.Should().ContainSingle().Subject;
            ProjectCreationResult creation = await factory.TryCreateProjectAsync(
                new ProjectCreationContext(WorkingDirectory.FromExplicit(projectPath)),
                CancellationToken.None);
            FunctionsProject project = creation.Should().BeOfType<ProjectCreationResult.Created>().Subject.Project;
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            IFileSystem fileSystem = Substitute.For<IFileSystem>();
            var orchestrator = new ContainerArtifactOrchestrator(
                serviceProvider.GetServices<IContainerArtifactProvider>(),
                new ContainerArtifactWriter(fileSystem));

            IReadOnlyList<ContainerArtifact> artifacts = await orchestrator.GenerateAsync(
                project,
                ContainerArtifactCapability.FlexByoc,
                projectInitializationSkipped: true,
                new Dictionary<string, string?>(),
                force: false,
                CancellationToken.None);

            artifacts.Select(artifact => artifact.RelativePath).Should().Equal("Dockerfile", "entrypoint.sh", ".dockerignore");
            artifacts.Should().OnlyContain(artifact => !artifact.Content.Contains("{{", StringComparison.Ordinal));
            await fileSystem.Received(3).WriteAllTextAsync(
                Arg.Is<string>(path => Path.GetDirectoryName(path) == projectPath),
                Arg.Any<string>(),
                CancellationToken.None);
        }
        finally
        {
            Directory.Delete(projectPath, recursive: true);
        }
    }

    private sealed class TestFunctionsCliBuilder(IServiceCollection services) : FunctionsCliBuilder
    {
        public override IServiceCollection Services { get; } =
            services ?? throw new ArgumentNullException(nameof(services));

        public List<IFunctionsProjectFactory> ProjectFactories { get; } = [];

        public override void RegisterCommand(FuncCommand command)
        {
        }

        public override void RegisterCommand<TCommand>()
        {
        }

        public override void RegisterCommand(Type commandType)
        {
        }

        public override void AddProjectFactory(IFunctionsProjectFactory factory)
        {
            ProjectFactories.Add(factory);
        }

        public override void AddProjectFactory<TFactory>()
        {
            Services.AddSingleton<IFunctionsProjectFactory, TFactory>();
        }
    }
}
