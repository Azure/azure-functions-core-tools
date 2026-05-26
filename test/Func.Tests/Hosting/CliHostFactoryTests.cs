// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

/// <summary>
/// Integration tests for the host-startup wiring: build the same host
/// production uses, register a <see cref="WorkloadPathsOptions"/> pointing
/// at a per-test temp directory, and assert the loaded workloads contributed
/// (or failed to contribute) commands as expected. The on-disk layout the
/// loader expects is
/// <c>&lt;Home&gt;/workloads.json</c> + <c>&lt;Home&gt;/workloads/&lt;pkg&gt;/&lt;ver&gt;/tools/any/&lt;asm&gt;.dll</c>.
/// </summary>
public sealed class CliHostFactoryTests : IDisposable
{
    private const string FixtureAssembly = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.dll";
    private const string CommandWorkloadType = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.StubWorkload";
    private const string ThrowingWorkloadType = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.ThrowingWorkload";

    private readonly string _home = Path.Combine(Path.GetTempPath(), "func-cli-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            try
            {
                Directory.Delete(_home, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; the OS will reap %TEMP% eventually.
            }
        }
    }

    [Fact]
    public async Task CreateHostAsync_LoadsInstalledWorkloads_AndAddsTheirCommandsToRoot()
    {
        StageFixtureWorkload("withcommand.fixture", "1.0.0", CommandWorkloadType);
        WriteRegistry(("withcommand.fixture", "1.0.0", CommandWorkloadType));

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);
        var rootCommand = Parser.CreateCommand(host.Services);

