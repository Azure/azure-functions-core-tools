// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands.Update;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using NSubstitute;
using Semver;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Update;

public sealed class UpdateCommandTests
{
    private static readonly Release _newerRelease = new(
        SemVersion.Parse("5.2.0", SemVersionStyles.Strict),
        new Uri("public/cli/v5/5.2.0/Azure.Functions.Cli.linux-x64.5.2.0.zip", UriKind.Relative));

    private static readonly Release _sameRelease = new(
        SemVersion.Parse("5.1.0", SemVersionStyles.Strict),
        new Uri("public/cli/v5/5.1.0/Azure.Functions.Cli.linux-x64.5.1.0.zip", UriKind.Relative));

    [Fact]
    public async Task ExecuteAsync_PackageManagerInstall_PrintsHintAndDoesNotUpdate()
    {
        var harness = new Harness();
        harness.InstallMethodDetector.Detect().Returns(
            new InstallMethod(InstallMethodKind.Homebrew, "Homebrew", "brew upgrade azure-functions-core-tools"));

        int exitCode = await harness.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains(harness.Interaction.Lines, l =>
            l.Contains("Homebrew", StringComparison.Ordinal)
            && l.Contains("brew upgrade azure-functions-core-tools", StringComparison.Ordinal));
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
        await harness.ReleaseFeed.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyUpToDate_PrintsSuccessAndDoesNotUpdate()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns(_sameRelease);

        int exitCode = await harness.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains(harness.Interaction.Lines, l =>
            l.StartsWith("SUCCESS:", StringComparison.Ordinal)
            && l.Contains("up to date", StringComparison.OrdinalIgnoreCase));
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_UpdateAvailable_WithYes_SkipsPromptAndUpdates()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--yes");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(harness.Interaction.Lines, l => l.StartsWith("CONFIRM:", StringComparison.Ordinal));
        await harness.Updater.Received(1).UpdateAsync(
            Arg.Is<Release>(r => r.Version == _newerRelease.Version),
            Arg.Any<IProgress<UpdateProgress>?>(),
            Arg.Any<CancellationToken>());
        Assert.Contains(harness.Interaction.Lines, l =>
            l.StartsWith("SUCCESS:", StringComparison.Ordinal)
            && l.Contains("5.2.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_UpdateAvailable_Interactive_PromptsAndUpdatesOnConfirm()
    {
        var harness = new Harness(isInteractive: true, confirmResponse: true);
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains(harness.Interaction.Lines, l =>
            l.StartsWith("CONFIRM:", StringComparison.Ordinal)
            && l.Contains("5.2.0", StringComparison.Ordinal));
        await harness.Updater.Received(1).UpdateAsync(
            Arg.Any<Release>(), Arg.Any<IProgress<UpdateProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UpdateAvailable_Interactive_UserDeclines_DoesNotUpdate()
    {
        var harness = new Harness(isInteractive: true, confirmResponse: false);
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync();

        Assert.Equal(0, exitCode);
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
        Assert.Contains(harness.Interaction.Lines, l => l.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_UpdateAvailable_NonInteractive_WithoutYes_ThrowsGraceful()
    {
        var harness = new Harness(isInteractive: false);
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns(_newerRelease);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(() => harness.InvokeAsync());

        Assert.True(ex.IsUserError);
        Assert.Contains("--yes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_PrereleaseFlag_PassesTrueToFeed()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(true, Arg.Any<CancellationToken>())
            .Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--prerelease", "--yes");

        Assert.Equal(0, exitCode);
        await harness.ReleaseFeed.Received(1).GetLatestAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_VersionOption_CallsGetVersionAsync()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetVersionAsync(Arg.Is<SemVersion>(v => v.ToString() == "5.2.0"), Arg.Any<CancellationToken>())
            .Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--version", "5.2.0", "--yes");

        Assert.Equal(0, exitCode);
        await harness.ReleaseFeed.Received(1)
            .GetVersionAsync(Arg.Is<SemVersion>(v => v.ToString() == "5.2.0"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_VersionOption_InvalidVersion_ThrowsGraceful()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.1.0");

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => harness.InvokeAsync("--version", "not-a-version", "--yes"));

        Assert.True(ex.IsUserError);
        Assert.Contains("--version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_FeedThrowsInvalidOperation_WrapsAsGraceful()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns<Release>(_ => throw new InvalidOperationException("feed down"));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(() => harness.InvokeAsync("--yes"));

        Assert.True(ex.IsUserError);
        Assert.Contains("feed down", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_UpdaterThrowsInvalidOperation_WrapsAsGraceful()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.1.0");
        harness.ReleaseFeed
            .GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns(_newerRelease);
        harness.Updater
            .UpdateAsync(Arg.Any<Release>(), Arg.Any<IProgress<UpdateProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("bad artifact"));

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(() => harness.InvokeAsync("--yes"));

        Assert.True(ex.IsUserError);
        Assert.Contains("bad artifact", ex.Message, StringComparison.Ordinal);
    }

    private sealed class Harness
    {
        public Harness(bool isInteractive = false, bool confirmResponse = false)
        {
            Interaction = new StubInteractionService(isInteractive, confirmResponse);
            ReleaseFeed = Substitute.For<IReleaseFeed>();
            Updater = Substitute.For<ICliUpdater>();
            InstallMethodDetector = Substitute.For<IInstallMethodDetector>();
            InstallMethodDetector.Detect().Returns(InstallMethod.Direct);
            VersionProvider = Substitute.For<ICliVersionProvider>();
            VersionProvider.Version.Returns("5.1.0");
            VersionProvider.InformationalVersion.Returns("5.1.0+abc");

            Command = new UpdateCommand(ReleaseFeed, Updater, InstallMethodDetector, VersionProvider, Interaction);
        }

        public StubInteractionService Interaction { get; }

        public IReleaseFeed ReleaseFeed { get; }

        public ICliUpdater Updater { get; }

        public IInstallMethodDetector InstallMethodDetector { get; }

        public ICliVersionProvider VersionProvider { get; }

        public UpdateCommand Command { get; }

        public async Task<int> InvokeAsync(params string[] args)
        {
            var root = new RootCommand { Command };
            ParseResult parseResult = root.Parse(["update", .. args]);
            var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
            return await parseResult.InvokeAsync(config);
        }
    }

    private sealed class StubInteractionService(bool isInteractive, bool confirmResponse) : TestInteractionService
    {
        private readonly bool _confirmResponse = confirmResponse;

        public override bool IsInteractive { get; } = isInteractive;

        public override Task<bool> ConfirmAsync(string prompt, bool defaultValue = false, CancellationToken cancellationToken = default)
        {
            // Reuse base capture so tests can still assert on the prompt line,
            // but return the caller-controlled answer instead of the default.
            base.ConfirmAsync(prompt, defaultValue, cancellationToken);
            return Task.FromResult(_confirmResponse);
        }
    }
}
