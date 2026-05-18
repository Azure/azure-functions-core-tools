// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();
    private readonly FunctionPalette _palette = new();
    private readonly IStartInitializationRunner _initializationRunner = Substitute.For<IStartInitializationRunner>();
    private readonly ICliVersionProvider _cliVersionProvider = Substitute.For<ICliVersionProvider>();

    public StartCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cliVersionProvider.Version.Returns("5.0.0-test");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void StartCommand_HasExpectedOptions()
    {
        var cmd = new StartCommand(_interaction, _palette, _cliVersionProvider, _initializationRunner);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--port", optionNames);
        Assert.Contains("--cors", optionNames);
        Assert.Contains("--cors-credentials", optionNames);
        Assert.Contains("--functions", optionNames);
        Assert.Contains("--no-build", optionNames);
        Assert.Contains("--enable-auth", optionNames);
        Assert.Contains("--host-version", optionNames);
        Assert.Contains("--output", optionNames);
        Assert.Contains("--no-tui", optionNames);
        Assert.Contains("--log-file", optionNames);
    }

    [Fact]
    public void StartCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("start", names);
    }

    [Fact]
    public async Task StartCommand_NonExistentPath_ThrowsGracefulException()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"start \"{nonExistent}\"");

        // Disable the default exception handler so GracefulException propagates,
        // matching production wiring.
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = await Assert.ThrowsAsync<GracefulException>(() => result.InvokeAsync(config));
        Assert.Contains("does not exist", ex.Message);
        Assert.Contains(nonExistent, ex.Message);
    }

    [Fact]
    public async Task StartCommand_InvalidOutputMode_ThrowsGracefulException()
    {
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"start \"{_tempDir}\" --output=bogus");

        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = await Assert.ThrowsAsync<GracefulException>(() => result.InvokeAsync(config));
        Assert.Contains("--output", ex.Message);
        Assert.Contains("bogus", ex.Message);
    }

    [Fact]
    public async Task StartCommand_RunsInitializationBeforeDashboardPipeline()
    {
        var source = new InMemoryHostEventStream();
        source.Complete();
        var initializationResult = new StartInitializationResult(
            new DashboardRunInfo(CliVersion: "5.0.0-test", ProfileName: "none", StackName: ".NET"),
            source,
            HostVersion: "4.834.0",
            BundleRequired: false,
            BundleVersion: null);
        _initializationRunner.RunAsync(
                Arg.Any<StartInitializationContext>(),
                Arg.Any<IStartInitializationRenderer>(),
                Arg.Any<CancellationToken>())
            .Returns(initializationResult);

        IServiceProvider services = TestParser.BuildServiceProviderWith(_interaction, services =>
        {
            services.AddSingleton(_cliVersionProvider);
            services.AddSingleton(_initializationRunner);
        });
        var root = Parser.CreateCommand(services);
        var result = root.Parse($"start \"{_tempDir}\" --output=plain --host-version 4.900.0 --no-build --enable-auth --port 9090 --functions HttpTrigger --cors http://localhost,http://example --cors-credentials");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        Assert.Equal(0, exitCode);
        await _initializationRunner.Received(1).RunAsync(
            Arg.Is<StartInitializationContext>(context =>
                context.Options.WorkingDirectory.Info.FullName == new DirectoryInfo(_tempDir).FullName
                && context.ProfileName == "none"
                && context.CliVersion == "5.0.0-test"
                && context.Options.OutputMode == OutputMode.Plain
                && context.Options.RequestedHostVersion == "4.900.0"
                && context.Options.NoBuild
                && context.Options.EnableAuth
                && context.Options.Port == 9090
                && context.Options.Functions.SequenceEqual(new[] { "HttpTrigger" })
                && context.Options.Cors.SequenceEqual(new[] { "http://localhost", "http://example" })
                && context.Options.CorsCredentials),
            Arg.Any<IStartInitializationRenderer>(),
            Arg.Any<CancellationToken>());
    }
}
