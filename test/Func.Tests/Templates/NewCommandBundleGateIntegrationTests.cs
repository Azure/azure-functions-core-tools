// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;
using Azure.Functions.Cli.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NewCommandBundleGateIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "bundle-gate-tests", Guid.NewGuid().ToString("N"));
    private readonly TestInteractionService _interaction = new();

    [Theory]
    [InlineData("[9.0.0,10.0.0)", "4.35.0", "[4.0.0,)", 1)]
    [InlineData("not-a-range", "4.35.0", "[4.0.0,)", 1)]
    [InlineData("[9.0.0,10.0.0)", "4.35.0-dev", "[4.0.0,)", 1)]
    [InlineData("[4.0.0,5.0.0)", null, "[4.0.0,)", 1)]
    [InlineData("[3.0.0,5.0.0)", "3.9.0", "[4.0.0,)", 1)]
    [InlineData("[4.0.0,5.0.0)", "4.35.0", "[4.0.0,)", 0)]
    [InlineData("[4.0.0,5.0.0)", "4.0.0", "[4.0.0,)", 0)]
    [InlineData("[4.0.0,5.0.0)", "4.35.0", null, 0)]
    public async Task ExecuteAsync_RealBundleResolver_OnlyCompatibleBundleScaffolds(
        string hostRange, string? installedVersion, string? minimum, int expectedExit)
    {
        var workingDirectory = CreateFixture(hostRange, minimum);
        var resolver = CreateBundleResolver(installedVersion);
        var resolution = await resolver.ResolveAsync(new ExtensionBundleProjectContext(
            BundleHelpers.StableBundleId, hostRange, "node", null, null), CancellationToken.None);
        var runner = CreateRunner(workingDirectory, resolver);
        var before = Snapshot(workingDirectory);

        int exitCode = await runner.ExecuteAsync(Invocation(workingDirectory), CancellationToken.None);

        // Check writes first so RED demonstrates the actual unsafe scaffold, not just an exit-code mismatch.
        if (expectedExit != 0)
        {
            Snapshot(workingDirectory).Should().BeEquivalentTo(before);
            _interaction.Lines.Should().NotContain(line => line.StartsWith("SUCCESS:", StringComparison.Ordinal));
        }
        else
        {
            File.ReadAllText(Path.Combine(workingDirectory.Info.FullName, "src", "functions", "BundleProbe.js"))
                .Should().Be("// generated BundleProbe");
        }

        exitCode.Should().Be(expectedExit);
        if (resolution is ExtensionBundleResolution.NoCompatibleInstall none)
        {
            _interaction.Lines.Should().Equal($"ERROR: {none.Hint}");
        }
    }

    [Theory]
    [MemberData(nameof(NewCommandBundleValidatorTests.ResolutionOutcomes), MemberType = typeof(NewCommandBundleValidatorTests))]
    public async Task ExecuteAsync_ResolutionOutcome_GatesTemplateDiscoveryAndWrites(ExtensionBundleResolution resolution, int expectedGateExit)
    {
        var workingDirectory = CreateFixture("[4.0.0,5.0.0)", "[4.0.0,)");
        var resolver = Substitute.For<IExtensionBundleResolver>();
        resolver.ResolveAsync(Arg.Any<ExtensionBundleProjectContext>(), Arg.Any<CancellationToken>()).Returns(resolution);
        var provider = Substitute.For<ITemplateEngineProvider>();
        provider.EngineId.Returns(EngineIds.V2);
        provider.ListTemplatesAsync(Arg.Any<TemplateListContext>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var runner = CreateRunner(workingDirectory, resolver, provider);
        var before = Snapshot(workingDirectory);

        if (expectedGateExit == -1)
        {
            Func<Task> act = () => runner.ExecuteAsync(Invocation(workingDirectory), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"Unknown resolution variant: {resolution.GetType().Name}");
        }
        else
        {
            // The resolved case reaches an empty catalogue; the failing cases stop before discovery.
            int exitCode = await runner.ExecuteAsync(Invocation(workingDirectory), CancellationToken.None);
            exitCode.Should().Be(1);
        }

        Snapshot(workingDirectory).Should().BeEquivalentTo(before);
    if (expectedGateExit == 0)
        {
            await provider.Received(1).ListTemplatesAsync(Arg.Any<TemplateListContext>(), CancellationToken.None);
        }
        else
        {
            provider.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(ITemplateEngineProvider.ListTemplatesAsync));
        }

        provider.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(ITemplateEngineProvider.ApplyAsync));
    }

    [Fact]
    public async Task ListAsync_IncompatibleBundle_StillListsWithoutScaffolding()
    {
        var workingDirectory = CreateFixture("[9.0.0,10.0.0)", "[4.0.0,)");
        var runner = CreateRunner(workingDirectory, CreateBundleResolver("4.35.0"));
        var before = Snapshot(workingDirectory);

        int exitCode = await runner.ListAsync(Invocation(workingDirectory), CancellationToken.None);

        exitCode.Should().Be(0);
        Snapshot(workingDirectory).Should().BeEquivalentTo(before);
        _interaction.AllOutput.Should().Contain("HTTP trigger");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private WorkingDirectory CreateFixture(string hostRange, string? minimum)
    {
        string project = Path.Combine(_root, "project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "host.json"), JsonSerializer.Serialize(new
        {
            version = "2.0",
            extensionBundle = new { id = BundleHelpers.StableBundleId, version = hostRange },
        }));
        string content = Path.Combine(_root, "templates", "tools", "any", "content");
        Directory.CreateDirectory(Path.Combine(content, "v2", "templates"));
        File.WriteAllText(Path.Combine(content, "templates-workload.json"), JsonSerializer.Serialize(new { minBundleVersion = minimum }));
        File.WriteAllText(Path.Combine(content, "v2", "templates", "templates.json"), """
            [{
              "id": "HttpTrigger-JavaScript", "name": "HTTP trigger", "language": "javascript",
              "actions": [{ "type": "WriteToFile", "filePath": "src/functions/$(FUNCTION_NAME_INPUT).js",
                            "fileContent": "// generated $(FUNCTION_NAME_INPUT)" }]
            }]
            """);
        return new WorkingDirectory(new DirectoryInfo(project), WasExplicit: true);
    }

    private ExtensionBundleResolver CreateBundleResolver(string? installedVersion)
    {
        var installed = Substitute.For<IInstalledBundleWorkloads>();
        installed.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns(installedVersion is null
            ? [] : new InstalledBundleWorkload[] { new(installedVersion, Path.Combine(_root, "bundle")) });
        return new ExtensionBundleResolver(new InstalledBundleScanner(installed), NullBundleResolveTelemetry.Instance,
            NullLogger<ExtensionBundleResolver>.Instance);
    }

    private NewCommandRunner CreateRunner(WorkingDirectory workingDirectory, IExtensionBundleResolver bundleResolver, ITemplateEngineProvider? provider = null)
    {
        var projectResolver = Substitute.For<IFunctionsProjectResolver>();
        projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(ProjectResolutionResults.Resolved(new NodeProject(workingDirectory), "test"));
        var options = Substitute.For<IOptionsMonitor<StackOptions>>();
        options.Get(Arg.Any<string>()).Returns(new StackOptions { Runtime = "node", Language = "javascript" });
        var installed = Substitute.For<IInstalledTemplatesWorkloads>();
        installed.ListInstalledAsync("node", Arg.Any<CancellationToken>())
            .Returns(new InstalledTemplatesWorkload[] { new("node", "1.0.0", Path.Combine(_root, "templates")) });
        HostJsonBundleSectionReader hostReader = new();
        TemplateEngineProviderRegistry registry = new([provider ?? new V2EngineProvider(installed)]);
        NewCommandRenderer renderer = new(_interaction);
        return new NewCommandRunner(
            new NewCommandContextResolver(_interaction, projectResolver, Substitute.For<IProfileResolver>(), options, [], installed, hostReader),
            new NewCommandBundleValidator(_interaction, hostReader, bundleResolver, new TemplatesWorkloadManifestReader()),
            new NewCommandTemplateCatalog(registry),
            new NewCommandTemplateSelector(_interaction, new TemplatePicker(_interaction)),
            new NewCommandTemplateApplicator(registry, new TemplateOptionHydrator([])),
            renderer,
            new NewCommandResultRenderer(_interaction, renderer));
    }

    private static NewInvocation Invocation(WorkingDirectory directory)
        => new(directory, "HttpTrigger-JavaScript", "BundleProbe", Force: true, NonInteractive: true);

    private static Dictionary<string, string> Snapshot(WorkingDirectory directory)
        => Directory.GetFiles(directory.Info.FullName, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory.Info.FullName, path), File.ReadAllText);

    private sealed class NodeProject(WorkingDirectory directory) : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory => directory;
        public override string StackName => "node";
        public override string StackDisplayName => "Node.js";
        public override bool SupportsExtensionBundles => true;
        public override FunctionsWorkerReference WorkerReference { get; } =
            FunctionsWorkerReference.FromWorkerInfo("Node.js", "node", "worker.config.json", "1.0.0");
    }
}