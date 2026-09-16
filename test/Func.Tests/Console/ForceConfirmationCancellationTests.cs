// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Tests.Commands.Quickstart;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console;

public class ForceConfirmationCancellationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Force_CancellationBeforeClearing_DoesNotModifyDirectory(bool quickstart, bool gitOnly)
    {
        using var cancellation = new CancellationTokenSource();
        var interaction = Substitute.For<IInteractionService>();
        interaction.ConfirmAsync("Continue?", false, true, Arg.Any<CancellationToken>()).Returns(call =>
        {
            CancellationToken forwardedToken = call.Arg<CancellationToken>();
            forwardedToken.CanBeCanceled.Should().BeTrue();
            // Model cancellation arriving as approval or the no-input fallback completes.
            cancellation.Cancel();
            forwardedToken.IsCancellationRequested.Should().BeTrue();
            return Task.FromResult(true);
        });
        interaction.ShowStatusAsync(Arg.Any<string>(), Arg.Any<Func<CancellationToken, Task<QuickstartManifest>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<QuickstartManifest>>>()(call.Arg<CancellationToken>()));
        interaction.StatusAsync(Arg.Any<string>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));

        bool scaffolded = false;
        bool commandConstructed = false;
        FuncCliCommand command;
        if (quickstart)
        {
            var provider = QuickstartTestHelpers.CreateProvider();
            var manifest = new QuickstartManifest([QuickstartTestHelpers.CreateEntry()]);
            var resolver = Substitute.For<IQuickstartProviderResolver>();
            resolver.SelectProviderAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(provider);
            resolver.ResolveOrPromptLanguageAsync(Arg.Any<string?>(), provider, manifest, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (gitOnly) cancellation.Cancel();
                return Task.FromResult<(string?, int?)>(("Python", null));
            });
            var manifestService = Substitute.For<IQuickstartManifestService>();
            manifestService.GetManifestAsync(Arg.Any<CancellationToken>()).Returns(manifest);
            var scaffolder = Substitute.For<IQuickstartScaffolder>();
            scaffolder.ScaffoldAsync(Arg.Any<QuickstartEntry>(), Arg.Any<string>(), Arg.Any<FetchMode>(), Arg.Any<CancellationToken>())
                .Returns(_ => { scaffolded = true; return Task.CompletedTask; });
            command = new QuickstartCommand(
                new QuickstartListCommand(interaction, resolver, manifestService, [provider]),
                new QuickstartInfoCommand(interaction, resolver, manifestService),
                interaction, resolver, manifestService, scaffolder, [provider]);
        }
        else
        {
            var initializer = Substitute.For<IProjectInitializer>();
            initializer.Stack.Returns("python");
            initializer.SupportedLanguages.Returns(_ =>
            {
                if (gitOnly && commandConstructed) cancellation.Cancel();
                return new[] { "python" };
            });
            initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns([]);
            initializer.InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>())
                .Returns(_ => { scaffolded = true; return Task.CompletedTask; });
            var localSettings = Substitute.For<ILocalSettingsProvider>();
            localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(LocalSettingsSnapshot.Empty);
            var bundles = Substitute.For<IInstalledBundleWorkloads>();
            bundles.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([]);
            command = new InitCommand(interaction, Substitute.For<IWorkloadHintRenderer>(), localSettings,
                Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(),
                new InstalledBundleScanner(bundles), [initializer]);
        }
        commandConstructed = true;

        string directory = Path.Combine(Path.GetTempPath(), $"func-force-cancel-{Guid.NewGuid():N}");
        string gitDirectory = Path.Combine(directory, ".git");
        Directory.CreateDirectory(gitDirectory);
        try
        {
            string gitSentinel = Path.Combine(gitDirectory, "keep.txt");
            string sentinel = Path.Combine(directory, "keep.txt");
            await File.WriteAllTextAsync(gitSentinel, "git content");
            if (!gitOnly) await File.WriteAllTextAsync(sentinel, "original content");
            ParseResult parsed = command.Parse([directory, "--stack", "python", "--language", "python", "--force"]);

            await FluentActions.Awaiting(() => parsed.InvokeAsync(
                    new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>();

            cancellation.IsCancellationRequested.Should().BeTrue();
            scaffolded.Should().BeFalse();
            Directory.GetFileSystemEntries(directory).Should().HaveCount(gitOnly ? 1 : 2);
            (await File.ReadAllTextAsync(gitSentinel)).Should().Be("git content");
            if (!gitOnly) (await File.ReadAllTextAsync(sentinel)).Should().Be("original content");
            await interaction.Received(gitOnly ? 0 : 1).ConfirmAsync("Continue?", false, true, Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}