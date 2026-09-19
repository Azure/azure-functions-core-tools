// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Tests.Console;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupFeatureResolverLabelTests
{
    [Theory]
    [InlineData("node", "node")]
    [InlineData("[red]node[/]", "[[red]]node[[/]]")]
    [InlineData("[broken", "[[broken")]
    [InlineData("node[custom]", "node[[custom]]")]
    [InlineData("node[[custom]]", "node[[[[custom]]]]")]
    public async Task ResolveFeaturesAsync_FeedAlias_EscapesOnlyLabelAndPreservesPlanIdentity(string alias, string expectedLabel)
    {
        const string stackPackageId = "contoso.stack";
        const string templatesPackageId = "contoso.templates";
        var catalog = Substitute.For<ISetupStackCatalog>();
        catalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SetupStackSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [alias] = stackPackageId },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [alias] = templatesPackageId }));
        using var stdout = new BufferedConsole(interactive: true, ansi: true);
        using var stderr = new BufferedConsole(interactive: true, ansi: true);
        stderr.Enqueue(ConsoleKey.Spacebar, ' ');
        stderr.Enqueue(ConsoleKey.Enter);
        var spectre = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(spectre.IsInteractive);
        MultiSelectionChoice[] offered = [];
        interaction.PromptForMultiSelectionAsync(
            Arg.Any<string>(), Arg.Any<IEnumerable<MultiSelectionChoice>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                offered = [.. call.ArgAt<IEnumerable<MultiSelectionChoice>>(1)];
                return spectre.PromptForMultiSelectionAsync(call.ArgAt<string>(0), offered, call.ArgAt<CancellationToken>(2));
            });
        SetupFeatureResolver resolver = CreateResolver(interaction, catalog);
        SetupDependencyPlanBuilder builder = new(Substitute.For<IHostJsonBundleSectionReader>(), catalog);
        SetupCommandOptions options = Options();
        using var cancellation = new CancellationTokenSource();

        SetupFeaturePlan? features = await resolver.ResolveFeaturesAsync(options, cancellation.Token);
        SetupDependencyPlan plan = await builder.BuildDependencyPlanAsync(
            options, features!, SetupProfileScope.Unconstrained, cancellation.Token);

        MultiSelectionChoice choice = offered.Should().ContainSingle().Which;
        choice.Label.Should().Be(expectedLabel);
        choice.Value.Should().Be(alias);
        stdout.Output.Should().BeEmpty();
        stderr.Reads.Should().Be(2);
        stderr.Output.Should().Contain(alias);
        using var literal = new BufferedConsole(interactive: false, ansi: false, noColor: true);
        literal.Write(new Markup(choice.Label));
        literal.Output.Should().Be(alias);
        features.Should().NotBeNull();
        features!.Features.Should().Equal([alias]);
        features.WorkerRuntimes.Should().Equal([alias]);
        features.RuntimeFeatures.Should().Equal([new SetupRuntimeFeature(alias, alias, InstallWorker: true)]);
        plan.Failures.Should().BeEmpty();
        SetupDependency stack = plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Stack).Which;
        stack.Name.Should().Be(alias);
        stack.PackageId.Should().Be(stackPackageId);
        SetupDependency templates = plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Templates).Which;
        templates.Name.Should().Be(alias);
        templates.PackageId.Should().Be(templatesPackageId);
        plan.Dependencies.Should().ContainSingle(d => d.Kind == SetupDependencyKind.Worker).Which.Name.Should().Be(alias);
    }

    public static IEnumerable<object[]> ReservedFeatures()
        => SetupFeatureResolver.ResolverKeywords.Order(StringComparer.Ordinal).Select(static feature => (object[])[feature]);

    [Theory]
    [MemberData(nameof(ReservedFeatures))]
    public async Task ResolveFeaturesAsync_ReservedCanonicalAlias_HintUsesExactPackageInstall(string canonical)
    {
        var catalog = Substitute.For<ISetupStackCatalog>();
        catalog.GetStacksAsync(Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new SetupStackSnapshot(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [canonical] = "contoso.stack" },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                SecondaryAliases: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["alternate"] = canonical }));
        SetupFeatureResolver resolver = CreateResolver(Substitute.For<IInteractionService>(), catalog);
        using var cancellation = new CancellationTokenSource();

        var exception = await FluentActions.Awaiting(() => resolver.ResolveFeaturesAsync(Options("alternate"), cancellation.Token))
            .Should().ThrowAsync<SetupConfigurationException>();

        exception.Which.Message.Should().Be(
            $"Stack alias 'alternate' resolves to reserved setup feature '{canonical}'. "
            + "Install the package explicitly with 'func workload install --exact <package-id>'.");
    }

    private static SetupFeatureResolver CreateResolver(IInteractionService interaction, ISetupStackCatalog catalog)
    {
        var store = Substitute.For<IWorkloadStore>();
        store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var configuration = Substitute.For<ICliConfigurationProvider>();
        configuration.GetProjectConfiguration(Arg.Any<DirectoryInfo>()).Returns(new ConfigurationBuilder().Build());
        return new SetupFeatureResolver(interaction, store, configuration, catalog);
    }

    private static SetupCommandOptions Options(params string[] features)
        => new(new DirectoryInfo(Path.GetTempPath()), features, [], null, SetupInstallPolicy.LatestCompatible,
            IncludePrerelease: false, NonInteractive: false, AssumeYes: false, Check: false, SetupOutputMode.Plain);
}