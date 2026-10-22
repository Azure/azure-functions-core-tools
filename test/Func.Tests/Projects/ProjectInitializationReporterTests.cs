// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects;

public sealed class ProjectInitializationReporterTests
{
    [Fact]
    public void Constructor_NullInteraction_RejectsProgrammerError()
    {
        Action create = () => new ProjectInitializationReporter(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }

    [Fact]
    public void WriteWarning_NullMessage_RejectsBeforeOutput()
    {
        var interaction = Substitute.For<IInteractionService>();
        var reporter = new ProjectInitializationReporter(interaction);

        Action write = () => reporter.WriteWarning(null!);

        write.Should().Throw<ArgumentNullException>().WithParameterName("message");
        interaction.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("A workload warning.")]
    [InlineData("[red]literal workload text[/]")]
    public void WriteWarning_UsesExistingSemanticOutputWithoutChangingText(string message)
    {
        var interaction = Substitute.For<IInteractionService>();
        var reporter = new ProjectInitializationReporter(interaction);

        reporter.WriteWarning(message);

        interaction.Received(1).WriteWarning(message);
        interaction.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostBuilder_ProvidesReporterToRootAndFreshScope(bool scoped)
    {
        var interaction = Substitute.For<IInteractionService>();
        var builder = CliHostFactory.CreateBuilder(interaction);
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var services = scoped ? scope.ServiceProvider : provider;

        services.GetRequiredService<IProjectInitializationReporter>().WriteWarning("Initialization warning.");

        interaction.Received(1).WriteWarning("Initialization warning.");
    }
}
