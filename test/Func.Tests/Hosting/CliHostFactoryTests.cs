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
/// production uses, point it at a temp <c>Workloads:Home</c>, and assert the
/// loaded workloads contributed (or failed to contribute) commands as
/// expected. Each test sets up its own temp directory with the on-disk
/// layout the loader expects:
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

    /// <summary>
    /// Mirrors the production boot sequence in Program.cs: build, point at
    /// the temp home, register workloads, build, start.
    /// </summary>
    private async Task<IHost> StartHostAsync(TestInteractionService interaction)
    {
        HostApplicationBuilder builder = CliHostFactory.CreateBuilder(interaction);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workloads:Home"] = _home,
        });

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
    {
        var registry = new
        {
            Schema = WorkloadManifestSchema.CurrentRegistrySchema,
            Workloads = entries.Select(e => new
            {
                PackageId = e.PackageId,
                PackageVersion = e.Version,
                Aliases = Array.Empty<string>(),
                EntryPoint = new
                {
                    AssemblyPath = FixtureAssembly,
                    Type = e.TypeName,
                },
            }).ToArray(),
        };

        Directory.CreateDirectory(_home);
        var json = JsonSerializer.Serialize(registry, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        // Match the on-disk shape WorkloadRegistry uses: $schema property name.
        json = json.Replace("\"schema\":", "\"$schema\":", StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(_home, WorkloadPathsOptions.WorkloadRegistryFileName), json);
    }
}
