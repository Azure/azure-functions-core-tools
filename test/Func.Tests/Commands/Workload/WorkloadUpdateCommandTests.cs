// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadUpdateCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();

    private static readonly Microsoft.Extensions.Options.IOptions<WorkloadCatalogOptions> _testCatalogOptions
        = Microsoft.Extensions.Options.Options.Create(new WorkloadCatalogOptions());

    [Fact]
    public void Update_HasExpectedArgsAndOptions()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        cmd.Arguments.Should().ContainSingle(a => a.Name == "id");
        cmd.Options.Should().Contain(o => o.Name == "--version");
        cmd.Options.Should().Contain(o => o.Name == "--all");
        cmd.Options.Should().Contain(o => o.Name == "--major");
        cmd.Options.Should().Contain(o => o.Name == "--source");
        cmd.Options.Should().Contain(o => o.Name == "--prerelease");
        cmd.Options.Should().Contain(o => o.Name == "--exact");
    }

    [Fact]
    public void Update_MissingPackageAndAll_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name]);

        parse.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Update_BothIdAndAll_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "test.workload", "--all"]);

        parse.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Update_AllAndVersion_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "--all", "--version", "1.0.0"]);

        parse.Errors.Should().NotBeEmpty();
        parse.Errors.Should().Contain(e => e.Message.Contains("--version cannot be combined with --all"));
    }

    [Fact]
    public void Update_AllAndExact_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "--all", "--exact"]);

        parse.Errors.Should().NotBeEmpty();
        parse.Errors.Should().Contain(e => e.Message.Contains("--exact cannot be combined with --all"));
    }

    [Fact]
    public void Update_WhitespaceArg_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "   "]);

        parse.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Update_InvalidVersion_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "Test.Workload", "--version", "not-semver"]);

        parse.Errors.Should().NotBeEmpty();
        parse.Errors.Should().Contain(e => e.Message.Contains("not a valid semver"));
    }

    [Fact]
    public async Task Update_HappyPath_WritesSuccessWithOldAndNewVersion()
    {
        _installer.UpdateAsync(
                "Test.Workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.1.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("SUCCESS:")
                && l.Contains("Updated workload")
                && l.Contains("test.workload")
                && l.Contains("from 1.0.0 to 1.1.0"));
    }

    [Fact]
    public async Task Update_NoUpdateAvailable_WritesHint_ExitsZero()
    {
        _installer.UpdateAsync(
                "Test.Workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.0.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: true));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.Contains("already at the latest")
                && l.Contains("test.workload")
                && l.Contains("1.0.0"));
    }

    [Fact]
    public async Task Update_ForwardsAllowMajorAndPrereleaseAndSource()
    {
        _installer.UpdateAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "2.0.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(
            cmd,
            "Test.Workload",
            "--version", "1.0.0",
            "--major",
            "--source", "https://example/v3/index.json",
            "--prerelease");

        exit.Should().Be(0);
        await _installer.Received(1).UpdateAsync(
            "Test.Workload",
            Arg.Is<NuGetVersion>(v => v != null && v.ToNormalizedString() == "1.0.0"),
            "https://example/v3/index.json",
            true,
            true,
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_NotInstalled_GracefulException()
    {
        _installer.UpdateAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Workload 'test.workload' is not installed."));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload")).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("not installed");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task Update_PackageNotFoundOnSource_GracefulException()
    {
        _installer.UpdateAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new WorkloadPackageNotFoundException("nope"));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload")).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task Update_All_NoInstalled_WritesHint_ExitsZero()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WorkloadEntry>());

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "--all");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:") && l.Contains("No workloads installed"));
        await _installer.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_All_IteratesDistinctPackageIds()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadEntry[]
        {
            new() { PackageId = "a.workload", PackageVersion = "1.0.0" },
            new() { PackageId = "A.Workload", PackageVersion = "1.1.0" },
            new() { PackageId = "b.workload", PackageVersion = "2.0.0" },
        });
        _installer.UpdateAsync(
                "a.workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "a.workload", PackageVersion = "1.2.0" },
                "1.1.0",
                NoUpdateAvailable: false));
        _installer.UpdateAsync(
                "b.workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "b.workload", PackageVersion = "2.0.0" },
                "2.0.0",
                NoUpdateAvailable: true));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "--all");

        exit.Should().Be(0);
        await _installer.Received(1).UpdateAsync(
            "a.workload", Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
        await _installer.Received(1).UpdateAsync(
            "b.workload", Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
        _interaction.Lines.Should().Contain(l => l.StartsWith("SUCCESS:") && l.Contains("a.workload"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:") && l.Contains("b.workload"));
    }

    [Fact]
    public async Task Update_All_PerIdFailureDoesNotStopOthers_ReturnsNonZero()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadEntry[]
        {
            new() { PackageId = "a.workload", PackageVersion = "1.0.0" },
            new() { PackageId = "b.workload", PackageVersion = "2.0.0" },
        });
        _installer.UpdateAsync(
                "a.workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new WorkloadPackageNotFoundException("not on feed"));
        _installer.UpdateAsync(
                "b.workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "b.workload", PackageVersion = "2.1.0" },
                "2.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "--all");

        exit.Should().Be(1);
        _interaction.Lines.Should().Contain(l => l.StartsWith("ERROR:") && l.Contains("a.workload"));
        _interaction.Lines.Should().Contain(l => l.StartsWith("SUCCESS:") && l.Contains("b.workload"));
    }

    [Fact]
    public async Task Update_NoCandidateOnSource_HintsAboutSource()
    {
        _installer.UpdateAsync(
                "Test.Workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.0.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: true,
                NoCandidateOnSource: true));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("HINT:")
                && l.Contains("No version of 'test.workload'")
                && l.Contains("--source"));
    }

    [Fact]
    public async Task Update_AliasMatchesInstalledEntry_ResolvesToPackageId()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadEntry[]
        {
            new()
            {
                PackageId = "test.workload",
                PackageVersion = "1.0.0",
                Aliases = ["demo"],
            },
        });
        _installer.UpdateAsync(
                "test.workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.1.0" },
                "1.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        int exit = await InvokeAsync(cmd, "demo");

        exit.Should().Be(0);
        await _installer.Received(1).UpdateAsync(
            "test.workload",
            Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_ExactSkipsAliasLookup_PassesArgumentThrough()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadEntry[]
        {
            new()
            {
                PackageId = "test.workload",
                PackageVersion = "1.0.0",
                Aliases = ["demo"],
            },
        });
        _installer.UpdateAsync(
                "demo",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Workload 'demo' is not installed."));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "demo", "--exact")).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("not installed");
        await _installer.DidNotReceive().UpdateAsync(
            "test.workload",
            Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AmbiguousAlias_GracefulException()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadEntry[]
        {
            new() { PackageId = "a.workload", PackageVersion = "1.0.0", Aliases = ["shared"] },
            new() { PackageId = "b.workload", PackageVersion = "2.0.0", Aliases = ["shared"] },
        });

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions);
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "shared")).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("matches multiple installed workloads");
        ex.Message.Should().Contain("a.workload");
        ex.Message.Should().Contain("b.workload");
        ex.IsUserError.Should().BeTrue();
        await _installer.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    private static Task<int> InvokeAsync(WorkloadUpdateCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
