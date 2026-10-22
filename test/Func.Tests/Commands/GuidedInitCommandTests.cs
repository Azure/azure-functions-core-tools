// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class GuidedInitCommandTests
{
    [Theory]
    [InlineData("--dry-run")]
    [InlineData("--non-interactive")]
    [InlineData("--acquire-quickstart")]
    [InlineData("--quickstart", "sample")]
    [InlineData("--template", "unknown")]
    public async Task GuidedOptions_WithoutOptIn_RejectWithoutCallingCoordinatorOrCreatingTarget(params string[] arguments)
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        var command = Command(interaction, coordinator);
        RootCommand root = [command];
        string target = Path.Combine(Path.GetTempPath(), "func-guided-unused-" + Guid.NewGuid().ToString("N"));

        int exit = await root.Parse(["init", target, .. arguments]).InvokeAsync();

        exit.Should().Be(1);
        interaction.AllOutput.Should().Contain("--guided");
        coordinator.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(target).Should().BeFalse();
    }

    [Theory]
    [InlineData("--yes")]
    [InlineData("--install")]
    [InlineData("--source", "https://unreviewed.invalid")]
    [InlineData("--package", "Unreviewed.Package")]
    [InlineData("-guided")]
    public void RetiredOrUnknownAuthorizationGrammar_IsStrictlyRejected(params string[] arguments)
    {
        RootCommand root = [Command(new(), Substitute.For<IQuickstartInitCoordinator>())];

        var parsed = root.Parse(["init", "--guided", .. arguments], new() { EnablePosixBundling = false });

        parsed.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AcquisitionFlag_WithoutCatalogSelection_DoesNotAuthorizeInstalledTemplate()
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        RootCommand root = [Command(interaction, coordinator)];

        int exit = await root.Parse(["init", "--guided", "--template", "installed", "--acquire-quickstart"]).InvokeAsync();

        exit.Should().Be(1);
        coordinator.ReceivedCalls().Should().BeEmpty();
        interaction.AllOutput.Should().Contain("requires an explicit --quickstart");
    }

    [Fact]
    public async Task GuidedRoute_WithInitializerSpecificOption_RejectsInsteadOfIgnoringIt()
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        var command = Command(interaction, coordinator);
        command.Options.Add(new Option<bool>("--initializer-option"));
        RootCommand root = [command];

        int exit = await root.Parse(["init", "--guided", "--initializer-option"]).InvokeAsync();

        exit.Should().Be(1);
        coordinator.ReceivedCalls().Should().BeEmpty();
        interaction.AllOutput.Should().Contain("ordinary init");
    }

    [Fact]
    public void Help_DescribesStagedScopeAndSeparateAcquisitionAuthorization()
    {
        var command = Command(new(), Substitute.For<IQuickstartInitCoordinator>());

        command.GuidedOption.Description.Should().Contain("staged").And.Contain("ordinary init");
        command.AcquireQuickstartOption.Description.Should().Contain("Explicit script authorization").And.Contain("--quickstart");
        command.TemplateOption.Description.Should().Contain("never trigger acquisition");
        command.DryRunOption.Description.Should().Contain("without writes");
        command.Options.Should().OnlyContain(option => !string.IsNullOrWhiteSpace(option.Description));
    }

    [Fact]
    public async Task PublicHelp_RendersOptInAndAcquisitionGrammarWithoutDispatch()
    {
        var interaction = new TestInteractionService();
        var root = TestParser.CreateRoot(interaction);

        int exit = await root.Parse(["init", "--help"]).InvokeAsync();

        exit.Should().Be(0);
        interaction.AllOutput.Should().Contain("--guided").And.Contain("--acquire-quickstart").And.Contain("--template");
        interaction.AllOutput.Should().Contain("staged");
    }

    [Fact]
    public async Task ResponseFile_PreservesPublicExpansionAndExplicitAcquisitionFlag()
    {
        string rootPath = Directory.CreateTempSubdirectory("func-guided-response-").FullName;
        try
        {
            string target = Path.Combine(rootPath, "new-project");
            string response = Path.Combine(rootPath, "init.rsp");
            File.WriteAllText(response, $"\"{target}\" --guided --stack node --quickstart fixture --acquire-quickstart --non-interactive");
            var coordinator = Substitute.For<IQuickstartInitCoordinator>();
            coordinator.ExecuteAsync(Arg.Any<QuickstartInitRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InitExecutionResult("fixtureBoundary", true));
            RootCommand root = [Command(new(), coordinator)];

            int exit = await root.Parse(["init", "@" + response]).InvokeAsync();

            exit.Should().Be(0);
            await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request => request.AuthorizeAcquisition
                && request.QuickstartId == "fixture" && request.Init.NonInteractive
                && request.Init.WorkingDirectory.Info.FullName == target), Arg.Any<CancellationToken>());
            Directory.Exists(target).Should().BeFalse("only the coordinator may create output on the guided route");
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task GuidedRoute_RecursiveGlobalOption_RemainsSupportedAndReserved()
    {
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        coordinator.ExecuteAsync(Arg.Any<QuickstartInitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitExecutionResult("fixtureBoundary", true));
        FuncRootCommand root = [Command(new(), coordinator)];

        int exit = await root.Parse(["init", "--guided", "--stack", "node", "--template", "installed", "--verbose"]).InvokeAsync();

        exit.Should().Be(0);
        await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request =>
            request.Init.ReservedAliases.Contains("--verbose")), Arg.Any<CancellationToken>());
    }

    private static InitCommand Command(TestInteractionService interaction, IQuickstartInitCoordinator coordinator)
        => new(interaction, Substitute.For<IWorkloadHintRenderer>(), Substitute.For<ILocalSettingsProvider>(),
            Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(),
            new(Substitute.For<IInstalledBundleWorkloads>()), Substitute.For<IFileSystem>(), [], coordinator);
}
