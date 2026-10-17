// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Fact]
    public async Task StagedFullEntry_ActualAtomicConfigurationCommitThenCancel_RemainsCompletedAcrossEntry()
    {
        using var installed = await InstallAsync("Java");
        string target = Path.Combine(_root, "entry-configuration-commit");
        string config = Path.Combine(target, ".func", "config.json");
        using CancellationTokenSource cancellation = new();
        CancelAfterConfigurationCommit files = new(config, cancellation);
        TestInteractionService interaction = new();

        int exit = await new NativeCliEntryPoint().ExecuteAsync(
            ["init", target, "-s", "java", "-l", "Java", "--no-bundles"], interaction,
            new(Path.Combine(_root, "workloads")), new(Home), cancellation.Token, services =>
            {
                _contexts.AddRange(services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>()
                    .Select(workload => workload.LoadContext));
                services.AddSingleton<IFileSystem>(files);
            }, _ => Task.FromResult<string?>(null));

        exit.Should().Be(0, interaction.AllOutput);
        cancellation.IsCancellationRequested.Should().BeTrue();
        files.Committed.Should().BeTrue();
        File.Exists(config).Should().BeTrue();
        File.Exists(Path.Combine(target, "pom.xml")).Should().BeTrue();
        interaction.AllOutput.Should().Contain("Initialized project template").And.NotContain("unexpected error");
    }

    private sealed class CancelAfterConfigurationCommit(string configurationPath, CancellationTokenSource cancellation)
        : PhysicalFileSystem, IFileSystem
    {
        public bool Committed { get; private set; }

        public new async Task CreateAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            await base.CreateAllTextAsync(path, contents, cancellationToken);
            if (path == configurationPath)
            {
                Committed = true;
                cancellation.Cancel();
            }
        }
    }
}
