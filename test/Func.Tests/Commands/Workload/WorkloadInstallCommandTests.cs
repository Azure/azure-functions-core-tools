// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
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

namespace Azure.Functions.Cli.Tests.Commands.Workload;

public class WorkloadInstallCommandTests
{
    private readonly TestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();

    private static readonly Microsoft.Extensions.Options.IOptions<WorkloadCatalogOptions> _testCatalogOptions
        = Microsoft.Extensions.Options.Options.Create(new WorkloadCatalogOptions());

    // Real command (not a substitute) — install confirms-yes by Parse+Invoke
    // through this instance, so the same _installer / _store mocks back it.
    private WorkloadUpdateCommand UpdateCommand() => new(_interaction, _installer, _store, _testCatalogOptions);

    private WorkloadInstallCommand NewInstall() => new(_interaction, _installer, _store, UpdateCommand(), _testCatalogOptions);

    [Fact]
    public void Install_HasExpectedArgsAndOptions()
    {
        var cmd = NewInstall();
        cmd.Arguments.Should().ContainSingle(a => a.Name == "id");
        cmd.Options.Should().Contain(o => o.Name == "--force");
        cmd.Options.Should().Contain(o => o.Name == "--version");
        cmd.Options.Should().Contain(o => o.Name == "--source");
        cmd.Options.Should().Contain(o => o.Name == "--prerelease");
    }

    [Fact]
    public async Task Install_PackageId_RoutesToCatalogInstaller()
    {
        StubCatalogResult();

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            "Test.Workload", null, null, (bool?)null, false, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_AllOptionsForwarded()
    {
        StubCatalogResult();

        var cmd = NewInstall();
        int exit = await InvokeAsync(
            cmd,
            "Test.Workload",
            "--version", "2.0.0-beta.1",
            "--source", "https://example/v3/index.json",
            "--prerelease",
            "--exact",
            "--force");

        exit.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            "Test.Workload",
            Arg.Is<NuGetVersion>(v => v != null && v.ToNormalizedString() == "2.0.0-beta.1"),
            "https://example/v3/index.json",
            true,
            true,
            true,
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_ShortAliases_VAndF_Forward()
    {
        StubCatalogResult();

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload", "-v", "1.2.3", "-f");

        exit.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            "Test.Workload",
            Arg.Is<NuGetVersion>(v => v != null && v.ToNormalizedString() == "1.2.3"),
            null,
            (bool?)null,
            false,
            true,
            Arg.Any<IProgress<WorkloadInstallProgress>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_ExactShortAlias_E_Forwards()
    {
        StubCatalogResult();

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "test.workload", "-e");

        exit.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            "test.workload", null, null, (bool?)null, true, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_LocalFolderSource_TreatedAsCatalogSource()
    {
        // --source can be a folder path; the catalog (LocalFolderSourceClient)
        // handles it. The CLI does not special-case paths.
        StubCatalogResult();

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload", "--source", "/tmp/local-feed");

        exit.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            "Test.Workload", null, "/tmp/local-feed", (bool?)null, false, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Install_InvalidVersion_FailsValidation()
    {
        var cmd = NewInstall();
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "Test.Workload", "--version", "not-semver"]);

        parse.Errors.Should().NotBeEmpty();
        parse.Errors.Should().Contain(e => e.Message.Contains("not a valid semver"));
    }

    [Fact]
    public void Install_MissingPackageArg_FailsValidation()
    {
        var cmd = NewInstall();
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name]);

        parse.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Install_WhitespaceArg_FailsValidation()
    {
        var cmd = NewInstall();
        var root = new RootCommand();
        root.Subcommands.Add(cmd);

        ParseResult parse = root.Parse([cmd.Name, "   "]);

        parse.Errors.Should().NotBeEmpty();
        parse.Errors.Should().Contain(e => e.Message.Contains("workload id is required"));
    }

    [Fact]
    public async Task Install_AlreadyInstalled_WritesIdempotentMessage()
    {
        StubCatalogResult(alreadyInstalled: true);

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:")
                && l.Contains("already installed")
                && l.Contains("test.workload"));
    }

