// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.AppStacks;
using Azure.Functions.Cli.Hosting.Dashboard.Demo;
using NSubstitute;
using Spectre.Console;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartInitializationTests : IDisposable
{
    private readonly string _tempDir;

    public StartInitializationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DemoRunner_SimulatesHostInstallAndSkipsBundle_ForDotNet()
    {
        IAppStackProvider stackProvider = Substitute.For<IAppStackProvider>();
        stackProvider.GetStackNameAsync(Arg.Any<WorkingDirectory>(), Arg.Any<CancellationToken>())
            .Returns(".NET");
        var renderer = new RecordingStartInitializationRenderer();
        var runner = new DemoStartInitializationRunner(stackProvider);
        var context = new StartInitializationContext(
            WorkingDirectory.FromExplicit(_tempDir),
            CliVersion: "5.0.0-test",
            ProfileName: "none",
            RequestedHostVersion: null,
            DemoFunctionCount: 12,
            DemoSpeedMultiplier: 0.001,
            DemoAutoExit: true);

        StartInitializationResult result = await runner.RunAsync(context, renderer, CancellationToken.None);

        Assert.Equal(".NET", result.RunInfo.StackName);
        Assert.Equal("4.834.0", result.HostVersion);
        Assert.False(result.BundleRequired);
        Assert.IsType<DemoEventSource>(result.EventStream);
        Assert.Contains(renderer.Events, static ev =>
            ev is StartInitializationStepStartedEvent
            {
                Step:
                {
                    Kind: StartInitializationStepKind.InstallHostWorkload,
                    DisplayKind: StartInitializationDisplayKind.Progress,
                },
            });
        Assert.Contains(renderer.Events, static ev =>
            ev is StartInitializationProgressEvent
            {
                StepKind: StartInitializationStepKind.InstallHostWorkload,
                Percent: 100,
            });
    }

    [Fact]
    public async Task JsonRenderer_EmitsInitializationRecords()
    {
        using var stream = new MemoryStream();
        var renderer = new JsonStartInitializationRenderer(stream, ownsStream: false);

        await renderer.OnEventAsync(
            new StartInitializationStartedEvent(DateTimeOffset.UnixEpoch, "none"),
            CancellationToken.None);
        await renderer.OnEventAsync(
            new StartInitializationProgressEvent(DateTimeOffset.UnixEpoch, StartInitializationStepKind.InstallHostWorkload, 50, "Downloading"),
            CancellationToken.None);
        await renderer.DisposeAsync();

        string[] lines = Encoding.UTF8.GetString(stream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        using var started = JsonDocument.Parse(lines[0]);
        Assert.Equal("start_initialization_started", started.RootElement.GetProperty("kind").GetString());
        Assert.Equal("none", started.RootElement.GetProperty("profile").GetString());
        using var progress = JsonDocument.Parse(lines[1]);
        Assert.Equal("start_initialization_progress", progress.RootElement.GetProperty("kind").GetString());
        Assert.Equal("install_host_workload", progress.RootElement.GetProperty("step").GetString());
        Assert.Equal(50, progress.RootElement.GetProperty("percent").GetDouble());
    }

    [Fact]
    public void CompactRenderer_ProgressMarkup_DoesNotTreatBarAsStyle()
    {
        var renderer = new CompactStartInitializationRenderer(new TestInteractionService());
        MethodInfo method = typeof(CompactStartInitializationRenderer).GetMethod("FormatProgress", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("FormatProgress method was not found.");
        string markup = (string)method.Invoke(renderer, [0d, "Preparing download"])!;
        using var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });

        console.Write(new Markup(markup));

        Assert.Contains("[------------------]", writer.ToString());
    }

    private sealed class RecordingStartInitializationRenderer : IStartInitializationRenderer
    {
        public List<StartInitializationEvent> Events { get; } = [];

        public Task OnEventAsync(StartInitializationEvent initializationEvent, CancellationToken cancellationToken)
        {
            Events.Add(initializationEvent);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
