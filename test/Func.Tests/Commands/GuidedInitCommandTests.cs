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
    [Fact]
    public async Task NativeArguments_PublicParser_ForwardsExactTokensAndReservesInitializerAndGlobalAliases()
    {
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        coordinator.ExecuteAsync(Arg.Any<QuickstartInitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitExecutionResult("fixtureBoundary", true));
        var command = Command(new(), coordinator);
        command.Options.Add(new Option<string>("--initializer-option", "-i"));
        FuncRootCommand root = [command];
        string target = Path.Combine(Path.GetTempPath(), "func-native-unused-" + Guid.NewGuid().ToString("N"));
        string[] raw = ["--greeting", "hello world", "--enabled=false", "--param:Stack", "custom", "-p:i", "literal", "--value", "@literal"];

        var parsed = root.Parse(["init", target, "--stack", "node", "--template", "Fixture.Full.Identity",
            "--name", "Project with spaces", .. raw.Select(token => "--template-argument=" + token)], new() { EnablePosixBundling = false });
        int exit = await parsed.InvokeAsync();

        parsed.Errors.Should().BeEmpty();
        exit.Should().Be(0);
        await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request =>
            request.Init.RawTemplateTokens.SequenceEqual(raw)
            && request.Init.Name == "Project with spaces"
            && request.Init.WorkingDirectory.Info.FullName == target
            && request.Init.TemplateReference == "Fixture.Full.Identity"
            && request.Init.ReservedAliases.Contains("--initializer-option")
            && request.Init.ReservedAliases.Contains("-i")
            && request.Init.ReservedAliases.Contains("--verbose")
            && request.Init.ReservedAliases.Contains("--template-argument")), Arg.Any<CancellationToken>());
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task NativeArguments_ResponseFileExpandsNestedReferencesAndPreservesQuotedAndInlineValues()
    {
        string rootPath = Directory.CreateTempSubdirectory("func-native-response-").FullName;
        try
        {
            string target = Path.Combine(rootPath, "new project");
            string nested = Path.Combine(rootPath, "nested.rsp");
            string response = Path.Combine(rootPath, "init.rsp");
            File.WriteAllText(nested, "--stack node --template Fixture.Full.Identity --non-interactive");
            File.WriteAllText(response, $"\"{target}\" @{nested} --template-argument=--greeting --template-argument \"hello world\" "
                + $"--template-argument=--value --template-argument=@{nested} --template-argument=--enabled=false "
                + "--template-argument=--path --template-argument \"C:\\fixtures\\new folder\"");
            var coordinator = Substitute.For<IQuickstartInitCoordinator>();
            coordinator.ExecuteAsync(Arg.Any<QuickstartInitRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InitExecutionResult("fixtureBoundary", true));
            RootCommand root = [Command(new(), coordinator)];

            var parsed = root.Parse(["init", "@" + response]);
            int exit = await parsed.InvokeAsync();

            parsed.Errors.Should().BeEmpty();
            exit.Should().Be(0);
            await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request =>
                request.Init.RawTemplateTokens.SequenceEqual(new[] { "--greeting", "hello world", "--value", "@" + nested, "--enabled=false", "--path", "C:\\fixtures\\new folder" })
                && request.Init.NonInteractive && request.Init.WorkingDirectory.Info.FullName == target),
                Arg.Any<CancellationToken>());
            Directory.Exists(target).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("menu")]
    [InlineData("omitted-target")]
    [InlineData("initializer")]
    public async Task NativeArguments_InvalidScopeRejectsBeforeDispatchOrDirectoryCreation(string mode)
    {
        string target = Path.Combine(Path.GetTempPath(), "func-native-rejected-" + Guid.NewGuid().ToString("N"));
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        var command = Command(interaction, coordinator);
        command.Options.Add(new Option<bool>("--initializer-option"));
        RootCommand root = [command];
        string[] tokens = mode switch
        {
            "legacy" => [target],
            "menu" => [target],
            "omitted-target" => ["--template", "installed"],
            _ => [target, "--template", "installed", "--initializer-option"],
        };

        int exit = await root.Parse(["init", .. tokens, "--template-argument=--greeting", "--template-argument=hello"]).InvokeAsync();

        exit.Should().Be(1);
        coordinator.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(target).Should().BeFalse();
        interaction.AllOutput.Should().Contain(mode == "initializer" ? "ordinary init" : "requires an explicit target path");
    }

    [Theory]
    [InlineData("--typo")]
    [InlineData("-xyz")]
    [InlineData("stray")]
    public async Task NativeArguments_UnknownHostTokenStillProducesParserErrorBeforeDispatch(string unknown)
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        var command = Command(interaction, coordinator);
        RootCommand root = [command];
        string target = Path.Combine(Path.GetTempPath(), "func-native-typo-" + Guid.NewGuid().ToString("N"));

        var parsed = root.Parse(["init", target, "--template", "installed", unknown,
            "--template-argument=--greeting", "--template-argument=hello"],
            new() { EnablePosixBundling = false });
        int exit = await parsed.InvokeAsync();

        command.TreatUnmatchedTokensAsErrors.Should().BeTrue();
        parsed.Errors.Should().NotBeEmpty();
        exit.Should().Be(1);
        coordinator.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public void NativeArguments_StandardDelimiterDoesNotEnableCatchallArguments()
    {
        RootCommand root = [Command(new(), Substitute.For<IQuickstartInitCoordinator>())];

        var parsed = root.Parse(["init", "--template", "installed", "--", "--greeting", "hello"]);

        parsed.Errors.Should().Contain(error => error.Message.Contains("Unrecognized option '--greeting'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NativeArguments_InlineExistingResponseReferenceRemainsLiteral()
    {
        string rootPath = Directory.CreateTempSubdirectory("func-native-literal-").FullName;
        try
        {
            string response = Path.Combine(rootPath, "flags.rsp");
            File.WriteAllText(response, "--acquire-quickstart --quickstart injected");
            var coordinator = Substitute.For<IQuickstartInitCoordinator>();
            coordinator.ExecuteAsync(Arg.Any<QuickstartInitRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InitExecutionResult("fixtureBoundary", true));
            RootCommand root = [Command(new(), coordinator)];

            int exit = await root.Parse(["init", Path.Combine(rootPath, "target"), "--template", "installed",
                "--template-argument=--greeting", "--template-argument=@" + response]).InvokeAsync();

            exit.Should().Be(0);
            await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request =>
                !request.AuthorizeAcquisition && request.QuickstartId == null
                && request.Init.RawTemplateTokens.SequenceEqual(new[] { "--greeting", "@" + response })),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task NativeArguments_NestedResponseValuesRemainRawTokensNotHostOptions()
    {
        string rootPath = Directory.CreateTempSubdirectory("func-native-nested-").FullName;
        try
        {
            string nested = Path.Combine(rootPath, "tail.rsp");
            string flags = Path.Combine(rootPath, "flags.rsp");
            string response = Path.Combine(rootPath, "init.rsp");
            File.WriteAllText(flags, "--template-argument=--acquire-quickstart --template-argument=--guided --template-argument=--stack --template-argument=injected");
            File.WriteAllText(nested, $"--template-argument=--greeting --template-argument \"nested words\" @{flags}");
            File.WriteAllText(response, $"\"{Path.Combine(rootPath, "target")}\" --stack node --quickstart fixture @{nested}");
            var coordinator = Substitute.For<IQuickstartInitCoordinator>();
            coordinator.ExecuteAsync(Arg.Any<QuickstartInitRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InitExecutionResult("fixtureBoundary", true));
            RootCommand root = [Command(new(), coordinator)];

            int exit = await root.Parse(["init", "@" + response]).InvokeAsync();

            exit.Should().Be(0);
            await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request =>
                request.AuthorizeAcquisition && request.QuickstartId == "fixture" && request.Init.ExplicitStack == "node"
                && request.Init.RawTemplateTokens.SequenceEqual(new[] { "--greeting", "nested words", "--acquire-quickstart", "--guided", "--stack", "injected" })),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void NativeArguments_MissingNestedResponseStillProducesPublicParserError()
    {
        string rootPath = Directory.CreateTempSubdirectory("func-native-missing-").FullName;
        try
        {
            string response = Path.Combine(rootPath, "init.rsp");
            File.WriteAllText(response, $"target --template installed --template-argument @{Path.Combine(rootPath, "missing.rsp")}");
            RootCommand root = [Command(new(), Substitute.For<IQuickstartInitCoordinator>())];

            var parsed = root.Parse(["init", "@" + response]);

            parsed.Errors.Should().Contain(error => error.Message.Contains("Response file not found", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact]
    public void NativeArguments_ResponseDelimiterDoesNotEnableUnmatchedNativeArguments()
    {
        string rootPath = Directory.CreateTempSubdirectory("func-native-delimiter-").FullName;
        try
        {
            string nested = Path.Combine(rootPath, "tail.rsp");
            string response = Path.Combine(rootPath, "init.rsp");
            File.WriteAllText(nested, "-- --greeting \"quoted words\"");
            File.WriteAllText(response, $"target --template installed @{nested}");
            RootCommand root = [Command(new(), Substitute.For<IQuickstartInitCoordinator>())];

            var parsed = root.Parse(["init", "@" + response]);

            parsed.Errors.Should().Contain(error => error.Message.Contains("--greeting", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Theory]
    [InlineData("--template-argument")]
    [InlineData("--template-argument=")]
    [InlineData("--template-argument", "")]
    [InlineData("--template-argument", "--stack", "node", "--template-argument=value")]
    [InlineData("--template-argument=one", "--template-argument", "--stack", "node")]
    [InlineData("--template-argument=one", "stray")]
    public void NativeArguments_EachOccurrenceRequiresExactlyOneToken(params string[] arguments)
    {
        RootCommand root = [Command(new(), Substitute.For<IQuickstartInitCoordinator>())];

        var parsed = root.Parse(["init", "target", "--template", "installed", .. arguments]);

        parsed.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task QuickstartOption_RoutesAndAuthorizesAcquisition()
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        coordinator.ExecuteAsync(Arg.Any<QuickstartInitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new InitExecutionResult("fixtureBoundary", true));
        RootCommand root = [Command(interaction, coordinator)];
        string target = Path.Combine(Path.GetTempPath(), "func-quickstart-implied-" + Guid.NewGuid().ToString("N"));

        int exit = await root.Parse(["init", target, "--quickstart", "sample"]).InvokeAsync();

        exit.Should().Be(0);
        await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request =>
            request.QuickstartId == "sample" && request.AuthorizeAcquisition), Arg.Any<CancellationToken>());
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task RemovedGuidedOption_RejectsWithoutDispatchOrTargetMutation()
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        RootCommand root = [Command(interaction, coordinator)];
        string target = Path.Combine(Path.GetTempPath(), "func-retired-guided-" + Guid.NewGuid().ToString("N"));

        var parsed = root.Parse(["init", target, "--guided", "--quickstart", "sample"]);
        int exit = await parsed.InvokeAsync();

        parsed.Errors.Should().Contain(error => error.Message.Contains("--guided", StringComparison.Ordinal));
        exit.Should().Be(1);
        coordinator.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(target).Should().BeFalse();
    }

    [Theory]
    [InlineData("--guided")]
    [InlineData("--yes")]
    [InlineData("--install")]
    [InlineData("--source", "https://unreviewed.invalid")]
    [InlineData("--package", "Unreviewed.Package")]
    [InlineData("-guided")]
    public void UnsupportedAuthorizationGrammar_IsStrictlyRejected(params string[] arguments)
    {
        RootCommand root = [Command(new(), Substitute.For<IQuickstartInitCoordinator>())];

        var parsed = root.Parse(["init", .. arguments], new() { EnablePosixBundling = false });

        parsed.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AcquisitionFlag_WithoutCatalogSelection_DoesNotAuthorizeInstalledTemplate()
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        RootCommand root = [Command(interaction, coordinator)];

        int exit = await root.Parse(["init", "--template", "installed", "--acquire-quickstart"]).InvokeAsync();

        exit.Should().Be(1);
        coordinator.ReceivedCalls().Should().BeEmpty();
        interaction.AllOutput.Should().Contain("requires an explicit --quickstart");
    }

    [Fact]
    public async Task TemplateRoute_WithInitializerSpecificOption_RejectsInsteadOfIgnoringIt()
    {
        var interaction = new TestInteractionService();
        var coordinator = Substitute.For<IQuickstartInitCoordinator>();
        var command = Command(interaction, coordinator);
        command.Options.Add(new Option<bool>("--initializer-option"));
        RootCommand root = [command];

        int exit = await root.Parse(["init", "--template", "installed", "--initializer-option"]).InvokeAsync();

        exit.Should().Be(1);
        coordinator.ReceivedCalls().Should().BeEmpty();
        interaction.AllOutput.Should().Contain("ordinary init");
    }

    [Fact]
    public void Help_DescribesTemplateAndAcquisitionAuthorization()
    {
        var command = Command(new(), Substitute.For<IQuickstartInitCoordinator>());

        command.Options.Should().NotContain(option => option.Aliases.Contains("--guided"));
        command.QuickstartOption.Description.Should().Contain("qualified acquisition").And.Contain("project creation");
        command.AcquireQuickstartOption.Description.Should().Contain("Explicit authorization").And.Contain("--quickstart");
        command.TemplateOption.Description.Should().Contain("never trigger acquisition");
        command.DryRunOption.Description.Should().Contain("without writes");
        command.TemplateArgumentOption.Description.Should().Contain("--template-argument=--symbol").And.Contain("explicit path");
        command.Options.Should().OnlyContain(option => !string.IsNullOrWhiteSpace(option.Description));
    }

    [Fact]
    public async Task PublicHelp_RendersTemplateAndAcquisitionGrammarWithoutDispatch()
    {
        var interaction = new TestInteractionService();
        var root = TestParser.CreateRoot(interaction);

        int exit = await root.Parse(["init", "--help"]).InvokeAsync();

        exit.Should().Be(0);
        interaction.AllOutput.Should().NotContain("--guided");
        interaction.AllOutput.Should().Contain("--acquire-quickstart").And.Contain("--template");
        interaction.AllOutput.Should().NotContain("--template-argument");
        interaction.AllOutput.Should().Contain("func init MyApp").And.Contain("Quickstarts need no template parameters");
        interaction.AllOutput.Should().Contain("Exact discovery catalog ID").And.Contain("authorization");
    }

    [Fact]
    public async Task ResponseFile_PreservesPublicExpansionAndExplicitAcquisitionFlag()
    {
        string rootPath = Directory.CreateTempSubdirectory("func-guided-response-").FullName;
        try
        {
            string target = Path.Combine(rootPath, "new-project");
            string response = Path.Combine(rootPath, "init.rsp");
            File.WriteAllText(response, $"\"{target}\" --stack node --quickstart fixture --acquire-quickstart --non-interactive");
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

        int exit = await root.Parse(["init", "--stack", "node", "--template", "installed", "--verbose"]).InvokeAsync();

        exit.Should().Be(0);
        await coordinator.Received(1).ExecuteAsync(Arg.Is<QuickstartInitRequest>(request =>
            request.Init.ReservedAliases.Contains("--verbose")), Arg.Any<CancellationToken>());
    }

    private static InitCommand Command(TestInteractionService interaction, IQuickstartInitCoordinator coordinator)
        => new(interaction, Substitute.For<IWorkloadHintRenderer>(), Substitute.For<ILocalSettingsProvider>(),
            Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(),
            new(Substitute.For<IInstalledBundleWorkloads>()), Substitute.For<IFileSystem>(), [], coordinator);
}
