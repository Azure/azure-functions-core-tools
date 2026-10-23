// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitWarning_ActualLoadedInitializer_ResolvesReporterAndHonorsSkipInRootAndFreshScope(bool scoped)
    {
        await InstallAssociationStackAsync();
        using var provider = await CreateServicesAsync();
        using var scope = provider.CreateScope();
        var services = scoped ? scope.ServiceProvider : provider;
        var initializer = services.GetServices<IProjectInitializer>().Single(value => value.Stack == "node");
        initializer.GetType().Assembly.Should().NotBeSameAs(typeof(IProjectInitializer).Assembly);
        services.GetRequiredService<IProjectInitializationReporter>().Should().BeOfType<ProjectInitializationReporter>();
        var target = Directory.CreateDirectory(Path.Combine(_root, "skip-project"));
        RootCommand command = [];
        initializer.GetInitOptions(new InitOptionRegistry(command));

        await initializer.InitializeAsync(new(WorkingDirectory.FromExplicit(target.FullName), "test", "JavaScript", false),
            command.Parse(["--skip-npm-install"]), CancellationToken.None);

        File.Exists(Path.Combine(target.FullName, "package.json")).Should().BeTrue();
        File.Exists(Path.Combine(target.FullName, "host.json")).Should().BeTrue();
        _interaction.Lines.Should().NotContain(line => line.Contains("dependencies were not installed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitWarning_DirectRegistration_PreservesExplicitReporter()
    {
        ServiceCollection services = new();
        var reporter = Substitute.For<IProjectInitializationReporter>();
        services.AddSingleton(reporter);

        await WorkloadRegistration.RegisterWorkloadsAsync(services, _paths, _interaction, CancellationToken.None);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProjectInitializationReporter>().Should().BeSameAs(reporter);
        reporter.ReceivedCalls().Should().BeEmpty();
    }
}
