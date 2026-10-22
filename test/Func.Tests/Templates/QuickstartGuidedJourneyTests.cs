// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Tests.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class ArtifactBindingJourneyTests
{
    [Fact]
    public async Task NativeArguments_PublicInstalledPreviewAndCreate_UseExplicitParametersAndMandatoryConfiguration()
    {
        await SeedAsync("parameters");
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction);
        command.Options.Add(new Option<string>("--initializer-option", "-i"));
        RootCommand root = [command];
        string[] arguments = ["init", Target, "--language", "JavaScript", "--template", Identity,
            "--name", "Project with spaces", "--non-interactive"];
        string[] raw = ["--required", "supplied", "--greeting", "hello world", "--enabled=false", "--count", "42",
            "--flavor", "bold", "--param:Stack", "native-stack", "-p:i", "native-init"];
        string[] argumentsForTemplate = [.. raw.Select(token => "--template-argument=" + token)];
        var before = TemplateState();

        int preview = await root.Parse([.. arguments, "--dry-run", .. argumentsForTemplate]).InvokeAsync();

        preview.Should().Be(0, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("Preview: " + Identity).And.Contain("parameters.txt").And.Contain(".func");
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().Equal(before);
        journey.Payload.Requested.Should().BeEmpty();

        int created = await root.Parse([.. arguments, .. argumentsForTemplate]).InvokeAsync();

        created.Should().Be(0, interaction.AllOutput);
        File.ReadAllText(Path.Combine(Target, "parameters.txt")).Should()
            .Be("supplied|hello world|False|42|bold|native-stack|native-init|Project_with_spaces");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact", "the higher precedence same-short-name sibling is not selected");
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(Target, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        journey.Payload.Requested.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--enabled")]
    [InlineData("--enabled", "not-bool")]
    [InlineData("--count", "not-int")]
    [InlineData("--flavor", "unknown")]
    [InlineData("--greeting", "one", "-g", "two")]
    [InlineData("--param:Stack", "one", "-p:s", "two")]
    [InlineData("--stack", "node")]
    [InlineData("--initializer-option", "ignored")]
    [InlineData("--greeting", "one", "stray")]
    [InlineData("--unknown", "ignored")]
    [InlineData("--acquire-quickstart")]
    [InlineData("--guided")]
    public async Task NativeArguments_PublicInstalledInvalidValuesOrAliases_RejectWithoutWrites(params string[] suffix)
    {
        await SeedAsync("parameters");
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction);
        command.Options.Add(new Option<string>("--initializer-option", "-i"));
        RootCommand root = [command];
        var before = TemplateState();

        int exit = await root.Parse(["init", Target, "--template", Identity, "--non-interactive",
            "--template-argument=--required", "--template-argument=supplied",
            .. suffix.Select(token => "--template-argument=" + token)]).InvokeAsync();

        exit.Should().Be(1, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("No project candidate accepts these template arguments");
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().Equal(before);
        journey.Payload.Requested.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeArguments_PublicScripts_RequireRequiredValueOrExactInstalledIdentity(bool missingRequired)
    {
        await SeedAsync("parameters");
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];
        var before = TemplateState();

        int exit = await root.Parse(["init", Target, "--template", missingRequired ? Identity : "Fixture.Missing",
            "--non-interactive", "--template-argument=--greeting", "--template-argument=optional"]).InvokeAsync();

        exit.Should().Be(1);
        interaction.AllOutput.Should().Contain(missingRequired ? "Supply all required template values: --required" : "not installed");
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().Equal(before);
        journey.Payload.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeArguments_PublicQuickstart_AuthorizesAcquisitionWithoutRedundantFlags()
    {
        await SeedAsync("parameters", installTemplates: false);
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--quickstart", "fixture", "--non-interactive",
            "--template-argument=--required", "--template-argument=script",
            "--template-argument=--greeting", "--template-argument=selected",
            "--template-argument=--enabled", "--template-argument=false"]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        journey.Lifecycle.ObservedFreshBinding.Should().BeTrue();
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        File.ReadAllText(Path.Combine(Target, "parameters.txt")).Should()
            .Be("script|selected|False|1|plain|default-stack|default-init|target");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task QuickstartOption_InteractiveRouteKeepsSeparateAcquisitionAndCreationConfirmations()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, [], [true, false]);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--quickstart", "fixture"]).InvokeAsync();

        exit.Should().Be(1, interaction.AllOutput);
        interaction.Confirmations.Should().Equal(false, false);
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().NotBeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task NativeArguments_PublicResponseExpandedValuesCannotNominateQuickstart()
    {
        await SeedAsync(installTemplates: false);
        string flags = Path.Combine(_root, "authorization.rsp");
        string response = Path.Combine(_root, "init.rsp");
        File.WriteAllText(flags, "--template-argument=--quickstart --template-argument=fixture");
        File.WriteAllText(response, $"\"{Target}\" --stack node --non-interactive @{flags}");
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", "@" + response]).InvokeAsync();

        exit.Should().Be(1);
        journey.Payload.Requested.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        interaction.AllOutput.Should().Contain("--quickstart catalog ID");
    }

    [Fact]
    public async Task NativeArguments_PublicResponseCreate_DoesNotReexpandInlineLiteralValue()
    {
        await SeedAsync("parameters");
        string literal = Path.Combine(_root, "literal flags.rsp");
        string nested = Path.Combine(_root, "parameters.rsp");
        string response = Path.Combine(_root, "init.rsp");
        File.WriteAllText(literal, "--acquire-quickstart --stack wrong");
        File.WriteAllText(nested, "--template-argument=--required --template-argument=from-response");
        File.WriteAllText(response, $"\"{Target}\" --stack node --template {Identity} --non-interactive @{nested} "
            + $"--template-argument=--greeting \"--template-argument=@{literal}\"");
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", "@" + response]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        File.ReadAllText(Path.Combine(Target, "parameters.txt")).Should()
            .Be($"from-response|@{literal}|True|1|plain|default-stack|default-init|target");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        journey.Payload.Requested.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("-literal")]
    public async Task NativeArguments_PublicInlineStringValue_DoesNotUseDefaultOrDropLiteral(string value)
    {
        await SeedAsync("parameters");
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--template", Identity, "--non-interactive",
            "--template-argument=--required", "--template-argument=supplied", "--template-argument=--greeting=" + value]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        File.ReadAllText(Path.Combine(Target, "parameters.txt")).Should()
            .Be($"supplied|{value}|True|1|plain|default-stack|default-init|target");
        journey.Payload.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultInit_InstalledTemplateRequiredPrompt_RemainsAvailableWithoutExplicitNativeArguments()
    {
        await SeedAsync("parameters");
        GuidedInteraction interaction = new(true, ["exact"], [true], ["prompted"]);
        var journey = Guided(interaction);
        IProjectInitializer initializer = NodeInitializer();
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction, [initializer])];

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("INPUT: --required (string)");
        File.ReadAllText(Path.Combine(Target, "parameters.txt")).Should()
            .Be("prompted|hello|True|1|plain|default-stack|default-init|target");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        journey.Payload.Requested.Should().BeEmpty();
        await initializer.DidNotReceive().InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("force")]
    [InlineData("adopt")]
    [InlineData("heal")]
    [InlineData("dry-run")]
    public async Task NativeArguments_PublicProtectedModes_DoNotAcquireCleanOrRewriteExistingFiles(string mode)
    {
        await SeedAsync("parameters", installTemplates: false);
        if (mode is "adopt" or "heal")
        {
            Directory.CreateDirectory(Target);
            File.WriteAllText(Path.Combine(Target, "host.json"), """{"version":"2.0"}""");
            if (mode == "heal")
            {
                Directory.CreateDirectory(Path.Combine(Target, ".func"));
                File.WriteAllText(Path.Combine(Target, ".func", "config.json"), """{"stack":{"runtime":"node"},"custom":"retain"}""");
            }
        }
        var before = TemplateState();
        Dictionary<string, string> targetFiles = Directory.Exists(Target)
            ? Directory.GetFiles(Target, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText)
            : [];
        GuidedInteraction interaction = new(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];
        string[] flags = mode switch { "force" => ["--force"], "dry-run" => ["--dry-run"], _ => [] };

        int exit = await root.Parse(["init", Target, "--stack", "node", "--quickstart", "fixture", "--acquire-quickstart",
            "--non-interactive", .. flags, "--template-argument=--required", "--template-argument=supplied"]).InvokeAsync();

        exit.Should().Be(1);
        journey.Payload.Requested.Should().BeEmpty();
        TemplateState().Should().Equal(before);
        if (targetFiles.Count == 0)
        {
            Directory.Exists(Target).Should().BeFalse();
        }
        else
        {
            Directory.GetFiles(Target, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText)
                .Should().BeEquivalentTo(targetFiles);
        }
        interaction.AllOutput.Should().Contain(mode switch
        {
            "force" => "does not support --force",
            "dry-run" => "Metadata intent only",
            _ => "requires an empty target",
        });
    }

    [Fact]
    public async Task DefaultInit_PublicParser_BrowseConsentRealAcquisitionFreshBoundCreateAndRepeatInstalledVisibility()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["Browse more templates", "first", "Use this template"], [true, true]);
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction, [NodeInitializer()]);
        RootCommand root = [command];

        var exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        journey.Lifecycle.Prepared.Should().NotBeNull();
        journey.Lifecycle.ObservedFreshBinding.Should().BeTrue();
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse("all prepared payload leases must be disposed before fresh bound init returns");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        interaction.Confirmations.Should().Equal(false, false);
        interaction.AllOutput.Should().Contain("Independently qualified").And.Contain("fixture-only").And.Contain(_selection.Artifact.Sha256);
        interaction.AllOutput.Should().Contain("unverified claims");
        interaction.AllOutput.Should().NotContain("Select a project stack:");
        interaction.AllOutput.Should().Contain("Basic Functions app");
        interaction.SelectionTitles.Should().Contain("Select a qualified template:");
        interaction.SelectionChoices[1].Should().Contain("Fixture (fixture) — Available to install");
        interaction.SelectionChoices.SelectMany(choices => choices).Should().NotContain(choice =>
            choice.StartsWith("View ", StringComparison.Ordinal)
            || choice.Contains("Unverified", StringComparison.Ordinal)
            || choice.Contains("Absent", StringComparison.Ordinal)
            || choice.Contains("Conflicting", StringComparison.Ordinal));

        var projection = await new QuickstartInstalledTemplates(Factory, _scopes, journey.Qualification).ProjectAsync(
            await journey.Catalog.SearchAsync(new(), CancellationToken.None), new(DirectoryTarget, _selection.Owner), CancellationToken.None);
        projection.Catalog.Entries.Should().BeEmpty();
        projection.ActionableEntries.Should().ContainSingle().Which.Availability.Should().Be(QuickstartAvailability.Installed);
        projection.Installed.Should().Contain(item => item.Entry.Identity == Identity && item.Selection != null);

        var before = TemplateState();
        var secondTarget = Path.Combine(_root, "second");
        GuidedInteraction repeatedInteraction = new(true, ["exact"], [true]);
        var repeated = Guided(repeatedInteraction);
        IProjectInitializer initializer = NodeInitializer();
        RootCommand secondRoot = [GuidedCommand(repeated.Coordinator, repeatedInteraction, [initializer])];

        int installed = await secondRoot.Parse(["init", secondTarget]).InvokeAsync();

        installed.Should().Be(0, repeatedInteraction.AllOutput);
        journey.Payload.Requested.Should().HaveCount(1, "installed selection never re-acquires");
        repeated.Payload.Requested.Should().BeEmpty();
        repeated.Lifecycle.Prepared.Should().BeNull();
        repeatedInteraction.Confirmations.Should().Equal([false], "only creation requires confirmation on repeat installed selection");
        repeatedInteraction.SelectionTitles.Should().ContainSingle().Which.Should().Be("Select a project template for Node.js (node):");
        repeatedInteraction.SelectionChoices[0].Should().Contain(option => option.Contains(Identity, StringComparison.Ordinal));
        TemplateState().Should().Equal(before);
        File.ReadAllText(Path.Combine(secondTarget, "value.txt")).Should().Be("exact");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact", "the repeat must use the fresh target");
        using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(secondTarget, ".func", "config.json")));
        configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be("node");
        configuration.RootElement.GetProperty("stack").GetProperty("language").GetString().Should().Be("JavaScript");
        using IWorkloadOperation operation = await new FileWorkloadOperationCoordinator(_paths).AcquireAsync(CancellationToken.None);
        operation.Validate(_paths);
        await initializer.DidNotReceive().InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DefaultInit_BasicSelection_UsesExistingInitializerWithoutWritingBeforeSelection()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["Basic Functions app"], []);
        interaction.SelectionMade = () => Directory.Exists(Target).Should().BeFalse();
        IProjectInitializer initializer = NodeInitializer();
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction, [initializer]);
        RootCommand root = [command];

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        await initializer.Received(1).InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(Target, "value.txt")).Should().BeFalse();
        interaction.AllOutput.Should().Contain("Select a project template for Node.js (node):").And.Contain("Basic Functions app");
        interaction.AllOutput.Should().NotContain("Select a project stack:");
        interaction.Confirmations.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultInit_BrowseBack_ReturnsToTemplateMenuThenBasicUsesOriginalInitializer()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["Browse more templates", "Back", "Basic Functions app"], []);
        interaction.SelectionMade = () => Directory.Exists(Target).Should().BeFalse();
        IProjectInitializer initializer = NodeInitializer();
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction, [initializer])];

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.SelectionTitles.Should().Equal("Select a project template for Node.js (node):",
            "Select a qualified template:", "Select a project template for Node.js (node):");
        journey.Payload.Requested.Should().BeEmpty();
        interaction.Confirmations.Should().BeEmpty();
        await initializer.Received(1).InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Browse_ActionableDuplicateLabelsRemainDistinctAndSelectTheExactQualifiedRow()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["second", "Use this template"], []);
        var journey = Guided(interaction);
        QuickstartCatalog original = await journey.Catalog.SearchAsync(new(), CancellationToken.None);
        QuickstartCatalogEntry first = original.Entries.Single();
        QuickstartCatalogEntry second = first with { Entry = first.Entry with { ShortDescription = "Second row" } };
        QuickstartCatalog duplicate = original with { Entries = Array.AsReadOnly<QuickstartCatalogEntry>([first, second]) };
        IQuickstartCatalogService catalog = Substitute.For<IQuickstartCatalogService>();
        catalog.SearchAsync(Arg.Any<QuickstartCatalogQuery>(), Arg.Any<CancellationToken>()).Returns(duplicate);
        using BoundInitScope scope = await _scopes.CreateAsync(CancellationToken.None);
        QuickstartBrowser browser = new(catalog, scope.Catalog, [], interaction, new(interaction),
            new(Factory, _scopes, journey.Qualification));

        QuickstartBrowseResult result = await browser.BrowseAsync(new("node")
        {
            AllowSelection = true,
            InstalledQuery = new(DirectoryTarget, _selection.Owner),
        }, CancellationToken.None);

        result.Outcome.Should().Be(QuickstartBrowseOutcome.Selected);
        result.Entry.Should().Be(second);
        interaction.SelectionChoices[0].Should().Contain(choice => choice.Contains("Available to install", StringComparison.Ordinal));
        interaction.SelectionChoices[0].Distinct(StringComparer.Ordinal).Should().HaveCount(interaction.SelectionChoices[0].Length);
        interaction.SelectionChoices.SelectMany(choices => choices).Should().NotContain(choice =>
            choice.StartsWith("View ", StringComparison.Ordinal)
            || choice.Contains("Unverified", StringComparison.Ordinal)
            || choice.Contains("Absent", StringComparison.Ordinal)
            || choice.Contains("Conflicting", StringComparison.Ordinal));
        journey.Payload.Requested.Should().BeEmpty();
        interaction.Confirmations.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DefaultInit_ProductionConsole_EmptyBrowseBackReturnsToMenuWhileCancelStops(bool back)
    {
        await SeedAsync(installTemplates: false);
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        for (int i = 0; i < (back ? 4 : 5); i++) stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        if (back)
        {
            stderr.Enqueue(ConsoleKey.DownArrow);
            stderr.Enqueue(ConsoleKey.DownArrow);
            stderr.Enqueue(ConsoleKey.Enter);
        }
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        IProjectInitializer initializer = NodeInitializer();
        var journey = CreateGuided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction, [initializer])];
        var before = TemplateState();

        int exit = await root.Parse(["init", Target, "--language", "TypeScript"]).InvokeAsync();

        exit.Should().Be(1, stdout.Output + stderr.Output);
        stderr.Reads.Should().Be(back ? 10 : 8);
        stderr.Output.Should().Contain("No compatible templates are available").And.Contain("Project template selection canceled")
            .And.NotContain("Guided init canceled");
        TemplateState().Should().Equal(before);
        Directory.Exists(Target).Should().BeFalse();
        journey.Payload.Requested.Should().BeEmpty();
        await initializer.DidNotReceive().InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("fixture", "quickstartStackMismatch")]
    [InlineData("unknown", "ambiguousQuickstart")]
    public async Task Guided_QuickstartUnavailableForSelectedStack_ReportsRequiredInstalledStackWithoutAcquisition(string id, string kind)
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []), scopes: new AdditionalStackScopeFactory(_scopes, includeDotNetOwner: false));
        var before = TemplateState();

        var result = await journey.Coordinator.ExecuteAsync(
            new(GuidedRequest() with { ExplicitStack = "dotnet", NonInteractive = true }, id, true), CancellationToken.None);

        result.Kind.Should().Be(id == "fixture" ? "ownerUnavailable" : kind);
        if (id == "fixture")
        {
            result.Diagnostics.Should().Contain(message => message.Contains("no unique actual loaded runtime owner", StringComparison.Ordinal));
        }
        journey.Payload.Requested.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().Equal(before);
    }

    [Fact]
    public async Task DefaultInit_MultipleStacksCancelAtStackSelectionLeavesTargetAndInstalledTemplateHiveUnchanged()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["Cancel", "first"], []);
        interaction.SelectionMade = () => Directory.Exists(Target).Should().BeFalse();
        IProjectInitializer nodeInitializer = NodeInitializer();
        IProjectInitializer[] initializers = [nodeInitializer, DotNetInitializer()];
        var journey = Guided(interaction, scopes: new AdditionalStackScopeFactory(_scopes));
        var command = GuidedCommand(journey.Coordinator, interaction, initializers);
        RootCommand root = [command];
        var before = TemplateState();

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(1);
        Directory.Exists(Target).Should().BeFalse();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
        TemplateState().Should().Equal(before);
        journey.Payload.Requested.Should().BeEmpty();
        foreach (IProjectInitializer initializer in initializers)
        {
            await initializer.DidNotReceive().InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
        }
        interaction.SelectionTitles.Should().ContainSingle().Which.Should().Be("Select a project stack:");
        interaction.SelectionChoices[0].Should().Contain(["Node.js (node)", ".NET (dotnet)"]);
        interaction.AllOutput.Should().Contain("canceled");
    }

    [Fact]
    public async Task DefaultInit_CancelAtTemplateSelectionLeavesTargetAndInstalledTemplateHiveUnchanged()
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, ["Cancel"], []);
        interaction.SelectionMade = () => Directory.Exists(Target).Should().BeFalse();
        IProjectInitializer initializer = NodeInitializer();
        var journey = Guided(interaction);
        var command = GuidedCommand(journey.Coordinator, interaction, [initializer]);
        RootCommand root = [command];
        var before = TemplateState();

        int exit = await root.Parse(["init", Target]).InvokeAsync();

        exit.Should().Be(1);
        Directory.Exists(Target).Should().BeFalse();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
        TemplateState().Should().Equal(before);
        journey.Payload.Requested.Should().BeEmpty();
        await initializer.DidNotReceive().InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
        interaction.SelectionTitles.Should().Equal("Select a project template for Node.js (node):");
        interaction.AllOutput.Should().Contain("canceled");
    }

    [Theory]
    [InlineData("decline", "canceled")]
    [InlineData("back", "canceled")]
    [InlineData("cancel", "canceled")]
    [InlineData("script", "acquisitionAuthorizationRequired")]
    [InlineData("dry-run", "payloadRequired")]
    [InlineData("force", "forceNotQualified")]
    public async Task Guided_RefusalAndReadOnlyModes_NeverAcquireOrCreate(string mode, string expected)
    {
        await SeedAsync(installTemplates: false);
        string[] selections = mode switch
        {
            "back" => ["Browse more templates", "Back", "Cancel"],
            "cancel" => ["Cancel"],
            _ => ["Browse more templates", "first", "Use this template"],
        };
        GuidedInteraction interaction = new(mode != "script", selections, [false]);
        var journey = Guided(interaction);
        var input = GuidedRequest() with { DryRun = mode == "dry-run", Force = mode == "force", NonInteractive = mode == "script" };
        var before = TemplateState();

        var result = await journey.Coordinator.ExecuteAsync(new(input, mode == "script" ? "fixture" : null,
            NativeStacks: new HashSet<string>(["node"], StringComparer.OrdinalIgnoreCase), OfferBasicSelection: true), CancellationToken.None);

        result.Kind.Should().Be(expected, interaction.AllOutput);
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().Equal(before);
    }

    [Theory]
    [InlineData("empty", "qualificationUnavailable")]
    [InlineData("role", "qualificationUnavailable")]
    [InlineData("source", "qualificationUnavailable")]
    [InlineData("digest", "qualificationUnavailable")]
    [InlineData("host", "qualificationUnavailable")]
    [InlineData("ambiguous", "qualificationUnavailable")]
    public async Task Guided_UnqualifiedNomination_NeverUsesInstalledReceiptAsPreAcquisitionApproval(string change, string expected)
    {
        await SeedAsync(installTemplates: false);
        // A genuine receipt without installed bytes still cannot approve a remote nomination.
        await _ownership.RecordAsync((await _catalog.LoadAsync(CancellationToken.None)).Artifacts.Single(), CancellationToken.None);
        JsonNode document = JsonNode.Parse(_catalog.Json)!;
        switch (change)
        {
            case "empty":
                _catalog.Json = """{"schemaVersion":1,"revision":"unqualified","stacks":[],"artifacts":[]}""";
                break;
            case "role":
                document["artifacts"]![0]!["templates"]![0]!["contentRole"] = "function";
                _catalog.Json = document.ToJsonString();
                break;
            case "source":
                document["stacks"]![0]!["sources"] = new JsonArray("https://other.invalid/v3/index.json");
                _catalog.Json = document.ToJsonString();
                break;
            case "digest":
                document["artifacts"]![0]!["sha256"] = new string('a', 64);
                _catalog.Json = document.ToJsonString();
                break;
            case "host":
                document["stacks"]![0]!["engineHostVersions"] = "[99.0.0,100.0.0)";
                _catalog.Json = document.ToJsonString();
                break;
            default:
                document["artifacts"]!.AsArray().Add(document["artifacts"]![0]!.DeepClone());
                _catalog.Json = document.ToJsonString();
                break;
        }
        var journey = Guided(new(false, [], []));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be(expected);
        result.Succeeded.Should().BeFalse();
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("inventory")]
    [InlineData("range")]
    [InlineData("owner")]
    [InlineData("live-owner")]
    [InlineData("target")]
    public async Task Guided_ChangedPlanAfterConsent_RejectsWithoutAcquisition(string change)
    {
        await SeedAsync(installTemplates: false);
        GuidedInteraction interaction = new(true, [], [true]);
        interaction.Confirming = async () =>
        {
            if (change is "revision" or "inventory" or "range")
            {
                JsonNode document = JsonNode.Parse(_catalog.Json)!;
                if (change == "revision")
                {
                    document["revision"] = "changed-after-consent";
                }
                else if (change == "range")
                {
                    document["stacks"]![0]!["companions"]![0]!["versions"] = "[1.0.0,3.0.0)";
                }
                else
                {
                    document["artifacts"]![0]!["templates"]![0]!["path"] = "different/.template.config/template.json";
                }
                _catalog.Json = document.ToJsonString();
            }
            else if (change == "live-owner")
            {
                WorkloadEntry owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
                CopyDirectory(_paths.GetInstallDirectory(owner.PackageId, owner.PackageVersion), _paths.GetInstallDirectory(owner.PackageId, "99.0.0"));
                var node = JsonSerializer.SerializeToNode(owner, WorkloadJsonContext.Default.WorkloadEntry)!;
                node["packageVersion"] = "99.0.0";
                await _workloads.SaveWorkloadAsync(JsonSerializer.Deserialize(node, WorkloadJsonContext.Default.WorkloadEntry)!, CancellationToken.None);
                AddStackAuthorityVersion("99.0.0");
            }
            else if (change == "owner")
            {
                await _workloads.SaveWorkloadAsync(new() { PackageId = "Unrelated.Registry.Change", PackageVersion = "1.0.0",
                    Source = Source, Kind = WorkloadKind.Content }, CancellationToken.None);
            }
            else
            {
                Directory.CreateDirectory(Target);
                File.WriteAllText(Path.Combine(Target, "keep.txt"), "user work");
            }
        };
        var journey = Guided(interaction);

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().BeOneOf("acquisitionFailed", "targetChanged", "planChanged");
        journey.Payload.Opened.Should().BeEmpty();
        File.Exists(Path.Combine(Target, "value.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_AuthorityChangesDuringPrivatePreparation_RejectsBeforeCommitAndDisposesPayload()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        journey.Payload.Opening = _ =>
        {
            JsonNode document = JsonNode.Parse(_catalog.Json)!;
            document["artifacts"]![0]!["contentRevision"] = "changed-in-flight";
            _catalog.Json = document.ToJsonString();
        };

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("acquisitionFailed");
        result.Diagnostics.Should().Contain(message => message.Contains("PlanChanged", StringComparison.Ordinal));
        journey.Payload.Opened.Should().ContainSingle();
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("payload")]
    public async Task Guided_WrongOrUnavailableArchive_NeverCreatesProject(string failure)
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        journey.Payload.Missing = failure == "payload";
        if (failure == "bytes")
        {
            using var stream = File.Open(Path.Combine(_root, "exact.nupkg"), FileMode.Append);
            stream.WriteByte(0);
        }

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("acquisitionFailed");
        result.Succeeded.Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_CreationDeclinedAfterAcquisition_PreservesInstalledPackagesAndReportsNoRollback()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(true, [], [true, false]));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), CancellationToken.None);

        result.Kind.Should().Be("creationDeclined");
        result.Diagnostics.Should().Contain(message => message.Contains("remain installed", StringComparison.Ordinal));
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().NotBeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Coordinator_UnapprovedQuickstartPlan_DoesNotAcquireInScript()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));

        var result = await journey.Coordinator.ExecuteAsync(
            new(GuidedRequest() with { NonInteractive = true }, "fixture"), CancellationToken.None);

        result.Kind.Should().Be("acquisitionAuthorizationRequired");
        result.Diagnostics.Should().Contain(message => message.Contains("select it with --quickstart", StringComparison.Ordinal));
        journey.Payload.Requested.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_PublicDirectUnknownTemplate_DoesNotDiscoverOrAcquire()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        var exit = await root.Parse(["init", Target, "--template", "unknown"]).InvokeAsync();

        exit.Should().Be(1);
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
        interaction.AllOutput.Should().Contain("not installed").And.NotContain("--guided");
    }

    [Fact]
    public async Task TemplateSelection_InfersUniqueCompatibleStackAfterSelection()
    {
        await SeedAsync("parameters");
        GuidedInteraction interaction = new(true, [], [true]);
        var journey = Guided(interaction, scopes: new AdditionalStackScopeFactory(_scopes, "node-alt", "JavaScript"));
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--template", Identity, "--template-argument=--required",
            "--template-argument=supplied"]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.SelectionTitles.Should().BeEmpty();
        interaction.Confirmations.Should().Equal(false);
        File.ReadAllText(Path.Combine(Target, "parameters.txt")).Should().StartWith("supplied|");
        journey.Payload.Requested.Should().BeEmpty();
    }

    [Fact]
    public async Task QuickstartSelection_NonInteractiveAmbiguityRequiresStackBeforeAcquisition()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction, scopes: new AdditionalStackScopeFactory(_scopes, "node-alt", "JavaScript"));
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--quickstart", "fixture", "--non-interactive"]).InvokeAsync();

        exit.Should().Be(1, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("multiple qualified installed stack owners").And.Contain("--stack");
        journey.Payload.Requested.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task QuickstartSelection_InteractiveAmbiguityPromptsForQualifiedStack()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(true, ["first"], [true, true]);
        var journey = Guided(interaction, scopes: new AdditionalStackScopeFactory(_scopes, "node-alt", "JavaScript"));
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--quickstart", "fixture"]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.SelectionTitles.Should().ContainSingle().Which.Should().Be("Select the qualified project stack:");
        interaction.SelectionChoices.Single().Should().Contain(option => option.Contains("node-alt", StringComparison.Ordinal));
        interaction.Confirmations.Should().Equal(false, false);
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
    }

    [Fact]
    public async Task QuickstartSelection_ExplicitConflictingStackNeverFallsBackToAnotherCandidate()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction, scopes: new AdditionalStackScopeFactory(_scopes, "node-alt", "JavaScript"));
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--quickstart", "fixture", "--stack", "node-alt", "--non-interactive"]).InvokeAsync();

        exit.Should().Be(1, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("declarations conflict with the explicit stack/language filters");
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().NotBeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task InstalledTemplate_ExplicitConflictingStackDoesNotFallBackOrAcquire()
    {
        await SeedAsync("parameters");
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction, scopes: new AdditionalStackScopeFactory(_scopes, "node-alt", "JavaScript"));
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--template", Identity, "--stack", "node-alt", "--non-interactive",
            "--template-argument=--required", "--template-argument=supplied"]).InvokeAsync();

        exit.Should().Be(1, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("no uniquely owner-bound compatible project candidate");
        journey.Payload.Requested.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("--dry-run")]
    [InlineData("--non-interactive")]
    public async Task TemplateBackedModifierWithoutSelector_RejectsBeforeStackPrompt(string modifier)
    {
        await SeedAsync("parameters");
        var interaction = new GuidedInteraction(true, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, modifier]).InvokeAsync();

        exit.Should().Be(1, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("--template").And.Contain("--quickstart");
        interaction.SelectionTitles.Should().BeEmpty();
        journey.Payload.Requested.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_ScriptExplicitAuthorization_AcquiresAndCreatesWithoutPrompts()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction);
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--stack", "node", "--quickstart", "fixture",
            "--acquire-quickstart", "--non-interactive"]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.Confirmations.Should().BeEmpty();
        journey.Payload.Requested.Should().ContainSingle();
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
    }

    [Fact]
    public async Task QuickstartOption_AuthorizesQualifiedOwnedUpdateBeforeCreation()
    {
        (NativeTemplateOwnerProof oldReceipt, string oldArchive) = await NominateOwnedUpdateAsync();
        string digest = _selection.Artifact.Sha256;
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction);

        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];
        int exit = await root.Parse(["init", Target, "--quickstart", "fixture", "--non-interactive"]).InvokeAsync();

        exit.Should().Be(0, interaction.AllOutput);
        interaction.AllOutput.Should().Contain("1.0.0").And.Contain("1.0.1");
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.1");
        journey.Lifecycle.Prepared!.Update.Should().NotBeNull();
        journey.Lifecycle.Prepared.Update!.Before.Receipt.Should().Be(oldReceipt.Receipt);
        journey.Lifecycle.Prepared.Update.Before.ReceiptSha256.Should().Be(oldReceipt.RawSha256);
        journey.Lifecycle.ObservedFreshBinding.Should().BeTrue();
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("updated exact project");
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeTrue();
        File.Exists(oldArchive).Should().BeFalse();
        string installed = await OwnedProjectArchiveAsync();
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(installed))).Should().Be(digest);
        NativeTemplateOwnerProof receipt = (await _ownership.GetRetainedProofAsync(PackageId, CancellationToken.None))!;
        receipt.Receipt.Should().Be(new NativeTemplateOwnerReceipt(PackageId, "1.0.1", digest, Source, "owned-update-after", "1.0.1"));
        receipt.RawSha256.Should().Be(_ownership.CreateProof(journey.Lifecycle.Prepared.Artifact).RawSha256);
        using TemplateEngineSession reader = await _templates.OpenReadAsync(Context, CancellationToken.None);
        (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
            .Where(package => package.Identifier == PackageId).Should().ContainSingle().Which.Version.Should().Be("1.0.1");
        (await reader.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Where(template => template.MountPointUri == installed).Select(template => template.Identity)
            .Should().BeEquivalentTo([Identity, "Fixture.Sibling.Project"]);
        File.Exists(_templates.Journal).Should().BeFalse();
    }

    [Fact]
    public async Task ApprovedOwnedUpdate_LaterCompanionFails_RetainsCommittedUpdateAndRetriesOnlyIncompletePackage()
    {
        await NominateOwnedUpdateAsync();
        const string otherId = "Fixture.Required.Functions";
        string otherArchive = Path.Combine(_root, "required-functions.nupkg");
        using (var zip = ZipFile.Open(otherArchive, ZipArchiveMode.Create))
        {
            Write(zip, otherId + ".nuspec", $$"""
                <?xml version="1.0"?><package><metadata><id>{{otherId}}</id><version>1.0.0</version><authors>fixture</authors>
                <description>Required closure evidence only.</description><packageTypes><packageType name="FuncTemplate"/></packageTypes></metadata></package>
                """);
            Write(zip, "function/.template.config/template.json",
                """{"identity":"Fixture.Required.Function","name":"Required function","shortName":"fixture-required","tags":{"type":"item"}}""");
            Write(zip, "function/value.txt", "required function content");
        }
        string otherDigest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(otherArchive)));
        JsonNode document = JsonNode.Parse(_catalog.Json)!;
        document["stacks"]![0]!["companions"]!.AsArray().Add(JsonNode.Parse($$"""
            {"packageId":"{{otherId}}","versions":"[1.0.0]","requiredRoles":["function"]}
            """));
        document["artifacts"]!.AsArray().Add(JsonNode.Parse($$"""
            {"packageId":"{{otherId}}","version":"1.0.0","source":"{{Source}}","sha256":"{{otherDigest}}",
             "contentRevision":"required-function-only","basicOnly":false,"templates":[
               {"path":"function/.template.config/template.json","identity":"Fixture.Required.Function","type":"item","contentRole":"function"}]}
            """));
        _catalog.Json = document.ToJsonString();
        var journey = Guided(new(false, [], []), additionalPayloads: new Dictionary<string, string>
        {
            [$"{otherId}/1.0.0"] = otherArchive,
        });
        QuickstartCatalogEntry nomination = (await journey.Catalog.SearchAsync(new(), CancellationToken.None)).Entries.Single();
        WorkloadEntry owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        QualifiedQuickstart qualified = await journey.Qualification.ResolveAsync(nomination,
            new(DirectoryTarget, _selection.Owner), owner, CancellationToken.None);
        journey.Lifecycle.CommitFailure = new IOException("The required companion failed before mutation.");
        journey.Lifecycle.CommitFailurePackageId = otherId;

        BasicAcquisitionResult partial = await journey.Acquisition.RunAsync(qualified.Acquisition, CancellationToken.None);

        partial.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        partial.Failure.Should().Be(BasicAcquisitionFailure.InstallFailed);
        partial.Completed.Should().Contain(step => step.PackageId == PackageId && step.Version == "1.0.1" && !step.Reused);
        partial.Completed.Should().NotContain(step => step.PackageId == otherId);
        NativeTemplateOwnerProof committedReceipt = (await _ownership.GetRetainedProofAsync(PackageId, CancellationToken.None))!;
        committedReceipt.Receipt.Version.Should().Be("1.0.1");
        (await _ownership.GetRetainedAsync(otherId, CancellationToken.None)).Should().BeNull();
        var beforeRetry = TemplateState();
        int payloads = journey.Payload.Requested.Count;

        BasicAcquisitionResult repeatedPartial = await journey.Acquisition.RunAsync(qualified.Acquisition, CancellationToken.None);

        repeatedPartial.Status.Should().Be(BasicAcquisitionStatus.PartialFailure);
        repeatedPartial.Completed.Should().Contain(step => step.PackageId == PackageId && step.Reused);
        journey.Payload.Requested.Skip(payloads).Should().Equal($"{otherId}/1.0.0");
        TemplateState().Should().Equal(beforeRetry);
        (await _ownership.GetRetainedProofAsync(PackageId, CancellationToken.None)).Should().Be(committedReceipt);
        journey.Lifecycle.CommitFailure = null;
        payloads = journey.Payload.Requested.Count;

        BasicAcquisitionResult completed = await journey.Acquisition.RunAsync(qualified.Acquisition, CancellationToken.None);

        completed.Status.Should().Be(BasicAcquisitionStatus.Completed);
        completed.Completed.Should().Contain(step => step.PackageId == PackageId && step.Reused);
        completed.Completed.Should().Contain(step => step.PackageId == otherId && !step.Reused);
        journey.Payload.Requested.Skip(payloads).Should().Equal($"{otherId}/1.0.0");
        (await _ownership.GetRetainedProofAsync(PackageId, CancellationToken.None)).Should().Be(committedReceipt);
        (await _ownership.GetRetainedAsync(otherId, CancellationToken.None))!.Sha256.Should().Be(otherDigest);
        File.Exists(_templates.Journal).Should().BeFalse();
    }

    [Theory]
    [InlineData("owner-inventory")]
    [InlineData("authority")]
    [InlineData("required-range")]
    public async Task ApprovedOwnedUpdate_AfterCommitContextDrifts_RefusesRetryWithoutPayloadOrNativeMutation(string change)
    {
        await NominateOwnedUpdateAsync();
        var journey = Guided(new(false, [], []));
        QuickstartCatalogEntry nomination = (await journey.Catalog.SearchAsync(new(), CancellationToken.None)).Entries.Single();
        WorkloadEntry owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        QualifiedQuickstart qualified = await journey.Qualification.ResolveAsync(nomination,
            new(DirectoryTarget, _selection.Owner), owner, CancellationToken.None);
        (await journey.Acquisition.RunAsync(qualified.Acquisition, CancellationToken.None)).Status.Should().Be(BasicAcquisitionStatus.Completed);
        var beforeRetry = TemplateState();
        int payloads = journey.Payload.Requested.Count;
        if (change == "owner-inventory")
        {
            JsonNode entry = JsonSerializer.SerializeToNode(owner, WorkloadJsonContext.Default.WorkloadEntry)!;
            entry["aliases"] = JsonNode.Parse("""["node","changed-owner-inventory"]""");
            await _workloads.SaveWorkloadAsync(JsonSerializer.Deserialize(entry, WorkloadJsonContext.Default.WorkloadEntry)!, CancellationToken.None);
        }
        else
        {
            JsonNode document = JsonNode.Parse(_catalog.Json)!;
            if (change == "authority")
            {
                document["revision"] = "different-approved-authority";
            }
            else
            {
                document["stacks"]![0]!["companions"]![0]!["versions"] = "[1.0.1,2.0.0)";
            }
            _catalog.Json = document.ToJsonString();
        }

        BasicAcquisitionResult result = await journey.Acquisition.RunAsync(qualified.Acquisition, CancellationToken.None);

        result.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        result.Completed.Should().NotContain(step => step.PackageId == PackageId);
        journey.Payload.Requested.Should().HaveCount(payloads);
        TemplateState().Should().Equal(beforeRetry);
        File.Exists(_templates.Journal).Should().BeFalse();
    }

    [Theory]
    [InlineData(BundleChannel.Preview)]
    [InlineData(BundleChannel.Experimental)]
    public async Task Guided_NullChannelNomination_UsesExactQualifiedPrereleaseWithoutTreatingItAsStable(BundleChannel channel)
    {
        await SeedAsync(channel: channel, installTemplates: false);
        var journey = Guided(new(false, [], []));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("created");
        result.SelectedTemplate!.Package!.Version.Should().Be(_selection.Artifact.Version);
        journey.Payload.Requested.Should().Equal($"{PackageId}/{_selection.Artifact.Version}");
        File.ReadAllText(Path.Combine(Target, "value.txt")).Should().Be("exact");
    }

    [Fact]
    public async Task Guided_AuthorityRangeWithNewerQualifiedCandidate_AcquiresOnlyNominatedExactVersion()
    {
        await SeedAsync(installTemplates: false);
        string alternate = Path.Combine(_root, "alternate.nupkg");
        File.Copy(Path.Combine(_root, "exact.nupkg"), alternate);
        using (var archive = ZipFile.Open(alternate, ZipArchiveMode.Update))
        {
            archive.GetEntry(PackageId + ".nuspec")!.Delete();
            Write(archive, PackageId + ".nuspec", $$"""
                <?xml version="1.0"?><package><metadata><id>{{PackageId}}</id><version>2.0.0</version><authors>fixture</authors>
                <description>Exact selection control.</description><packageTypes><packageType name="FuncTemplate"/></packageTypes></metadata></package>
                """);
        }
        JsonNode document = JsonNode.Parse(_catalog.Json)!;
        JsonNode artifact = document["artifacts"]![0]!.DeepClone();
        artifact["version"] = "2.0.0";
        artifact["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(alternate)));
        document["artifacts"]!.AsArray().Add(artifact);
        document["stacks"]![0]!["companions"]![0]!["versions"] = "[1.0.0,3.0.0)";
        _catalog.Json = document.ToJsonString();
        var journey = Guided(new(false, [], []));

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("created");
        result.SelectedTemplate!.Package!.Version.Should().Be("1.0.0");
        journey.Payload.Requested.Should().Equal($"{PackageId}/1.0.0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedNativeCheck_IsReadOnlyAndRevalidatesConsentAtWriterAdmission(bool changeAfterInitialSelection)
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        QuickstartCatalogEntry nomination = (await journey.Catalog.SearchAsync(new(), CancellationToken.None)).Entries.Single();
        WorkloadEntry owner = (await _workloads.GetWorkloadsAsync(CancellationToken.None)).Single();
        var qualified = await journey.Qualification.ResolveAsync(nomination, new(DirectoryTarget, _selection.Owner), owner, CancellationToken.None);
        if (changeAfterInitialSelection)
        {
            journey.Lifecycle.Selecting = () =>
            {
                JsonNode document = JsonNode.Parse(_catalog.Json)!;
                document["revision"] = "changed-before-check-admission";
                _catalog.Json = document.ToJsonString();
            };
        }
        var before = TemplateState();

        var result = await journey.Acquisition.RunAsync(qualified.Acquisition with { Check = true }, CancellationToken.None);

        if (changeAfterInitialSelection)
        {
            result.Failure.Should().Be(BasicAcquisitionFailure.PlanChanged);
        }
        else
        {
            result.Status.Should().Be(BasicAcquisitionStatus.Incomplete);
        }
        journey.Payload.Requested.Should().BeEmpty();
        journey.Lifecycle.Prepared.Should().BeNull();
        Directory.Exists(Target).Should().BeFalse();
        TemplateState().Should().Equal(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Guided_CancellationDuringConsentOrPrivateAcquisition_NeverStartsProjectCreation(bool duringAcquisition)
    {
        await SeedAsync(installTemplates: false);
        using CancellationTokenSource cancellation = new();
        var interaction = new GuidedInteraction(true, [], [true]);
        var journey = Guided(interaction);
        if (duringAcquisition)
        {
            journey.Payload.Opening = _ => cancellation.Cancel();
            Func<Task> execute = () => journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), cancellation.Token);
            var canceled = await execute.Should().ThrowAsync<OperationCanceledException>();
            canceled.Which.CancellationToken.Should().Be(interaction.ConfirmationToken);
            canceled.Which.InnerException.Should().BeOfType<OperationCanceledException>();
        }
        else
        {
            interaction.Confirming = () =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            };
            Func<Task> execute = () => journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture"), cancellation.Token);
            await execute.Should().ThrowAsync<OperationCanceledException>();
        }
        journey.Payload.Opened.Should().BeEmpty();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("private-acquisition")]
    [InlineData("prepared-acquisition")]
    public async Task Guided_PublicParser_CancellationPropagatesAfterAcquisitionCleanupWithoutCreating(string phase)
    {
        await SeedAsync(installTemplates: false);
        using CancellationTokenSource cancellation = new();
        var interaction = new GuidedInteraction(true, [], [true]);
        var journey = Guided(interaction);
        OperationCanceledException original = new("Fixture private acquisition canceled.", cancellation.Token);
        if (phase == "consent")
        {
            interaction.Confirming = () =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            };
        }
        else if (phase == "private-acquisition")
        {
            journey.Payload.Opening = _ =>
            {
                cancellation.Cancel();
                throw original;
            };
        }
        else
        {
            journey.Lifecycle.PreparedAction = cancellation.Cancel;
        }
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];
        Func<Task> invoke = () => root.Parse(["init", Target, "--quickstart", "fixture"])
            .InvokeAsync(new() { EnableDefaultExceptionHandler = false }, cancellation.Token);

        var canceled = await invoke.Should().ThrowAsync<OperationCanceledException>();

        canceled.Which.CancellationToken.Should().Be(interaction.ConfirmationToken);
        if (phase != "consent")
        {
            canceled.Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
            interaction.AllOutput.Should().Contain("Acquisition canceled").And.Contain("remain").And.Contain(_selection.Owner.PackageId);
        }
        if (phase == "private-acquisition")
        {
            canceled.Which.InnerException.Should().BeSameAs(original);
        }
        if (phase == "prepared-acquisition")
        {
            journey.Lifecycle.Prepared.Should().NotBeNull();
            File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        }
        using IWorkloadOperation operation = await new FileWorkloadOperationCoordinator(_paths).AcquireAsync(CancellationToken.None);
        operation.Validate(_paths);
        Directory.Exists(Target).Should().BeFalse();
        journey.Lifecycle.ObservedFreshBinding.Should().BeFalse();
        (await _ownership.GetRetainedAsync(PackageId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Guided_TemplateCommitFailure_ReportsPartialAcquisitionAndRetainedStackWithoutCreating()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        journey.Lifecycle.CommitFailure = new IOException("Injected template store write failure.");

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), CancellationToken.None);

        result.Kind.Should().Be("acquisitionFailed");
        result.Diagnostics.Should().Contain(message => message.Contains("PartialFailure", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(message => message.Contains(_selection.Owner.PackageId, StringComparison.Ordinal)
            && message.Contains("remain", StringComparison.Ordinal));
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_PublicParser_AcquisitionIoFailureRemainsOrdinaryFailureAfterCleanup()
    {
        await SeedAsync(installTemplates: false);
        var interaction = new GuidedInteraction(false, [], []);
        var journey = Guided(interaction);
        journey.Lifecycle.CommitFailure = new IOException("Injected template store write failure.");
        RootCommand root = [GuidedCommand(journey.Coordinator, interaction)];

        int exit = await root.Parse(["init", Target, "--stack", "node", "--quickstart", "fixture",
            "--acquire-quickstart", "--non-interactive"]).InvokeAsync(new() { EnableDefaultExceptionHandler = false }, CancellationToken.None);

        exit.Should().Be(1);
        interaction.AllOutput.Should().Contain("PartialFailure").And.Contain("remain").And.Contain(_selection.Owner.PackageId);
        File.Exists(journey.Lifecycle.Prepared!.Archive).Should().BeFalse();
        journey.Lifecycle.ObservedFreshBinding.Should().BeFalse();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_CancellationAfterRealOutputMove_RetainsCommittedFilesWithoutClaimingConfigurationOrRollback()
    {
        await SeedAsync(installTemplates: false);
        using CancellationTokenSource cancellation = new();
        List<string> moved = [];
        var installation = new RecordingCommitFileSystem(_files)
        {
            AfterMove = path =>
            {
                moved.Add(path);
                cancellation.Cancel();
            },
        };
        var journey = Guided(new(false, [], []), installation);

        var result = await journey.Coordinator.ExecuteAsync(new(GuidedRequest(), "fixture", true), cancellation.Token);

        result.Kind.Should().Be("invocationFailed");
        result.Succeeded.Should().BeFalse();
        result.Invocation!.HasCommittedOutput.Should().BeTrue();
        result.Invocation.ConfiguredProjects.Should().BeEmpty();
        moved.Should().ContainSingle();
        File.Exists(moved[0]).Should().BeTrue();
        File.Exists(Path.Combine(Target, ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Guided_PreCanceledRequest_DoesNotInspectOrAcquire()
    {
        await SeedAsync(installTemplates: false);
        var journey = Guided(new(false, [], []));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> execute = () => journey.Coordinator.ExecuteAsync(new(GuidedRequest()), cancellation.Token);

        await execute.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    private async Task<(NativeTemplateOwnerProof Receipt, string OldArchive)> NominateOwnedUpdateAsync()
    {
        await SeedAsync(installTemplates: false);
        var initial = Guided(new(false, [], []));
        BasicTemplateArtifact old = (await _catalog.LoadAsync(CancellationToken.None)).Artifacts.Single();
        await initial.Lifecycle.EnsureAsync(old, Context, false, CancellationToken.None);
        NativeTemplateOwnerProof receipt = (await _ownership.GetRetainedProofAsync(PackageId, CancellationToken.None))!;
        string oldArchive = await OwnedProjectArchiveAsync();
        string archive = Path.Combine(_root, "exact.nupkg");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            zip.GetEntry(PackageId + ".nuspec")!.Delete();
            Write(zip, PackageId + ".nuspec", $$"""
                <?xml version="1.0"?><package><metadata><id>{{PackageId}}</id><version>1.0.1</version><authors>fixture</authors>
                <description>Owned update evidence only.</description><packageTypes><packageType name="FuncTemplate"/></packageTypes></metadata></package>
                """);
            zip.GetEntry("exact/value.txt")!.Delete();
            Write(zip, "exact/value.txt", "updated exact project");
        }
        string digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive)));
        JsonNode document = JsonNode.Parse(_catalog.Json)!;
        document["revision"] = "owned-update-fixture-only";
        document["stacks"]![0]!["companions"]![0]!["versions"] = "[1.0.1]";
        document["artifacts"]![0]!["version"] = "1.0.1";
        document["artifacts"]![0]!["sha256"] = digest;
        document["artifacts"]![0]!["contentRevision"] = "owned-update-after";
        _catalog.Json = document.ToJsonString();
        _selection = _selection with { Artifact = new(PackageId, "1.0.1", digest) };
        return (receipt, oldArchive);
    }

    private async Task<string> OwnedProjectArchiveAsync()
    {
        using TemplateEngineSession reader = await _templates.OpenReadAsync(Context, CancellationToken.None);
        return (await reader.PackageManager.GetManagedTemplatePackagesAsync(false, CancellationToken.None))
            .Single(package => package.Identifier == PackageId).MountPointUri;
    }

    private InitExecutionRequest GuidedRequest() => new(DirectoryTarget, null, null, "node", null, false, false, false, false,
        [], new HashSet<string>(StringComparer.Ordinal));

    private (QuickstartInitCoordinator Coordinator, FixturePayloadSource Payload, TrackingLifecycle Lifecycle,
        IQuickstartCatalogService Catalog, QuickstartQualification Qualification, BasicAcquisitionRunner Acquisition)
        Guided(GuidedInteraction interaction, IFileSystem? installation = null, IBoundInitScopeFactory? scopes = null,
            IReadOnlyDictionary<string, string>? additionalPayloads = null)
        => CreateGuided(interaction, installation, scopes, additionalPayloads);

    private (QuickstartInitCoordinator Coordinator, FixturePayloadSource Payload, TrackingLifecycle Lifecycle,
        IQuickstartCatalogService Catalog, QuickstartQualification Qualification, BasicAcquisitionRunner Acquisition)
        CreateGuided(IInteractionService interaction, IFileSystem? installation = null, IBoundInitScopeFactory? scopes = null,
            IReadOnlyDictionary<string, string>? additionalPayloads = null)
    {
        IBoundInitScopeFactory scopeFactory = scopes ?? _scopes;
        var source = Substitute.For<IQuickstartCatalogSource>();
        using var package = JsonDocument.Parse($$"""
            {"schemaVersion":1,"packageId":"{{PackageId}}","version":"{{_selection.Artifact.Version}}","source":"{{Source}}",
             "templateIdentity":"{{Identity}}","artifact":{"algorithm":"sha256","digest":"{{_selection.Artifact.Sha256}}","evidenceReference":"inert"},
             "projects":[{"path":".","stack":"node","language":"JavaScript"}],"requiredWorkloads":["node"]}
            """);
        var remote = Remote();
        source.GetCatalogDocumentAsync(Arg.Any<CancellationToken>()).Returns(new QuickstartCatalogDocument(
            new([remote.Entry]), [new(remote.Entry, package.RootElement.Clone())], new(QuickstartCatalogOrigin.LocalFile, "fixture-only", null)));
        var catalog = new QuickstartCatalogService(source);
        var renderer = new QuickstartCatalogRenderer(interaction);
        Dictionary<string, string> archives = new(StringComparer.OrdinalIgnoreCase)
        {
            [$"{PackageId}/{_selection.Artifact.Version}"] = Path.Combine(_root, "exact.nupkg"),
        };
        if (additionalPayloads is not null)
        {
            foreach (var item in additionalPayloads)
            {
                archives.Add(item.Key, item.Value);
            }
        }
        var payload = new FixturePayloadSource(archives);
        var lifecycle = new TrackingLifecycle(new BasicTemplateLifecycle(_templates, payload, new BasicArtifactInspector(_files), _ownership, _files));
        var version = Substitute.For<ICliVersionProvider>();
        version.Version.Returns(AssemblyCliVersionProvider.Instance.Version);
        var qualification = new QuickstartQualification(_catalog, new(), lifecycle, _workloads, version);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        var deployment = new WorkloadDeploymentService(_paths, _workloads, new WorkloadMetadataReader(), coordinator, _files,
            new BundledTemplateInstaller(_templates, new BasicArtifactInspector(_files), _files, new(Path.Combine(_root, "cli"))));
        var acquisition = new BasicAcquisitionRunner(_catalog, new(),
            new PackageSourceProvider(Options.Create(new WorkloadCatalogOptions())), _workloads, _paths,
            Substitute.For<IWorkloadInstaller>(), lifecycle, _files, coordinator, deployment);
        var arguments = new TemplateArgumentParser();
        var factory = new TemplaterFactory(_templates, installation ?? _files);
        var init = new InitExecutionRunner(factory, arguments, new(arguments, interaction), interaction, new(interaction), new(_files),
            () => throw new InvalidOperationException("Guided flow must refresh capabilities."),
            () => throw new InvalidOperationException("Guided flow must refresh project support."),
            _files, Substitute.For<ILocalSettingsProvider>(), Substitute.For<IFunctionsProjectResolver>(), new AcquisitionBoundaryScopes(scopeFactory, lifecycle));
        var browser = new FreshBrowser(catalog, scopeFactory, Factory, interaction, qualification);
        return (new(scopeFactory, Factory, browser, catalog, qualification, acquisition, init, new(_files), interaction, renderer),
            payload, lifecycle, catalog, qualification, acquisition);
    }

    private InitCommand GuidedCommand(IQuickstartInitCoordinator coordinator, IInteractionService interaction,
        IEnumerable<IProjectInitializer>? initializers = null)
        => new(interaction, Substitute.For<IWorkloadHintRenderer>(), Substitute.For<ILocalSettingsProvider>(),
            Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(),
            new(Substitute.For<IInstalledBundleWorkloads>()), _files, initializers ?? [], coordinator);

    private static IProjectInitializer NodeInitializer()
    {
        IProjectInitializer initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("node");
        initializer.SupportedLanguages.Returns(["JavaScript"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        initializer.WorkerRuntimeAliases.Returns(["node"]);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns([]);
        initializer.InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return initializer;
    }

    private static IProjectInitializer DotNetInitializer()
    {
        IProjectInitializer initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("dotnet");
        initializer.SupportedLanguages.Returns(["C#"]);
        initializer.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
        initializer.WorkerRuntimeAliases.Returns(["dotnet"]);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns([]);
        initializer.InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return initializer;
    }

    private Dictionary<string, string> TemplateState()
        => Directory.Exists(_templates.Hive)
            ? HiveBytes().ToDictionary(pair => pair.Key, pair => Convert.ToHexString(pair.Value))
            : [];

    private async Task<WorkloadEntry> SeedGuidedOwnerAsync(string archive)
    {
        var source = Substitute.For<IWorkloadPackageSource>();
        using var reader = new PackageArchiveReader(archive);
        string id = reader.NuspecReader.GetId();
        NuGetVersion version = reader.NuspecReader.GetVersion();
        source.ResolveAsync(id, version, Source, true, true, Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(id, version, new(Source)));
        source.DownloadAsync(Arg.Any<ResolvedPackage>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                string temporary = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".nupkg");
                File.Copy(archive, temporary);
                return new TemporaryWorkloadPackageFile(temporary);
            });
        var runtime = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
        runtime.Current.Returns("win-x64");
        var selector = new WorkloadRidPackageSelector(runtime);
        var metadata = new WorkloadMetadataReader();
        var operation = new FileWorkloadOperationCoordinator(_paths);
        var deployment = new WorkloadDeploymentService(_paths, _workloads, metadata, operation, _files,
            new BundledTemplateInstaller(_templates, new BasicArtifactInspector(_files), _files, new(Path.Combine(_root, "cli"))));
        var installer = new WorkloadInstaller(source, new WorkloadPackageInspector(metadata, selector), selector, deployment, operation, _workloads, _files);
        return (await installer.InstallFromCatalogAsync(id, version, Source, true, true, false, cancellationToken: CancellationToken.None)).Entry;
    }

    private sealed class GuidedInteraction(bool interactive, string[] selections, bool[] confirmations, string[]? inputs = null) : TestInteractionService, IInteractionService
    {
        private readonly Queue<string> _selections = new(selections);
        private readonly Queue<bool> _confirmations = new(confirmations);
        private readonly Queue<string> _inputs = new(inputs ?? []);
        public override bool IsInteractive => interactive;
        public List<bool> Confirmations { get; } = [];
        public List<string> SelectionTitles { get; } = [];
        public List<string[]> SelectionChoices { get; } = [];
        public CancellationToken ConfirmationToken { get; private set; }
        public Func<Task>? Confirming { get; set; }
        public Action? SelectionMade { get; set; }

        Task<string> IInteractionService.PromptForInputAsync(string prompt, string? defaultValue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteLine($"INPUT: {prompt}");
            return Task.FromResult(_inputs.Dequeue());
        }

        Task<string> IInteractionService.PromptForSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] options = [.. choices];
            string selected = _selections.Dequeue();
            SelectionTitles.Add(title);
            SelectionChoices.Add(options);
            WriteLine($"SELECT: {title} [{string.Join(", ", options)}]");
            SelectionMade?.Invoke();
            if (selected == "bundled-js")
            {
                options.Should().Contain(option => option.Contains("Azure.Functions.Node.Basic.JavaScript", StringComparison.Ordinal),
                    "the actual bundled Basic project must be selectable in the public default menu");
            }
            return Task.FromResult(selected switch
            {
                "first" => options[0],
                "second" => options[1],
                "exact" => options.Single(option => option.Contains(Identity, StringComparison.Ordinal)),
                "bundled-js" => options.Single(option => option.Contains("Azure.Functions.Node.Basic.JavaScript", StringComparison.Ordinal)),
                _ => options.Single(option => option == selected),
            });
        }

        public override async Task<bool> ConfirmAsync(string prompt, bool defaultValue, bool whenInputUnavailable, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConfirmationToken = cancellationToken;
            Confirmations.Add(defaultValue);
            WriteLine($"CONFIRM: {prompt}");
            if (Confirming is not null)
            {
                await Confirming();
            }
            return _confirmations.Dequeue();
        }
    }

    private sealed class AdditionalStackScopeFactory(IBoundInitScopeFactory inner, string stackName = "dotnet",
        string language = "C#", bool includeDotNetOwner = true) : IBoundInitScopeFactory
    {
        public async Task<BoundInitScope> CreateAsync(CancellationToken cancellationToken)
        {
            BoundInitScope scope = await inner.CreateAsync(cancellationToken);
            IProjectStack dotnet = Substitute.For<IProjectStack>();
            dotnet.Stack.Returns(stackName);
            dotnet.DisplayName.Returns(stackName == "dotnet" ? ".NET" : stackName);
            dotnet.WorkerRuntimeAliases.Returns([stackName]);
            dotnet.SupportedLanguages.Returns([language]);
            dotnet.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>());
            var owners = scope.StackOwners.ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            if (includeDotNetOwner)
            {
                owners.Add(stackName, scope.StackOwners["node"]);
            }
            return new(scope.Availability, new([.. scope.Catalog.Stacks, dotnet]), scope.Support, scope.Verifier, scope)
            {
                StackOwners = owners,
            };
        }
    }

    private sealed class TrackingLifecycle(IBasicTemplateLifecycle inner) : IBasicTemplateLifecycle
    {
        public PreparedBasicTemplate? Prepared { get; private set; }
        public bool ObservedFreshBinding { get; set; }
        public IOException? CommitFailure { get; set; }
        public string? CommitFailurePackageId { get; set; }
        public Action? Selecting { get; set; }
        public Action? PreparedAction { get; set; }
        public Task<BasicTemplateSelection> SelectAsync(BasicCompanionPlan plan, TemplateEngineContext context, CancellationToken cancellationToken)
        {
            Selecting?.Invoke();
            return inner.SelectAsync(plan, context, cancellationToken);
        }
        public Task<BasicTemplateSelection> SelectOwnedUpdateAsync(BasicCompanionPlan plan, BasicTemplateArtifact after,
            string selectedProjectIdentity, NativeTemplateOwnerIdentity owner, string workloadInventory,
            TemplateEngineContext context, CancellationToken cancellationToken)
        {
            Selecting?.Invoke();
            return inner.SelectOwnedUpdateAsync(plan, after, selectedProjectIdentity, owner, workloadInventory, context, cancellationToken);
        }
        public Task<BasicTemplateSelection> RevalidateOwnedUpdateAsync(BasicCompanionPlan plan, BasicOwnedTemplateUpdate update,
            TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.RevalidateOwnedUpdateAsync(plan, update, context, cancellationToken);
        public Task<bool> EnsureAsync(BasicTemplateArtifact artifact, TemplateEngineContext context, bool offline, CancellationToken cancellationToken)
            => inner.EnsureAsync(artifact, context, offline, cancellationToken);
        public async Task<PreparedBasicTemplate> PrepareAsync(BasicTemplateSelection selection, TemplateEngineContext context, bool offline, CancellationToken cancellationToken)
        {
            Prepared = await inner.PrepareAsync(selection, context, offline, cancellationToken);
            PreparedAction?.Invoke();
            return Prepared;
        }
        public Task<bool> CommitAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
            => CommitFailure is null || (CommitFailurePackageId is not null && CommitFailurePackageId != prepared.Artifact.PackageId)
                ? inner.CommitAsync(prepared, context, cancellationToken) : throw CommitFailure;
        public Task RevalidatePreparedAsync(PreparedBasicTemplate prepared, TemplateEngineContext context, CancellationToken cancellationToken)
            => inner.RevalidatePreparedAsync(prepared, context, cancellationToken);
    }

    private sealed class AcquisitionBoundaryScopes(IBoundInitScopeFactory inner, TrackingLifecycle lifecycle) : IBoundInitScopeFactory
    {
        public Task<BoundInitScope> CreateAsync(CancellationToken cancellationToken)
        {
            if (lifecycle.Prepared is { } prepared)
            {
                File.Exists(prepared.Archive).Should().BeFalse("private acquisition payloads must already be disposed at fresh bound scope entry");
                lifecycle.ObservedFreshBinding = true;
            }
            return inner.CreateAsync(cancellationToken);
        }
    }

    private sealed class FreshBrowser(IQuickstartCatalogService catalog, IBoundInitScopeFactory scopes,
        ITemplaterFactory templates, IInteractionService interaction, QuickstartQualification qualification) : IQuickstartBrowser
    {
        public async Task<QuickstartBrowseResult> BrowseAsync(QuickstartBrowseRequest request, CancellationToken cancellationToken)
        {
            using BoundInitScope scope = await scopes.CreateAsync(cancellationToken);
            QuickstartBrowser browser = new(catalog, scope.Catalog, [], interaction, new(interaction),
                new(templates, scopes, qualification));
            return await browser.BrowseAsync(request, cancellationToken);
        }
    }
}
