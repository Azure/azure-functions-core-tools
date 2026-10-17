// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Tests.Console;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeStagedEntryNoAcquisitionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "staged-entry-no-acquisition-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FreshStagedCohort_MissingInstalledStack_RefusesWithoutInteractiveSetupOrLiveFiles()
    {
        using BufferedConsole stdout = new(true, true, noColor: true);
        using BufferedConsole stderr = new(true, true, noColor: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), stdout, stderr);
        var setup = Substitute.For<ISetupRunner>();
        string target = Path.Combine(_root, "target");

        int exit = await new NativeCliEntryPoint().ExecuteAsync(["init", target, "-s", "node", "-l", "js"],
            interaction, new(Path.Combine(_root, "workloads")), new(Path.Combine(_root, "home")), CancellationToken.None,
            services => services.AddSingleton(setup), _ => Task.FromResult<string?>(null));

        exit.Should().Be(1);
        stderr.Reads.Should().Be(0);
        setup.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(target).Should().BeFalse();
        stdout.Output.Should().NotContain("Run `func setup`").And.NotContain("installing");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
