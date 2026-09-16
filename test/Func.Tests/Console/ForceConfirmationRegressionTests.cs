// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Quickstart;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Tests.Commands.Quickstart;
using Azure.Functions.Cli.Workloads;
using NSubstitute;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Console;

public class ForceConfirmationRegressionTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));

    public enum Response
    {
        No,
        Enter,
        Yes,
        Cancel,
        InputFailure,
    }

    public static IEnumerable<object[]> Profiles()
    {
        foreach (bool stdoutInput in new[] { false, true })
        foreach (bool stdoutAnsi in new[] { false, true })
        foreach (bool stderrInput in new[] { false, true })
        foreach (bool stderrAnsi in new[] { false, true })
        foreach (Response response in Enum.GetValues<Response>())
        {
            yield return [stdoutInput, stdoutAnsi, stderrInput, stderrAnsi, response];
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task Init_Force_OnlyClearsWhenConfirmedOrInputUnavailable(
        bool stdoutInput, bool stdoutAnsi, bool stderrInput, bool stderrAnsi, Response response)
    {
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("python");
        initializer.SupportedLanguages.Returns(["python"]);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns([]);
        var localSettings = Substitute.For<ILocalSettingsProvider>();
        localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(LocalSettingsSnapshot.Empty);
        var bundles = Substitute.For<IInstalledBundleWorkloads>();
        bundles.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([]);

        await AssertForceConfirmationAsync(
            service => new InitCommand(service, Substitute.For<IWorkloadHintRenderer>(), localSettings,
                Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(),
                new InstalledBundleScanner(bundles), [initializer]),
            stdoutInput, stdoutAnsi, stderrInput, stderrAnsi, response, CancellationToken.None);

        await initializer.Received(!stdoutInput || response == Response.Yes ? 1 : 0).InitializeAsync(
            Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task Quickstart_Force_OnlyClearsWhenConfirmedOrInputUnavailable(
        bool stdoutInput, bool stdoutAnsi, bool stderrInput, bool stderrAnsi, Response response)
    {
        var provider = QuickstartTestHelpers.CreateProvider();
        var entry = QuickstartTestHelpers.CreateEntry();
        var manifest = new QuickstartManifest([entry]);
        var resolver = Substitute.For<IQuickstartProviderResolver>();
        resolver.SelectProviderAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(provider);
        resolver.ResolveOrPromptLanguageAsync(Arg.Any<string?>(), provider, manifest, Arg.Any<CancellationToken>())
            .Returns(("Python", (int?)null));
        var manifestService = Substitute.For<IQuickstartManifestService>();
        manifestService.GetManifestAsync(Arg.Any<CancellationToken>()).Returns(manifest);
        var scaffolder = Substitute.For<IQuickstartScaffolder>();

        await AssertForceConfirmationAsync(service => new QuickstartCommand(
                new QuickstartListCommand(service, resolver, manifestService, [provider]),
                new QuickstartInfoCommand(service, resolver, manifestService),
                service, resolver, manifestService, scaffolder, [provider]),
            stdoutInput, stdoutAnsi, stderrInput, stderrAnsi, response, CancellationToken.None);

        await scaffolder.Received(!stdoutInput || response == Response.Yes ? 1 : 0).ScaffoldAsync(
            entry, Arg.Any<string>(), Arg.Any<FetchMode>(), Arg.Any<CancellationToken>());
    }

    private async Task AssertForceConfirmationAsync(
        Func<IInteractionService, FuncCliCommand> createCommand,
        bool stdoutInput, bool stdoutAnsi, bool stderrInput, bool stderrAnsi, Response response, CancellationToken cancellationToken)
    {
        using var stdout = new BufferedConsole(stdoutInput, stdoutAnsi);
        using var stderr = new BufferedConsole(stderrInput, stderrAnsi);
        stdout.Profile.Capabilities.Interactive.Should().Be(stdoutInput);
        stdout.Profile.Capabilities.Ansi.Should().Be(stdoutAnsi);
        stderr.Profile.Capabilities.Interactive.Should().Be(stderrInput);
        stderr.Profile.Capabilities.Ansi.Should().Be(stderrAnsi);
        switch (response)
        {
            case Response.No:
                stdout.Enqueue(ConsoleKey.N, 'n');
                stdout.Enqueue(ConsoleKey.Enter);
                break;
            case Response.Enter:
                stdout.Enqueue(ConsoleKey.Enter);
                break;
            case Response.Yes:
                stdout.Enqueue(ConsoleKey.Y, 'y');
                stdout.Enqueue(ConsoleKey.Enter);
                break;
            case Response.Cancel:
                stdout.Enqueue(ConsoleKey.C, '\u0003', control: true);
                break;
            case Response.InputFailure:
                // Exhausting scripted input throws rather than returning a default.
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(response));
        }
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var command = createCommand(service);
        string directory = Path.Combine(Path.GetTempPath(), $"func-force-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string sentinel = Path.Combine(directory, "keep.txt");
        string gitDirectory = Path.Combine(directory, ".git");
        Directory.CreateDirectory(gitDirectory);
        string gitSentinel = Path.Combine(gitDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "original content", cancellationToken);
        await File.WriteAllTextAsync(gitSentinel, "git content", cancellationToken);

        try
        {
            int? result = null;
            var exception = await Record.ExceptionAsync(async () =>
            {
                result = await command.Parse([directory, "--stack", "python", "--language", "python", "--force"])
                    .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellationToken);
            });
            bool shouldClear = !stdoutInput || response == Response.Yes;

            _output.WriteLine(
                $"command={command.Name}, stdoutInput={stdoutInput}, stdoutAnsi={stdoutAnsi}, " +
                $"stderrInput={stderrInput}, stderrAnsi={stderrAnsi}, response={response}, IsInteractive={service.IsInteractive}, " +
                $"exit={result}, stdoutReads={stdout.Reads}, stderrReads={stderr.Reads}, " +
                $"sentinelExists={File.Exists(sentinel)}, gitSentinelExists={File.Exists(gitSentinel)}");
            File.Exists(sentinel).Should().Be(!shouldClear, "only confirmation or unavailable input authorizes --force deletion");
            if (stdoutInput && response == Response.Cancel)
            {
                exception.Should().BeAssignableTo<OperationCanceledException>();
                result.Should().BeNull();
            }
            else if (stdoutInput && response == Response.InputFailure)
            {
                exception.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("scripted input");
                result.Should().BeNull();
            }
            else
            {
                exception.Should().BeNull();
                result.Should().Be(shouldClear ? 0 : 1);
            }
            stdout.Reads.Should().Be(stdoutInput ? response is Response.No or Response.Yes ? 2 : 1 : 0);
            stderr.Reads.Should().Be(0);
            (await File.ReadAllTextAsync(gitSentinel, cancellationToken)).Should().Be("git content");
            if (!shouldClear)
            {
                (await File.ReadAllTextAsync(sentinel, cancellationToken)).Should().Be("original content");
                Directory.GetFileSystemEntries(directory).Should().HaveCount(2);
            }
            if (stdoutInput)
            {
                stdout.Output.Should().Contain("Continue?");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}