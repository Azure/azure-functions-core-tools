// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Update;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Update;
using NSubstitute;
using Semver;

namespace Azure.Functions.Cli.Tests.Commands.Update;

public sealed class UpdateCommandTests
{
    private static readonly Release _newerRelease = CreateRelease("5.2.0");
    private static readonly Release _sameRelease = CreateRelease("5.1.0");

    public static TheoryData<string> ReaderFailureCategories =>
    [
        "missing",
        "invalid-operation",
        "too-large",
        "unsupported-schema",
        "invalid-metadata",
        "io",
        "unauthorized",
    ];

    public static TheoryData<string> ExternalMethods =>
    [
        "npm",
        "future-package-manager",
        "custom-enterprise-installer",
    ];

    [Fact]
    public async Task ExecuteAsync_StandaloneInPlace_ReadsMetadataBeforeResolvingAndUpdating()
    {
        var harness = new Harness();
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--yes");

        exitCode.Should().Be(0);
        Received.InOrder(() =>
        {
            harness.MetadataReader.ReadAsync(Arg.Any<CancellationToken>());
            harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>());
            harness.Updater.UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>());
        });
    }

    [Theory]
    [MemberData(nameof(ExternalMethods))]
    public async Task ExecuteAsync_ExternalPolicy_RendersGenericGuidanceAndDoesNotResolveOrUpdate(string installMethod)
    {
        var harness = new Harness();
        harness.MetadataReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new InstallationMetadata(installMethod, InstallationUpdatePolicy.External));

        int exitCode = await harness.InvokeAsync();

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should()
            .Contain("managed externally")
            .And.Contain($"installation method: '{installMethod}'")
            .And.Contain("same installer or package manager")
            .And.Contain("https://aka.ms/func-cli");
        await harness.ReleaseFeed.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
        await harness.ReleaseFeed.DidNotReceiveWithAnyArgs().GetVersionAsync(default!, default);
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Theory]
    [InlineData("standalone")]
    [InlineData("future-installer")]
    public async Task ExecuteAsync_InPlacePolicy_UpdatesRegardlessOfOpaqueInstallMethod(string installMethod)
    {
        var harness = new Harness();
        harness.MetadataReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new InstallationMetadata(installMethod, InstallationUpdatePolicy.InPlace));
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--yes");

        exitCode.Should().Be(0);
        await harness.Updater.Received(1)
            .UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownUpdatePolicy_FailsClosed()
    {
        var harness = new Harness();
        harness.MetadataReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(new InstallationMetadata("future-installer", (InstallationUpdatePolicy)999));

        Func<Task> act = async () => await harness.InvokeAsync("--yes");

        GracefulException exception = (await act.Should().ThrowAsync<GracefulException>()).Which;
        exception.IsUserError.Should().BeTrue();
        exception.Message.Should().Contain("https://aka.ms/func-cli");
        await harness.ReleaseFeed.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Theory]
    [MemberData(nameof(ReaderFailureCategories))]
    public async Task ExecuteAsync_DocumentedMetadataReaderFailure_FailsClosedWithReinstallGuidance(string category)
    {
        var harness = new Harness();
        Exception readerFailure = CreateReaderFailure(category);
        harness.MetadataReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<InstallationMetadata>(_ => throw readerFailure);

        Func<Task> act = async () => await harness.InvokeAsync("--yes");

        GracefulException exception = (await act.Should().ThrowAsync<GracefulException>()).Which;
        exception.IsUserError.Should().BeTrue();
        exception.InnerException.Should().BeSameAs(readerFailure);
        exception.Message.Should().Contain("https://aka.ms/func-cli");
        await harness.ReleaseFeed.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedMetadataReaderFailure_Propagates()
    {
        var harness = new Harness();
        harness.MetadataReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<InstallationMetadata>(_ => throw new NotSupportedException("unexpected"));

        Func<Task> act = async () => await harness.InvokeAsync("--yes");

        NotSupportedException exception = (await act.Should().ThrowAsync<NotSupportedException>()).Which;
        exception.Message.Should().Be("unexpected");
    }

    [Fact]
    public async Task ExecuteAsync_YesFlag_SkipsConfirmation()
    {
        var harness = new Harness(promptAvailable: true, confirmResponse: false);
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--yes");

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().NotContain("CONFIRM:");
        await harness.Updater.Received(1).UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PromptAvailableWithoutBroadInteractiveSupport_UsesPromptResponse()
    {
        var harness = new Harness(promptAvailable: true, confirmResponse: true);
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync();

        exitCode.Should().Be(0);
        harness.Interaction.IsInteractive.Should().BeFalse();
        harness.Interaction.AllOutput.Should().Contain("CONFIRM:");
        await harness.Updater.Received(1).UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PromptUnavailable_UsesSafeDeclineFallback()
    {
        var harness = new Harness(promptAvailable: false, confirmResponse: true);
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync();

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("Update cancelled.");
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_StableSelection_RequestsLatestStable()
    {
        var harness = new Harness();
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        await harness.InvokeAsync("--yes");

        await harness.ReleaseFeed.Received(1).GetLatestAsync(false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PrereleaseSelection_IncludesPrereleases()
    {
        var harness = new Harness();
        harness.ReleaseFeed.GetLatestAsync(true, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        await harness.InvokeAsync("--prerelease", "--yes");

        await harness.ReleaseFeed.Received(1).GetLatestAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_PinnedSelection_RequestsExactVersion()
    {
        var harness = new Harness();
        harness.ReleaseFeed.GetVersionAsync(
            Arg.Is<SemVersion>(version => version.ToString() == "5.2.0"),
            Arg.Any<CancellationToken>())
            .Returns(_newerRelease);

        await harness.InvokeAsync("--version", "5.2.0", "--yes");

        await harness.ReleaseFeed.Received(1).GetVersionAsync(
            Arg.Is<SemVersion>(version => version.ToString() == "5.2.0"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPinnedVersion_FailsBeforeCallingFeed()
    {
        var harness = new Harness();

        Func<Task> act = async () => await harness.InvokeAsync("--version", "not-a-version", "--yes");

        GracefulException exception = (await act.Should().ThrowAsync<GracefulException>()).Which;
        exception.IsUserError.Should().BeTrue();
        exception.Message.Should().Contain("--version");
        await harness.ReleaseFeed.DidNotReceiveWithAnyArgs().GetVersionAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ReleaseFeedKnownFailure_WrapsAsUserError()
    {
        var harness = new Harness();
        var feedFailure = new InvalidOperationException("release feed unavailable");
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>())
            .Returns<Release>(_ => throw feedFailure);

        Func<Task> act = async () => await harness.InvokeAsync("--yes");

        GracefulException exception = (await act.Should().ThrowAsync<GracefulException>()).Which;
        exception.IsUserError.Should().BeTrue();
        exception.InnerException.Should().BeSameAs(feedFailure);
    }

    [Fact]
    public async Task ExecuteAsync_LatestIsOlderThanCurrent_DoesNotDowngrade()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.3.0-preview.1");
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--yes");

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("newer").And.Contain("No update was performed");
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_PinnedOlderVersion_AnnouncesAndPerformsDowngrade()
    {
        var harness = new Harness();
        harness.VersionProvider.Version.Returns("5.3.0");
        harness.ReleaseFeed.GetVersionAsync(_newerRelease.Version, Arg.Any<CancellationToken>()).Returns(_newerRelease);

        int exitCode = await harness.InvokeAsync("--version", "5.2.0", "--yes");

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("Downgrading Azure Functions CLI");
        await harness.Updater.Received(1).UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCurrent_DoesNotUpdate()
    {
        var harness = new Harness();
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_sameRelease);

        int exitCode = await harness.InvokeAsync("--yes");

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should().Contain("already up to date");
        await harness.Updater.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_PassesThroughAndDoesNotResolveRelease()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        harness.MetadataReader.ReadAsync(Arg.Any<CancellationToken>())
            .Returns<InstallationMetadata>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));

        Func<Task> act = async () => await harness.InvokeAsync(cancellation.Token, "--yes");

        OperationCanceledException exception = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.IsCancellationRequested.Should().BeTrue();
        await harness.ReleaseFeed.DidNotReceiveWithAnyArgs().GetLatestAsync(default, default);
    }

    [Fact]
    public async Task ExecuteAsync_UpdaterCancellation_Propagates()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);
        harness.Updater.UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(call =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(call.Arg<CancellationToken>());
            });

        Func<Task> act = async () => await harness.InvokeAsync(cancellation.Token, "--yes");

        OperationCanceledException exception = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
        exception.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedUpdaterFailure_Propagates()
    {
        var harness = new Harness();
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);
        harness.Updater.UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidDataException("unexpected updater failure"));

        Func<Task> act = async () => await harness.InvokeAsync("--yes");

        InvalidDataException exception = (await act.Should().ThrowAsync<InvalidDataException>()).Which;
        exception.Message.Should().Be("unexpected updater failure");
    }

    [Fact]
    public async Task ExecuteAsync_UpdaterProgress_RendersPhasesAndBytes()
    {
        var harness = new Harness();
        harness.ReleaseFeed.GetLatestAsync(false, Arg.Any<CancellationToken>()).Returns(_newerRelease);
        harness.Updater.UpdateAsync(_newerRelease, Arg.Any<IProgress<UpdateProgress>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                IProgress<UpdateProgress> progress = call.Arg<IProgress<UpdateProgress>>();
                progress.Report(new UpdateProgress(UpdatePhase.Downloading, BytesRead: 64, TotalBytes: 128));
                progress.Report(new UpdateProgress(UpdatePhase.Extracting));
                progress.Report(new UpdateProgress(UpdatePhase.Installing));
                progress.Report(new UpdateProgress(UpdatePhase.Verifying));
                return Task.CompletedTask;
            });

        int exitCode = await harness.InvokeAsync("--yes");

        exitCode.Should().Be(0);
        harness.Interaction.AllOutput.Should()
            .Contain("PROGRESS TOTAL: 128")
            .And.Contain("PROGRESS REPORT: 64")
            .And.Contain("Extracting update package")
            .And.Contain("Installing update")
            .And.Contain("Verifying installation");
    }

    [Fact]
    public void CommandSurface_UsesExpectedNameOptionsAndDescriptions()
    {
        var harness = new Harness();

        harness.Command.Name.Should().Be("update");
        harness.Command.Description.Should().Contain("Azure Functions CLI");
        harness.Command.Options.Select(option => option.Name).Should().Contain(["--prerelease", "--version", "--yes"]);
        harness.Command.Options.Should().OnlyContain(option => !string.IsNullOrWhiteSpace(option.Description));
    }

    [Fact]
    public void Parser_RegistersUpdateAsTopLevelCommand()
    {
        var interaction = new TestInteractionService();

        FuncRootCommand root = TestParser.CreateRoot(interaction);

        root.Subcommands.Should().ContainSingle(command => command.Name == "update");
    }

    private static Exception CreateReaderFailure(string category) => category switch
    {
        "missing" => new FileNotFoundException("metadata is missing"),
        "invalid-operation" => new InvalidOperationException("process path is unavailable"),
        "too-large" => new InstallationMetadataTooLargeException("metadata is too large", new IOException("limit exceeded")),
        "unsupported-schema" => new UnsupportedInstallationMetadataSchemaException("schema is unsupported"),
        "invalid-metadata" => new InvalidInstallationMetadataException("metadata is invalid"),
        "io" => new IOException("canonical path resolution failed"),
        "unauthorized" => new UnauthorizedAccessException("installation metadata is inaccessible"),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    private static Release CreateRelease(string version) =>
        new(
            SemVersion.Parse(version, SemVersionStyles.Strict),
            new Uri($"public/cli/v5/{version}/Azure.Functions.Cli.win-x64.{version}.zip", UriKind.Relative))
        {
            Sha256Checksum = new string('a', 64),
        };

    private sealed class Harness
    {
        public Harness(bool promptAvailable = false, bool confirmResponse = false)
        {
            Interaction = new StubInteractionService(promptAvailable, confirmResponse);
            ReleaseFeed = Substitute.For<IReleaseFeed>();
            Updater = Substitute.For<ICliUpdater>();
            MetadataReader = Substitute.For<IInstallationMetadataReader>();
            MetadataReader.ReadAsync(Arg.Any<CancellationToken>())
                .Returns(new InstallationMetadata("standalone", InstallationUpdatePolicy.InPlace));
            VersionProvider = Substitute.For<ICliVersionProvider>();
            VersionProvider.Version.Returns("5.1.0");
            VersionProvider.InformationalVersion.Returns("5.1.0+test");
            Command = new UpdateCommand(ReleaseFeed, Updater, MetadataReader, VersionProvider, Interaction);
        }

        public StubInteractionService Interaction { get; }

        public IReleaseFeed ReleaseFeed { get; }

        public ICliUpdater Updater { get; }

        public IInstallationMetadataReader MetadataReader { get; }

        public ICliVersionProvider VersionProvider { get; }

        public UpdateCommand Command { get; }

        public Task<int> InvokeAsync(params string[] args) => InvokeAsync(CancellationToken.None, args);

        public async Task<int> InvokeAsync(CancellationToken cancellationToken, params string[] args)
        {
            var root = new RootCommand { Command };
            ParseResult parseResult = root.Parse(["update", .. args]);
            var configuration = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
            return await parseResult.InvokeAsync(configuration, cancellationToken);
        }
    }

    private sealed class StubInteractionService(bool promptAvailable, bool confirmResponse) : TestInteractionService
    {
        public override async Task<bool> ConfirmAsync(
            string prompt,
            bool defaultValue,
            bool whenInputUnavailable,
            CancellationToken cancellationToken = default)
        {
            bool fallback = await base.ConfirmAsync(prompt, defaultValue, whenInputUnavailable, cancellationToken);
            return promptAvailable ? confirmResponse : fallback;
        }
    }
}
