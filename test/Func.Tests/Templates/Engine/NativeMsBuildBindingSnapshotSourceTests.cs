// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class NativeMsBuildBindingSnapshotSourceTests
{
    private readonly IEngineEnvironmentSettings _settings = Substitute.For<IEngineEnvironmentSettings>();
    private static string Project => Path.GetFullPath("Selected.csproj");

    [Theory]
    [InlineData("TargetFramework", "net10.0")]
    [InlineData("RootNamespace", "Imported.Functions")]
    [InlineData("TargetFrameworks", null)]
    [InlineData("targetframework", null)]
    [InlineData("msbuild:TargetFramework", null)]
    [InlineData("", null)]
    [InlineData("Anything", null)]
    public async Task GetBoundValueAsync_KnownAndUnknownSymbols_ReturnsOnlySuppliedSnapshotValue(string name, string? expected)
    {
        NativeMsBuildBindingSnapshot snapshot = new(Project, "Selected.Template", "selected-source", new string('a', 64),
            "net10.0", "Imported.Functions");
        NativeMsBuildBindingSnapshotSource source = new(snapshot);

        string? value = await source.GetBoundValueAsync(_settings, name, CancellationToken.None);

        value.Should().Be(expected);
        _settings.ReceivedCalls().Should().BeEmpty("binding reads only supplied primitives, not project or process state");
    }

    [Fact]
    public async Task GetBoundValueAsync_SuppliedFramework_DoesNotInventNet10OrPerformEligibilitySelection()
    {
        NativeMsBuildBindingSnapshot snapshot = new(Project, "Selected.Template", "selected-source", new string('a', 64),
            "explicit-evaluated-value", "Imported.Functions");
        NativeMsBuildBindingSnapshotSource source = new(snapshot);

        string? value = await source.GetBoundValueAsync(_settings, "TargetFramework", CancellationToken.None);

        value.Should().Be("explicit-evaluated-value", "framework eligibility belongs to the existing .NET preparation helper");
        source.SourcePrefix.Should().Be("msbuild");
        source.RequiresPrefixMatch.Should().BeTrue();
    }

    [Fact]
    public async Task NativeEngine_UnknownBinding_RetainsAuthoredDefaultWithoutProjectDiscovery()
    {
        string root = Directory.CreateTempSubdirectory("func-snapshot-fallback-").FullName;
        try
        {
            string template = Path.Combine(root, "template");
            Directory.CreateDirectory(Path.Combine(template, ".template.config"));
            File.WriteAllText(Path.Combine(template, ".template.config", "template.json"),
                """
                {
                  "identity": "Snapshot.Default.Control",
                  "name": "Snapshot defaults",
                  "shortName": "snapshot-default-control",
                  "tags": { "type": "item" },
                  "symbols": {
                    "Framework": { "type": "bind", "binding": "msbuild:TargetFramework", "defaultValue": "framework-fallback", "replaces": "FRAMEWORK_VALUE" },
                    "Unknown": { "type": "bind", "binding": "msbuild:MissingProperty", "defaultValue": "authored-default", "replaces": "UNKNOWN_VALUE" }
                  }
                }
                """);
            File.WriteAllText(Path.Combine(template, "values.txt"), "FRAMEWORK_VALUE UNKNOWN_VALUE");
            string missingProject = Path.Combine(root, "not-discovered.csproj");
            NativeMsBuildBindingSnapshotSource source = new(new(missingProject, "Snapshot.Default.Control",
                template, new string('a', 64), "net10.0", "Selected.Namespace"));
            using DefaultTemplateEngineHost host = new("func", "1.0.0", null,
                [.. Components.AllComponents, .. Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents,
                    (typeof(IBindSymbolSource), source)]);
            using EngineEnvironmentSettings settings = new(host, settingsLocation: Path.Combine(root, "hive"));
            TemplatePackageManager manager = new(settings);
            IManagedTemplatePackageProvider provider = manager.GetBuiltInManagedProvider(InstallationScope.Global);
            IReadOnlyList<InstallResult> installed = await provider.InstallAsync([new InstallRequest(template)], CancellationToken.None);
            installed.Should().ContainSingle().Which.Success.Should().BeTrue();
            ITemplateInfo selected = (await manager.GetTemplatesAsync(CancellationToken.None)).Single();
            string output = Path.Combine(root, "output");

            ITemplateCreationResult created = await new TemplateCreator(settings).InstantiateAsync(selected, "Values", "Values",
                output, new Dictionary<string, string?>(), cancellationToken: CancellationToken.None);

            created.Status.Should().Be(CreationResultStatus.Success);
            File.ReadAllText(Path.Combine(output, "values.txt")).Should().Be("net10.0 authored-default");
            File.Exists(missingProject).Should().BeFalse("the snapshot adapter must never discover or evaluate a project");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GetBoundValueAsync_PreCanceled_PropagatesCancellation()
    {
        NativeMsBuildBindingSnapshotSource source = new(new(Project, "Selected.Template", "selected-source",
            new string('a', 64), "net10.0", "Imported.Functions"));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        Func<Task> action = () => source.GetBoundValueAsync(_settings, "TargetFramework", cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetBoundValueAsync_NullArguments_Throw()
    {
        NativeMsBuildBindingSnapshotSource source = new(new(Project, "Selected.Template", "selected-source",
            new string('a', 64), "net10.0", "Imported.Functions"));
        Func<Task> settings = () => source.GetBoundValueAsync(null!, "TargetFramework", CancellationToken.None);
        Func<Task> name = () => source.GetBoundValueAsync(_settings, null!, CancellationToken.None);

        await settings.Should().ThrowAsync<ArgumentNullException>().WithParameterName("settings");
        await name.Should().ThrowAsync<ArgumentNullException>().WithParameterName("bindname");
    }

    [Theory]
    [InlineData("path")]
    [InlineData("relative")]
    [InlineData("extension")]
    [InlineData("identity")]
    [InlineData("source")]
    [InlineData("digest")]
    [InlineData("framework")]
    [InlineData("namespace")]
    public void Snapshot_MissingOrMalformedInput_RefusesRatherThanGuessing(string invalid)
    {
        Action action = () => new NativeMsBuildBindingSnapshot(
            invalid == "path" ? null! : invalid == "relative" ? "Selected.csproj" : invalid == "extension" ? Path.ChangeExtension(Project, ".cmd") : Project,
            invalid == "identity" ? "" : "Selected.Template", invalid == "source" ? "" : "selected-source",
            invalid == "digest" ? "not-sha256" : new string('a', 64), invalid == "framework" ? "" : "net10.0",
            invalid == "namespace" ? "" : "Imported.Functions");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullSnapshot_Throws()
    {
        Action action = () => new NativeMsBuildBindingSnapshotSource(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("snapshot");
    }
}