    [Fact]
    public async Task Install_ContentKind_MessageMentionsContentPath()
    {
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(
                new WorkloadEntry
                {
                    PackageId = "test.content",
                    PackageVersion = "1.0.0",
                    Kind = WorkloadKind.Content,
                    Source = "https://api.nuget.org/v3/index.json",
                },
                AlreadyInstalled: false));

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "test.content");

        exit.Should().Be(0);
        _interaction.Lines.Should().Contain(l => l.StartsWith("SUCCESS:")
                && l.Contains("content at")
                && l.Contains("api.nuget.org"));
    }

    [Fact]
    public async Task Install_AmbiguousAlias_GracefulException()
    {
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new AmbiguousPackageMatchException(
                "myalias", new[] { "Pkg.A", "Pkg.B" }));

        var cmd = NewInstall();
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "myalias")).Should().ThrowAsync<GracefulException>()).Which;
        ex.Message.Should().Contain("myalias");
        ex.Message.Should().Contain("--exact");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task Install_PackageNotFound_GracefulException()
    {
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new WorkloadPackageNotFoundException("not on any source"));

        var cmd = NewInstall();
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload")).Should().ThrowAsync<GracefulException>()).Which;
        ex.Message.Should().Contain("not on any source");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task Install_InvalidWorkload_GracefulException()
    {
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidWorkloadException("bad package"));

        var cmd = NewInstall();
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload")).Should().ThrowAsync<GracefulException>()).Which;
        ex.Message.Should().Be("bad package");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task Install_BrokenInstall_AppendsForceHint()
    {
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException(
                "Workload 'x' version '1' is already installed at '/p' but is missing from the registry."));

        var cmd = NewInstall();
        GracefulException ex = (await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload")).Should().ThrowAsync<GracefulException>()).Which;
        ex.Message.Should().Contain("missing from the registry");
        ex.Message.Should().Contain("--force");
        ex.IsUserError.Should().BeTrue();
    }

    [Fact]
    public async Task Install_UnexpectedException_PropagatesUnchanged()
    {
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Throws(new NullReferenceException("oops"));

        var cmd = NewInstall();
        await FluentActions.Awaiting(() => InvokeAsync(cmd, "Test.Workload")).Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task Install_LocalNupkgPath_RoutesToLocalInstaller()
    {
        string tempPkg = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.nupkg");
        await File.WriteAllBytesAsync(tempPkg, [0x50, 0x4B]);
        try
        {
            _installer.InstallFromPackageAsync(tempPkg, Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
                .Returns(new WorkloadInstallResult(
                    new WorkloadEntry
                    {
                        PackageId = "test.workload",
                        PackageVersion = "1.0.0",
                        EntryPoint = new EntryPointSpec { AssemblyPath = "x.dll", Type = "T" },
                    },
                    AlreadyInstalled: false));

            var cmd = NewInstall();
            int exit = await InvokeAsync(cmd, tempPkg);

            exit.Should().Be(0);
            await _installer.Received(1).InstallFromPackageAsync(
                tempPkg, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
            await _installer.DidNotReceiveWithAnyArgs().InstallFromCatalogAsync(
                default!, default, default, default, default, default, default);
        }
        finally
        {
            File.Delete(tempPkg);
        }
    }

    [Fact]
    public async Task Install_AlreadyInstalled_NonInteractive_ReturnsOneAndDoesNotInstall()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry
            {
                PackageId = "Test.Workload",
                PackageVersion = "1.0.0",
                Aliases = ["test"],
            },
        });

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(1);
        await _installer.DidNotReceive().InstallFromCatalogAsync(
            Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
            Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
        _interaction.Lines.Should().Contain(l =>
            l.StartsWith("HINT:") && l.Contains("func workload update test"));
    }

    [Fact]
    public async Task Install_AlreadyInstalled_AliasMatch_NonInteractive_HintsCanonicalId()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry
            {
                PackageId = "Test.Workload",
                PackageVersion = "1.2.3",
                Aliases = ["alias1"],
            },
        });

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "alias1");

        exit.Should().Be(1);
        _interaction.Lines.Should().Contain(l =>
            l.StartsWith("HINT:") && l.Contains("alias1") && l.Contains("1.2.3"));
    }

    [Fact]
    public async Task Install_AlreadyInstalled_AliasMatch_NonInteractive_HintsCanonicalIdWhenNoAlias()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry
            {
                PackageId = "Test.Workload",
                PackageVersion = "1.2.3",
                Aliases = [],
            },
        });

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(1);
        _interaction.Lines.Should().Contain(l =>
            l.StartsWith("HINT:") && l.Contains("Test.Workload") && l.Contains("1.2.3"));
    }

    [Fact]
    public async Task Install_AlreadyInstalled_ExactSkipsAliasMatch_ProceedsToInstall()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry
            {
                PackageId = "Test.Workload",
                PackageVersion = "1.0.0",
                Aliases = ["alias1"],
            },
        });
        StubCatalogResult();

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "alias1", "--exact");

        exit.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            "alias1", null, null, (bool?)null, true, false, Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_AlreadyInstalled_ForceSkipsPromptAndInstalls()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            new WorkloadEntry
            {
                PackageId = "Test.Workload",
                PackageVersion = "1.0.0",
                Aliases = ["test"],
            },
        });
        StubCatalogResult();

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload", "--force");

        exit.Should().Be(0);
        await _installer.Received(1).InstallFromCatalogAsync(
            "Test.Workload", null, null, (bool?)null, false, true, Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().GetWorkloadsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Install_PassesProgressReporter_AndRendersDescriptionUpdates()
    {
        // Drive the captured IProgress<...> as if the installer were emitting
        // real phase reports; we should see the descriptions land on the
        // recording progress context so the user sees them as bar labels.
        IProgress<WorkloadInstallProgress>? captured = null;
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Do<IProgress<WorkloadInstallProgress>?>(p => captured = p),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                captured?.Report(new WorkloadInstallProgress(WorkloadInstallPhase.Resolving, "Resolving workload 'Test.Workload'"));
                captured?.Report(new WorkloadInstallProgress(WorkloadInstallPhase.Downloading, "Downloading 'test.workload' 1.0.0"));
                captured?.Report(new WorkloadInstallProgress(WorkloadInstallPhase.Extracting, "Extracting workload 'test.workload' 1.0.0"));
                captured?.Report(new WorkloadInstallProgress(WorkloadInstallPhase.Registering, "Registering workload 'test.workload' 1.0.0"));
                return new WorkloadInstallResult(
                    new WorkloadEntry
                    {
                        PackageId = "test.workload",
                        PackageVersion = "1.0.0",
                        EntryPoint = new EntryPointSpec { AssemblyPath = "x.dll", Type = "T" },
                    },
                    AlreadyInstalled: false);
            });

        var cmd = NewInstall();
        int exit = await InvokeAsync(cmd, "Test.Workload");

        exit.Should().Be(0);
        captured.Should().NotBeNull();
        _interaction.Lines.Should().Contain(l => l.StartsWith("PROGRESS:", StringComparison.Ordinal) && l.Contains("Installing workload 'Test.Workload'", StringComparison.Ordinal));
        _interaction.Lines.Should().Contain(l => l == "PROGRESS: Resolving workload 'Test.Workload'");
        _interaction.Lines.Should().Contain(l => l == "PROGRESS: Downloading 'test.workload' 1.0.0");
        _interaction.Lines.Should().Contain(l => l == "PROGRESS: Extracting workload 'test.workload' 1.0.0");
        _interaction.Lines.Should().Contain(l => l == "PROGRESS: Registering workload 'test.workload' 1.0.0");
    }

    private void StubCatalogResult(bool alreadyInstalled = false, string packageId = "test.workload") =>
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(
                new WorkloadEntry
                {
                    PackageId = packageId,
                    PackageVersion = "1.0.0",
                    EntryPoint = new EntryPointSpec { AssemblyPath = "x.dll", Type = "T" },
                },
                alreadyInstalled));

    private static Task<int> InvokeAsync(WorkloadInstallCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }
}

