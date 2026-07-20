// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public sealed class SetupCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();

    public SetupCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-setup-command-{Guid.NewGuid():N}");
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
    public void SetupCommand_HasExpectedOptions()
    {
        var cmd = new SetupCommand(Substitute.For<ISetupRunner>(), Microsoft.Extensions.Options.Options.Create(new Azure.Functions.Cli.Workloads.Catalog.WorkloadCatalogOptions()));
        IReadOnlyList<string> optionNames = cmd.Options.Select(o => o.Name).ToArray();

        optionNames.Should().Contain("--features");
        optionNames.Should().Contain("--profile");
        optionNames.Should().Contain("--profiles");
        optionNames.Should().Contain("--install-policy");
        optionNames.Should().Contain("--source");
        optionNames.Should().Contain("--prerelease");
        optionNames.Should().Contain("--non-interactive");
        optionNames.Should().Contain("--yes");
        optionNames.Should().Contain("--check");
        optionNames.Should().Contain("--output");
    }

    [Fact]
    public void SetupCommand_FeaturesHelp_UsesDotnetFeatureName()
    {
        var cmd = new SetupCommand(Substitute.For<ISetupRunner>(), Microsoft.Extensions.Options.Options.Create(new Azure.Functions.Cli.Workloads.Catalog.WorkloadCatalogOptions()));
        string description = cmd.FeaturesOption.Description ?? string.Empty;

        description.Should().Contain("dotnet");
        description.Should().NotContain("dotnet-isolated");
    }

    [Fact]
    public void SetupCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);

        root.Subcommands.Should().Contain(command => command.Name == "setup");
    }

    [Fact]
    public async Task SetupCommand_InvokesRunnerWithParsedOptions()
    {
        ISetupRunner setupRunner = Substitute.For<ISetupRunner>();
        setupRunner.RunAsync(Arg.Any<SetupCommandOptions>(), Arg.Any<CancellationToken>())
            .Returns(new SetupRunResult(0));

        IServiceProvider services = TestParser.BuildServiceProviderWith(_interaction, serviceCollection =>
        {
            serviceCollection.AddSingleton(setupRunner);
        });
        FuncRootCommand root = Parser.CreateCommand(services);
        ParseResult result = root.Parse(
            $"setup \"{_tempDir}\" --features node,python --profile flex --profile premium --profiles staging,prod "
            + "--source https://example.test/v3/index.json --install-policy if-needed --prerelease --non-interactive --yes --check --output json");

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(0);
        await setupRunner.Received(1).RunAsync(
            Arg.Is<SetupCommandOptions>(options =>
                options.WorkingDirectory.FullName == new DirectoryInfo(_tempDir).FullName
                && options.Features.SequenceEqual(new[] { "node", "python" })
                && options.ProfileNames.SequenceEqual(new[] { "flex", "premium", "staging", "prod" })
                && options.Source == "https://example.test/v3/index.json"
                && options.InstallPolicy == SetupInstallPolicy.IfNeeded
                && options.IncludePrerelease
                && options.NonInteractive
                && options.AssumeYes
                && options.Check
                && options.OutputMode == SetupOutputMode.Json),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetupCommand_InvalidInstallPolicy_ThrowsGracefulException()
    {
        var root = TestParser.CreateRoot(_interaction);
        ParseResult result = root.Parse($"setup \"{_tempDir}\" --install-policy always");

        GracefulException ex = (await FluentActions.Awaiting(() => result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false })).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("--install-policy");
        ex.Message.Should().Contain("always");
    }
}
