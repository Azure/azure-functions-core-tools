// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

[Collection(nameof(WorkingDirectoryTests))]
public class StartCommandTests : IDisposable
{
    private static readonly string _safeDir = Path.GetTempPath();
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();
    private readonly CaptureHostRunner _hostRunner = new();

    public StartCommandTests()
    {
        Directory.SetCurrentDirectory(_safeDir);
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.SetCurrentDirectory(_safeDir); } catch { }
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void StartCommand_HasExpectedOptions()
    {
        var cmd = new StartCommand(_interaction, _hostRunner);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--port", optionNames);
        Assert.Contains("--cors", optionNames);
        Assert.Contains("--cors-credentials", optionNames);
        Assert.Contains("--functions", optionNames);
        Assert.Contains("--no-build", optionNames);
        Assert.Contains("--enable-auth", optionNames);
        Assert.Contains("--host-version", optionNames);
    }

    [Fact]
    public async Task StartCommand_NoHostJson_ReturnsError()
    {
        var cmd = new StartCommand(_interaction, _hostRunner);
        var parseResult = cmd.Parse([_tempDir]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains(_interaction.Lines, l => l.Contains("host.json"));
        Assert.Null(_hostRunner.LastConfig);
    }

    [Fact]
    public async Task StartCommand_WithHostJson_LaunchesHost()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        var cmd = new StartCommand(_interaction, _hostRunner);
        var parseResult = cmd.Parse([_tempDir]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.NotNull(_hostRunner.LastConfig);
        Assert.Equal(HostConfiguration.DefaultPort, _hostRunner.LastConfig!.Port);
    }

    [Fact]
    public async Task StartCommand_WithCustomPort_PassesPortToHost()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        var cmd = new StartCommand(_interaction, _hostRunner);
        var parseResult = cmd.Parse([_tempDir, "--port", "9090"]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(9090, _hostRunner.LastConfig!.Port);
    }

    [Fact]
    public async Task StartCommand_WithFunctionsFilter_PassesFilterToHost()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        var cmd = new StartCommand(_interaction, _hostRunner);
        var parseResult = cmd.Parse([_tempDir, "--functions", "Func1", "Func2"]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "Func1", "Func2" }, _hostRunner.LastConfig!.FunctionsFilter!);
    }

    [Fact]
    public void StartCommand_RegisteredInParser()
    {
        var root = Parser.CreateCommand(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("start", names);
    }

    /// <summary>
    /// Test double that captures the configuration passed to Start().
    /// </summary>
    private class CaptureHostRunner : IHostRunner
    {
        public HostConfiguration? LastConfig { get; private set; }

        public int Start(HostConfiguration config, CancellationToken cancellationToken = default)
        {
            LastConfig = config;
            return 0;
        }
    }
}