// Separate fixture so the IsInteractive override doesn't bleed into the
// other tests that rely on the default (non-interactive) behavior.
public class WorkloadInstallNextStepsHintTests
{
    private readonly InteractiveTestInteractionService _interaction = new();
    private readonly IWorkloadInstaller _installer = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();

    private static readonly Microsoft.Extensions.Options.IOptions<WorkloadCatalogOptions> _testCatalogOptions
        = Microsoft.Extensions.Options.Options.Create(new WorkloadCatalogOptions());

    private WorkloadInstallCommand NewInstall() => new(_interaction, _installer, _store, new WorkloadUpdateCommand(_interaction, _installer, _store, _testCatalogOptions), _testCatalogOptions);

    [Theory]
    [InlineData("Azure.Functions.Cli.Workloads.Workers.go", "go")]
    [InlineData("Azure.Functions.Cli.Workloads.Workers.node", "node")]
    [InlineData("Azure.Functions.Cli.Workloads.Go", "go")]
    [InlineData("Azure.Functions.Cli.Workloads.Python", "python")]
    [InlineData("Azure.Functions.Cli.Workloads.Templates.Node", "node")]
    public async Task Install_KnownFeaturePackage_PrintsSetupHint(string packageId, string expectedFeature)
    {
        StubInstall(packageId);

        await InvokeAsync(NewInstall(), packageId);

        _interaction.Lines.Should().Contain(l =>
            l.StartsWith("HINT:", StringComparison.Ordinal)
            && l.Contains("Next steps:", StringComparison.Ordinal)
            && l.Contains($"func setup --features {expectedFeature}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_BundlePackage_SuggestsRuntimeFeature()
    {
        StubInstall(IInstalledBundleWorkloads.BundleWorkloadPackageId);

        await InvokeAsync(NewInstall(), IInstalledBundleWorkloads.BundleWorkloadPackageId);

        _interaction.Lines.Should().Contain(l =>
            l.StartsWith("HINT:", StringComparison.Ordinal)
            && l.Contains("func setup --features runtime", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_HostPackage_SuggestsHostFeature()
    {
        const string hostPackageId = "Azure.Functions.Cli.Workloads.Host.osx-arm64";
        StubInstall(hostPackageId);

        await InvokeAsync(NewInstall(), hostPackageId);

        _interaction.Lines.Should().Contain(l =>
            l.StartsWith("HINT:", StringComparison.Ordinal)
            && l.Contains("func setup --features host", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_UnknownPackage_DoesNotPrintHint()
    {
        StubInstall("Third.Party.Workload");

        await InvokeAsync(NewInstall(), "Third.Party.Workload");

        _interaction.Lines.Should().NotContain(l =>
            l.StartsWith("HINT:", StringComparison.Ordinal) && l.Contains("Next steps:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_AlreadyInstalled_DoesNotPrintHint()
    {
        StubInstall("Azure.Functions.Cli.Workloads.Workers.go", alreadyInstalled: true);

        await InvokeAsync(NewInstall(), "Azure.Functions.Cli.Workloads.Workers.go");

        _interaction.Lines.Should().NotContain(l =>
            l.StartsWith("HINT:", StringComparison.Ordinal) && l.Contains("Next steps:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_NonInteractive_DoesNotPrintHint()
    {
        var nonInteractive = new TestInteractionService();
        StubInstall("Azure.Functions.Cli.Workloads.Workers.go");
        var cmd = new WorkloadInstallCommand(nonInteractive, _installer, _store, new WorkloadUpdateCommand(nonInteractive, _installer, _store, _testCatalogOptions), _testCatalogOptions);

        await InvokeAsync(cmd, "Azure.Functions.Cli.Workloads.Workers.go");

        nonInteractive.Lines.Should().NotContain(l =>
            l.StartsWith("HINT:", StringComparison.Ordinal) && l.Contains("Next steps:", StringComparison.Ordinal));
    }

    private void StubInstall(string packageId, bool alreadyInstalled = false) =>
        _installer.InstallFromCatalogAsync(
                Arg.Any<string>(), Arg.Any<NuGetVersion?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<IProgress<WorkloadInstallProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(
                new WorkloadEntry
                {
                    PackageId = packageId,
                    PackageVersion = "1.0.0",
                    EntryPoint = new EntryPointSpec { AssemblyPath = "x.dll", Type = "T" },
                },
                alreadyInstalled));

    private static Task<int> InvokeAsync(WorkloadInstallCommand cmd, params string[] args)
    {
        var root = new RootCommand();
        root.Subcommands.Add(cmd);
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        return root.Parse(new[] { cmd.Name }.Concat(args).ToArray()).InvokeAsync(config);
    }

    private sealed class InteractiveTestInteractionService : TestInteractionService
    {
        public override bool IsInteractive => true;
    }
}
