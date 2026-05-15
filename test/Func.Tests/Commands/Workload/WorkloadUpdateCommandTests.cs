// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadUpdateCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();

    [Fact]
    public void Update_HasExpectedArgsAndOptions()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        Assert.Single(cmd.Arguments, a => a.Name == "id");
        Assert.Contains(cmd.Options, o => o.Name == "--version");
        Assert.Contains(cmd.Options, o => o.Name == "--all");
        Assert.Contains(cmd.Options, o => o.Name == "--major");
        Assert.Contains(cmd.Options, o => o.Name == "--source");
        Assert.Contains(cmd.Options, o => o.Name == "--prerelease");
        Assert.Contains(cmd.Options, o => o.Name == "--exact");
    }

    [Fact]
    public void Update_MissingPackageAndAll_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name]);

        Assert.NotEmpty(parse.Errors);
    }

    [Fact]
    public void Update_BothIdAndAll_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "test.workload", "--all"]);

        Assert.NotEmpty(parse.Errors);
    }

    [Fact]
    public void Update_AllAndVersion_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "--all", "--version", "1.0.0"]);

        Assert.NotEmpty(parse.Errors);
        Assert.Contains(parse.Errors, e => e.Message.Contains("--version cannot be combined with --all"));
    }

    [Fact]
    public void Update_AllAndExact_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "--all", "--exact"]);

        Assert.NotEmpty(parse.Errors);
        Assert.Contains(parse.Errors, e => e.Message.Contains("--exact cannot be combined with --all"));
    }

    [Fact]
    public void Update_WhitespaceArg_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "   "]);

        Assert.NotEmpty(parse.Errors);
    }

    [Fact]
    public void Update_InvalidVersion_FailsValidation()
    {
        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "Test.Workload", "--version", "not-semver"]);

        Assert.NotEmpty(parse.Errors);
        Assert.Contains(parse.Errors, e => e.Message.Contains("not a valid semver"));
    }

    [Fact]
    public async Task Update_HappyPath_WritesSuccessWithOldAndNewVersion()
    {
        _installer.UpdateAsync(
                "Test.Workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.1.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "Test.Workload");

        Assert.Equal(0, exit);
        Assert.Contains(
            _interaction.Lines,
            l => l.StartsWith("SUCCESS:")
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
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.0.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: true));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "Test.Workload");

        Assert.Equal(0, exit);
        Assert.Contains(
            _interaction.Lines,
            l => l.Contains("already at the latest")
                && l.Contains("test.workload")
                && l.Contains("1.0.0"));
    }

    [Fact]
    public async Task Update_ForwardsAllowMajorAndPrereleaseAndSource()
    {
        _installer.UpdateAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "2.0.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(
            cmd,
            "Test.Workload",
            "--version", "1.0.0",
            "--major",
            "--source", "https://example/v3/index.json",
            "--prerelease");

        Assert.Equal(0, exit);
        await _installer.Received(1).UpdateAsync(
            "Test.Workload",
            Arg.Is<NuGetVersion>(v => v != null && v.ToNormalizedString() == "1.0.0"),
            "https://example/v3/index.json",
            true,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_NotInstalled_GracefulException()
    {
        _installer.UpdateAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Workload 'test.workload' is not installed."));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload"));

        Assert.Contains("not installed", ex.Message);
        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task Update_PackageNotFoundOnSource_GracefulException()
    {
        _installer.UpdateAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new WorkloadPackageNotFoundException("nope"));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "Test.Workload"));

        Assert.True(ex.IsUserError);
    }

    [Fact]
    public async Task Update_All_NoInstalled_WritesHint_ExitsZero()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WorkloadEntry>());

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "--all");

        Assert.Equal(0, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("HINT:") && l.Contains("No workloads installed"));
        await _installer.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "a.workload", PackageVersion = "1.2.0" },
                "1.1.0",
                NoUpdateAvailable: false));
        _installer.UpdateAsync(
                "b.workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "b.workload", PackageVersion = "2.0.0" },
                "2.0.0",
                NoUpdateAvailable: true));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "--all");

        Assert.Equal(0, exit);
        await _installer.Received(1).UpdateAsync(
            "a.workload", Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _installer.Received(1).UpdateAsync(
            "b.workload", Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        Assert.Contains(_interaction.Lines, l => l.StartsWith("SUCCESS:") && l.Contains("a.workload"));
        Assert.Contains(_interaction.Lines, l => l.StartsWith("WARNING:") && l.Contains("b.workload"));
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
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new WorkloadPackageNotFoundException("not on feed"));
        _installer.UpdateAsync(
                "b.workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "b.workload", PackageVersion = "2.1.0" },
                "2.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "--all");

        Assert.Equal(1, exit);
        Assert.Contains(_interaction.Lines, l => l.StartsWith("ERROR:") && l.Contains("a.workload"));
        Assert.Contains(_interaction.Lines, l => l.StartsWith("SUCCESS:") && l.Contains("b.workload"));
    }

    [Fact]
    public async Task Update_NoCandidateOnSource_HintsAboutSource()
    {
        _installer.UpdateAsync(
                "Test.Workload",
                Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                Entry: new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.0.0" },
                PreviousVersion: "1.0.0",
                NoUpdateAvailable: true,
                NoCandidateOnSource: true));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "Test.Workload");

        Assert.Equal(0, exit);
        Assert.Contains(
            _interaction.Lines,
            l => l.StartsWith("HINT:")
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
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadUpdateResult(
                new WorkloadEntry { PackageId = "test.workload", PackageVersion = "1.1.0" },
                "1.0.0",
                NoUpdateAvailable: false));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        int exit = await InvokeAsync(cmd, "demo");

        Assert.Equal(0, exit);
        await _installer.Received(1).UpdateAsync(
            "test.workload",
            Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
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
                Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Workload 'demo' is not installed."));

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "demo", "--exact"));

        Assert.Contains("not installed", ex.Message);
        await _installer.DidNotReceive().UpdateAsync(
            "test.workload",
            Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_AmbiguousAlias_GracefulException()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new WorkloadEntry[]
        {
            new() { PackageId = "a.workload", PackageVersion = "1.0.0", Aliases = ["shared"] },
            new() { PackageId = "b.workload", PackageVersion = "2.0.0", Aliases = ["shared"] },
        });

        var cmd = new WorkloadUpdateCommand(_interaction, _installer, _store);
        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => InvokeAsync(cmd, "shared"));

        Assert.Contains("matches multiple installed workloads", ex.Message);
        Assert.Contains("a.workload", ex.Message);
        Assert.Contains("b.workload", ex.Message);
        Assert.True(ex.IsUserError);
        await _installer.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    private static Task<int> InvokeAsync(WorkloadUpdateCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}