        Assert.Contains(rootCommand.Subcommands, c => string.Equals(c.Name, "hello-from-workload", StringComparison.Ordinal));
        Assert.DoesNotContain(interaction.Lines, l => l.StartsWith("WARNING:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateHostAsync_ContentWorkloads_AreIncludedInProviderInventory()
    {
        StageFixtureWorkload("withcommand.fixture", "1.0.0", CommandWorkloadType);
        WriteRegistry(
            ("withcommand.fixture", "1.0.0", CommandWorkloadType, WorkloadKind.Workload),
            ("host.content", "4.0.0", null, WorkloadKind.Content),
            ("host.content", "4.1.0", null, WorkloadKind.Content));

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);

        IWorkloadProvider provider = host.Services.GetRequiredService<IWorkloadProvider>();
        Assert.Single(provider.GetRuntimeWorkloads());
        Assert.Equal(2, provider.GetContentWorkloads().Count);
        Assert.Equal(3, provider.GetWorkloads().Count);
        Assert.All(provider.GetContentWorkloads(), w => Assert.Equal(w.PackageId, w.DisplayName));
    }

    [Fact]
    public async Task CreateHostAsync_WhenWorkloadConfigureThrows_WarnsAndContinues()
    {
        StageFixtureWorkload("throwing.fixture", "1.0.0", ThrowingWorkloadType);
        WriteRegistry(("throwing.fixture", "1.0.0", ThrowingWorkloadType));

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);
        var rootCommand = Parser.CreateCommand(host.Services);

        Assert.Contains(
            interaction.Lines,
            l => l.StartsWith("WARNING:", StringComparison.Ordinal)
                 && l.Contains("throwing.fixture@1.0.0", StringComparison.Ordinal)
                 && l.Contains("Boom from ThrowingWorkload", StringComparison.Ordinal));

        // Built-ins must still be there: a single misbehaving workload cannot brick the CLI.
        Assert.Contains(rootCommand.Subcommands, c => string.Equals(c.Name, "version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateHostAsync_WithEmptyHome_LoadsHostWithoutWorkloadCommands()
    {
        Directory.CreateDirectory(_home);
        // No workloads.json written: WorkloadStore returns an empty list.

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);
        var rootCommand = Parser.CreateCommand(host.Services);

        var workloads = host.Services.GetRequiredService<IWorkloadProvider>().GetWorkloads();
        Assert.Empty(workloads);
        Assert.DoesNotContain(rootCommand.Subcommands, c => string.Equals(c.Name, "hello-from-workload", StringComparison.Ordinal));
        Assert.DoesNotContain(interaction.Lines, l => l.StartsWith("WARNING:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateHostAsync_IgnoresWorkloadsHomeFromIConfiguration()
    {
        // Stage a workload at the override home so it should load.
        StageFixtureWorkload("withcommand.fixture", "1.0.0", CommandWorkloadType);
        WriteRegistry(("withcommand.fixture", "1.0.0", CommandWorkloadType));

        // Point IConfiguration at a *different* directory. If a future
        // change ever rewires WorkloadPathsOptions to honour IConfiguration,
        // the loader would look here instead and the workload command would
        // be missing.
        var configHome = Path.Combine(Path.GetTempPath(), "func-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configHome);

        try
        {
            var interaction = new TestInteractionService();
            HostApplicationBuilder builder = CreateBuilderWithHome(interaction, _home);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workloads:Home"] = configHome,
            });

            await builder.RegisterWorkloadsAsync();
            using IHost host = builder.Build();
            await host.StartAsync();

            var rootCommand = Parser.CreateCommand(host.Services);

            // The registered WorkloadPathsOptions wins: the staged workload's command is present.
            Assert.Contains(rootCommand.Subcommands, c => string.Equals(c.Name, "hello-from-workload", StringComparison.Ordinal));

            // And the bound IWorkloadPaths reflects the override, not IConfiguration.
            var paths = host.Services.GetRequiredService<IWorkloadPaths>();
            Assert.Equal(Path.GetFullPath(_home), paths.Home);
        }
        finally
        {
            if (Directory.Exists(configHome))
            {
                try
                {
                    Directory.Delete(configHome, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    /// <summary>
    /// Builds a host with a pre-registered <see cref="WorkloadPathsOptions"/>
    /// pointing at <paramref name="home"/>, so tests can redirect the
    /// workload root without mutating the real process environment (which
    /// would leak across parallel xUnit runs).
    /// </summary>
    private static HostApplicationBuilder CreateBuilderWithHome(TestInteractionService interaction, string home)
    {
        HostApplicationBuilder builder = CliHostFactory.CreateBuilder(interaction);

        // RegisterWorkloadsAsync's descriptor scan picks up this
        // ImplementationInstance and skips constructing a default
        // WorkloadPathsOptions (which would read the real env var).
        builder.Services.AddSingleton(new WorkloadPathsOptions(home));
        return builder;
    }

    /// <summary>
    /// Mirrors the production boot sequence in Program.cs: build, register
    /// workloads (which reads the substituted workload home), build, start.
    /// </summary>
    private async Task<IHost> StartHostAsync(TestInteractionService interaction)
    {
        HostApplicationBuilder builder = CreateBuilderWithHome(interaction, _home);

        await builder.RegisterWorkloadsAsync();
        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    /// <summary>
    /// Lays out one workload's runtime payload at the path the loader expects:
    /// <c>&lt;Home&gt;/workloads/&lt;pkg&gt;/&lt;ver&gt;/tools/any/</c>, copying the
    /// fixture's DLL/deps/runtimeconfig from where the test build's
    /// <c>LayoutFixtureWorkloadsForTests</c> target staged them.
    /// </summary>
    private void StageFixtureWorkload(string packageId, string version, string typeName)
    {
        // typeName is unused at the filesystem layer; it lives in the registry entry
        // (see WriteRegistry). It's a method parameter purely for callsite readability.
        _ = typeName;
        var installDir = Path.Combine(_home, "workloads", packageId, version, "tools", "any");
        Directory.CreateDirectory(installDir);

        var stagedFixtureRoot = Path.Combine(AppContext.BaseDirectory, "tools", "any");
        foreach (var file in Directory.EnumerateFiles(stagedFixtureRoot))
        {
            var dest = Path.Combine(installDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
    }

    private void WriteRegistry(params (string PackageId, string Version, string TypeName)[] entries)
        => WriteRegistry([.. entries.Select(e => (
            e.PackageId,
            e.Version,
            TypeName: (string?)e.TypeName,
            Kind: WorkloadKind.Workload))]);

    private void WriteRegistry(params (string PackageId, string Version, string? TypeName, WorkloadKind Kind)[] entries)
    {
        WorkloadRegistry registry = new()
        {
            Workloads = [.. entries.Select(e => new WorkloadEntry
            {
                PackageId = e.PackageId,
                PackageVersion = e.Version,
                Aliases = [],
                Kind = e.Kind,
                EntryPoint = e.TypeName is null
                    ? null
                    : new EntryPointSpec
                {
                    AssemblyPath = FixtureAssembly,
                    Type = e.TypeName,
                },
            })],
        };

        Directory.CreateDirectory(_home);
        string json = JsonSerializer.Serialize(registry, WorkloadJsonContext.Default.WorkloadRegistry);
        File.WriteAllText(Path.Combine(_home, WorkloadPathsOptions.WorkloadRegistryFileName), json);
    }
}
