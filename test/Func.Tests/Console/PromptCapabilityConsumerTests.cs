// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console;

public class PromptCapabilityConsumerTests
{
    [Theory]
    [MemberData(nameof(SpectreInteractionServiceTests.Capabilities), MemberType = typeof(SpectreInteractionServiceTests))]
    public async Task NewTemplateSelector_UsesWholeCommandCapabilities(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var selector = new NewCommandTemplateSelector(service, new TemplatePicker(service));
        FunctionTemplateInfo first = CreateTemplate("first");
        FunctionTemplateInfo second = CreateTemplate("second");
        var invocation = new NewInvocation(WorkingDirectory.FromExplicit(Path.GetTempPath()), null, null, false, false);

        var result = await selector.SelectAsync(invocation, [first, second], CancellationToken.None);

        if (input && stdoutAnsi && stderrAnsi)
        {
            result.Should().BeSameAs(second);
            stderr.Reads.Should().Be(2);
        }
        else
        {
            result.Should().BeNull();
            stderr.Output.Should().Contain("Missing required option: --template");
            stderr.Reads.Should().Be(0);
        }
        stdout.Reads.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(SpectreInteractionServiceTests.Capabilities), MemberType = typeof(SpectreInteractionServiceTests))]
    public async Task Init_MissingLanguage_UsesWholeCommandCapabilities(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        stderr.Enqueue(ConsoleKey.DownArrow);
        stderr.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var initializer = Substitute.For<IProjectInitializer>();
        initializer.Stack.Returns("node");
        initializer.SupportedLanguages.Returns(["javascript", "typescript"]);
        initializer.GetInitOptions(Arg.Any<IInitOptionRegistry>()).Returns([]);
        var localSettings = Substitute.For<ILocalSettingsProvider>();
        localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(LocalSettingsSnapshot.Empty);
        var installed = Substitute.For<IInstalledBundleWorkloads>();
        installed.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([]);
        var command = new InitCommand(service, Substitute.For<IWorkloadHintRenderer>(), localSettings,
            Substitute.For<IFunctionsProjectResolver>(), Substitute.For<IHostJsonBundleSectionReader>(), new InstalledBundleScanner(installed), [initializer]);
        string directory = Path.Combine(Path.GetTempPath(), $"func-5511-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            int result = await command.Parse([directory, "--stack", "node"]).InvokeAsync();

            if (input && stdoutAnsi && stderrAnsi)
            {
                result.Should().Be(0);
                await initializer.Received(1).InitializeAsync(Arg.Any<InitContext>(), Arg.Any<ParseResult>(), Arg.Any<CancellationToken>());
                stderr.Reads.Should().Be(2);
            }
            else
            {
                result.Should().Be(1);
                stderr.Output.Should().Contain("--language <javascript|typescript>");
                await initializer.DidNotReceiveWithAnyArgs().InitializeAsync(default!, default!, default);
                Directory.GetFileSystemEntries(directory).Should().BeEmpty();
                stderr.Reads.Should().Be(0);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(SpectreInteractionServiceTests.Capabilities), MemberType = typeof(SpectreInteractionServiceTests))]
    public async Task FirstRun_UnsafeCommandConsole_DoesNotPromptInstallOrMark(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        stdout.Enqueue(ConsoleKey.N, 'n');
        stdout.Enqueue(ConsoleKey.Enter);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        var store = Substitute.For<IFirstRunStateStore>();
        store.GetStateAsync(Arg.Any<CancellationToken>()).Returns(FirstRunState.NeverPrompted);
        var setup = Substitute.For<ISetupRunner>();
        var coordinator = new FirstRunCoordinator(service, store, setup);

        (await coordinator.EnsureFirstRunPromptedAsync("unknown", new RootCommand().Parse([]), CancellationToken.None)).Should().BeNull();

        await setup.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        if (input && stdoutAnsi && stderrAnsi)
        {
            stdout.Reads.Should().Be(2);
            await store.Received(1).MarkCompleteAsync(Arg.Any<CancellationToken>());
        }
        else
        {
            stdout.Reads.Should().Be(0);
            stdout.Output.Should().BeEmpty();
            await store.DidNotReceiveWithAnyArgs().GetStateAsync(default);
            await store.DidNotReceiveWithAnyArgs().MarkCompleteAsync(default);
        }
    }

    [Theory]
    [MemberData(nameof(SpectreInteractionServiceTests.Capabilities), MemberType = typeof(SpectreInteractionServiceTests))]
    public void DashboardMode_UsesWholeCommandCapabilities(bool input, bool stdoutAnsi, bool stderrAnsi, bool noColor)
    {
        using var stdout = new BufferedConsole(input, stdoutAnsi, noColor);
        using var stderr = new BufferedConsole(input, stderrAnsi, noColor);
        var service = new SpectreInteractionService(new DefaultTheme(), stdout, stderr);
        bool interactive = input && stdoutAnsi && stderrAnsi;
        OutputMode expected = interactive ? OutputMode.Compact : OutputMode.Plain;

        OutputModeResolver.Resolve(null, false, service).Should().Be(expected);
        OutputModeResolver.ApplyTerminalSafetyFallback(OutputMode.Compact, service, out bool downgraded).Should().Be(expected);
        downgraded.Should().Be(!interactive);
        OutputModeResolver.Resolve(OutputMode.Json, false, service).Should().Be(OutputMode.Json);
        OutputModeResolver.ApplyTerminalSafetyFallback(OutputMode.Json, service, out bool jsonDowngraded).Should().Be(OutputMode.Json);
        jsonDowngraded.Should().BeFalse();
        OutputModeResolver.Resolve(null, true, service).Should().Be(OutputMode.Plain);
    }

    private static FunctionTemplateInfo CreateTemplate(string id)
        => new(id, "node", EngineIds.V2, id, null, null, ["javascript"], new TemplateMetadata([], false, null));
}