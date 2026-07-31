// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Search;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;
    private readonly IFuncTemplatePackageService _packageService;
    private readonly IFuncTemplateSearchService _searchService;
    private readonly NewCommandRunner _runner;

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
        _packageService = Substitute.For<IFuncTemplatePackageService>();
        _searchService = Substitute.For<IFuncTemplateSearchService>();

        // The project resolver deliberately returns "not resolved": the
        // lifecycle modes must work with no Functions project (D30). If a
        // dispatch path accidentally resolved a project this substitute would
        // surface it.
        Cli.Projects.IFunctionsProjectResolver projectResolver = Substitute.For<Cli.Projects.IFunctionsProjectResolver>();

        _runner = new NewCommandRunner(
            _interaction,
            projectResolver,
            Substitute.For<Cli.Profiles.IProfileResolver>(),
            Substitute.For<Microsoft.Extensions.Options.IOptionsMonitor<Cli.Configuration.StackOptions>>(),
            System.Array.Empty<Cli.Projects.IProjectInitializer>(),
            new TemplateOptionHydrator(System.Array.Empty<Cli.Projects.IProjectInitializer>()),
            new TemplatePicker(_interaction),
            new NewCommandRenderer(_interaction),
            Substitute.For<IFuncTemplateCatalog>(),
            Substitute.For<IFuncTemplateScaffolder>(),
            _packageService,
            Substitute.For<IFuncExtensionBundleContextAccessor>(),
            Substitute.For<Cli.Bundles.IHostJsonBundleSectionReader>(),
            Substitute.For<Cli.Bundles.IExtensionBundleResolver>(),
            _searchService);
    }

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        var cmd = new NewCommand(_runner);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--name");
        optionNames.Should().Contain("--template");
        optionNames.Should().Contain("--force");
        optionNames.Should().Contain("--non-interactive");
        optionNames.Should().Contain("--list");
    }

    [Fact]
    public void NewCommand_HasLifecycleAndSearchOptions()
    {
        var cmd = new NewCommand(_runner);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--install");
        optionNames.Should().Contain("--uninstall");
        optionNames.Should().Contain("--update");
        optionNames.Should().Contain("--all");
        optionNames.Should().Contain("--source");
        optionNames.Should().Contain("--search");
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        names.Should().Contain("new");
    }

    [Fact]
    public void NewCommand_HasPathArgument()
    {
        var cmd = new NewCommand(_runner);
        cmd.Arguments.Should().ContainSingle();
        cmd.Arguments[0].Name.Should().Be("path");
    }

    // Single-dash typos like `-name` must surface as unrecognized options, not "needs a project".
    [Fact]
    public void NewCommand_SingleDashLongOption_ReportsUnrecognizedOption()
    {
        var root = TestParser.CreateRoot(_interaction);

        ParseResult result = root.Parse(
            new[] { "new", "--template", "HttpTrigger-Python", "-name", "ttpt" },
            new ParserConfiguration { EnablePosixBundling = false });

        result.Errors.Should().Contain(e => e.Message.Contains("Unrecognized option '-name'", System.StringComparison.Ordinal));
    }

    [Fact]
    public void NewCommand_InstallAndList_AreMutuallyExclusive()
    {
        var root = TestParser.CreateRoot(_interaction);

        ParseResult result = root.Parse(new[] { "new", "--install", "Some.Pkg", "--list" });

        result.Errors.Should().Contain(e => e.Message.Contains("only one of", System.StringComparison.Ordinal));
    }

    [Fact]
    public void NewCommand_AllWithoutUpdate_IsRejected()
    {
        var root = TestParser.CreateRoot(_interaction);

        ParseResult result = root.Parse(new[] { "new", "--all" });

        result.Errors.Should().Contain(e => e.Message.Contains("--all can only be used with --update", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task NewCommand_Install_BypassesProjectGate_AndRendersResult()
    {
        var package = new InstalledTemplatePackage("Some.Pkg", "1.2.3", "feed", null);
        _packageService.InstallAsync(Arg.Any<TemplatePackageInstallRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TemplatePackageInstallResult.Installed(package));

        int exit = await InvokeAsync(new NewCommand(_runner), "--install", "Some.Pkg::1.2.3");

        exit.Should().Be(0);
        _interaction.AllOutput.Should().Contain("Installed template package 'Some.Pkg'");
        await _packageService.Received(1).InstallAsync(
            Arg.Is<TemplatePackageInstallRequest>(r => r.PackageIdentifier == "Some.Pkg" && r.Version == "1.2.3"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewCommand_Search_RendersResults()
    {
        _searchService
            .SearchAsync(Arg.Any<FuncSearchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FuncSearchResults(
                "queue",
                Source: null,
                [
                    new FuncSearchPackageResult(
                        "Some.Templates",
                        "1.0.0",
                        [new FuncSearchTemplateResult("Queue trigger", ["queue"], "node", "javascript")],
                        new FuncTemplateInstalledState.NotInstalled()),
                ]));

        int exit = await InvokeAsync(new NewCommand(_runner), "--search", "queue");

        exit.Should().Be(0);
        _interaction.AllOutput.Should().Contain("Some.Templates");
        await _searchService.Received(1).SearchAsync(
            Arg.Is<FuncSearchRequest>(r => r.Term == "queue" && r.Source == null),
            Arg.Any<CancellationToken>());
    }

    private static Task<int> InvokeAsync(FuncCliCommand command, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(command);
        ParseResult result = root.Parse(new[] { command.Name }.Concat(args).ToArray());
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return result.InvokeAsync(config);
    }
}
