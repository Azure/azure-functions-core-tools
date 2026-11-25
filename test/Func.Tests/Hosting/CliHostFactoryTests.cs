// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
    private const string StackWorkloadType = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.StackWorkload";
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
    public void CreateBuilder_RegistersPlatform()
    {
        var interaction = new TestInteractionService();
        HostApplicationBuilder builder = CliHostFactory.CreateBuilder(interaction);
        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IPlatform>().Should().NotBeNull();
    }

    [Fact]
    public void CreateBuilder_RegistersInstalledProjectStackCatalog()
    {
        var interaction = new TestInteractionService();
        HostApplicationBuilder builder = CliHostFactory.CreateBuilder(interaction);
        using ServiceProvider provider = builder.Services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<InstalledProjectStackCatalog>();

        catalog.Stacks.Should().BeEmpty();
        provider.GetRequiredService<InstalledProjectStackCatalog>().Should().BeSameAs(catalog);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateHostAsync_LoadsCommandWithoutRequiringAProjectStack(bool declaresStack)
    {
        await StageWorkloadAsync("withcommand.fixture", CommandWorkloadType, declaresStack ? new("comet", "native", "Comet") : null, TestContext.Current.CancellationToken);

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);
        var rootCommand = Parser.CreateCommand(host.Services);

        rootCommand.Subcommands.Should().Contain(c => string.Equals(c.Name, "hello-from-workload", StringComparison.Ordinal));
        host.Services.GetRequiredService<InstalledProjectStackCatalog>().Stacks.Should().BeEmpty();
        interaction.Lines.Should().NotContain(l => l.StartsWith("WARNING:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterWorkloads_ProvidesOnlyTheOwningInstalledDescriptorWithoutAFeed()
    {
        var paths = new WorkloadPathsOptions(_home);
        var token = TestContext.Current.CancellationToken;
        await StageWorkloadAsync("Contoso.DevKit", StackWorkloadType, new("comet", "native", "Comet v1"), token);
        await StageWorkloadAsync("Other.Publisher", StackWorkloadType, new("lunar", "lunar", "Lunar"), token);
        await StageWorkloadAsync("Tool.WithoutStack", CommandWorkloadType, null, token);
        var services = new ServiceCollection();
        services.AddSingleton<InstalledProjectStackCatalog>();

        await WorkloadRegistration.RegisterWorkloadsAsync(services, paths, new TestInteractionService(), token);

        using var provider = services.BuildServiceProvider();
        var stacks = provider.GetServices<IProjectStack>().ToDictionary(s => s.Descriptor.Identifier);
        var workloads = provider.GetServices<WorkloadInfo>().ToDictionary(w => w.PackageId);
        stacks["comet"].Descriptor.Should().BeSameAs(workloads["Contoso.DevKit"].Stack);
        stacks["lunar"].Descriptor.Should().BeSameAs(workloads["Other.Publisher"].Stack);
        provider.GetRequiredService<InstalledProjectStackCatalog>().Stacks.Should().BeEquivalentTo(stacks.Values);
        var command = provider.GetServices<FuncCliCommand>().OfType<ExternalCommand>().Should().ContainSingle().Subject;
        command.Workload.PackageId.Should().Be("Tool.WithoutStack");
        command.Workload.DisplayName.Should().Be("Package title");
        provider.GetServices<StackDescriptor>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "native", "Comet", "Fixture stack", "does not declare a stack")]
    [InlineData("lunar", "native", "Comet", "Fixture stack", "Identifier")]
    [InlineData("comet", "other-host", "Comet", "Fixture stack", "WorkerRuntime")]
    [InlineData("comet", "native", "Renamed", "Fixture stack", "DisplayName")]
    [InlineData("COMET", "native", "Comet", "Fixture stack", "Identifier")]
    [InlineData("comet", "NATIVE", "Comet", "Fixture stack", "WorkerRuntime")]
    [InlineData("comet", "native", "COMET", "Fixture stack", "DisplayName")]
    [InlineData("comet", "native", "Comet", "Fixture Stack", "Description")]
    [InlineData("comet", "native", "Comet", "Fixture stack", null)]
    public async Task Catalog_ChecksTypeRegistrationAgainstItsPackage(string? id, string runtime, string label, string description, string? expectedError)
    {
        const string type = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.FixedStackWorkload";
        await StageWorkloadAsync("Contoso.DevKit", type, id is null ? null : new(id, runtime, label, description), TestContext.Current.CancellationToken);
        using IHost host = await StartHostAsync(new TestInteractionService());
        var catalog = host.Services.GetRequiredService<InstalledProjectStackCatalog>();

        if (expectedError is null)
        {
            catalog.FindStack("comet").Should().NotBeNull();
        }
        else
        {
            FluentActions.Invoking(() => catalog.FindStack("comet")).Should().Throw<ProjectStackResolutionException>().WithMessage($"*Contoso.DevKit@1.0.0*{expectedError}*");
        }
    }

    [Fact]
    public async Task Catalog_RejectsStacksRegisteredByEachOthersPackage()
    {
        const string type = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.SwappedStackWorkload";
        var token = TestContext.Current.CancellationToken;
        await StageWorkloadAsync("Contoso.Comet", type, new("comet", "native", "Comet"), token);
        await StageWorkloadAsync("Contoso.Lunar", type, new("lunar", "lunar", "Lunar"), token);
        using IHost host = await StartHostAsync(new TestInteractionService());
        var catalog = host.Services.GetRequiredService<InstalledProjectStackCatalog>();

        FluentActions.Invoking(() => catalog.Stacks).Should().Throw<ProjectStackResolutionException>().WithMessage("*Contoso.*@1.0.0*");
    }

    [Fact]
    public async Task CreateHostAsync_StackConstructorWithoutDeclaredStack_ReportsPackageAndSkipsActivation()
    {
        StageFixtureWorkload("Contoso.DevKit", "1.0.0", StackWorkloadType);
        WriteRegistry(("Contoso.DevKit", "1.0.0", StackWorkloadType));
        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);

        interaction.Lines.Should().Contain(line => line.Contains("Contoso.DevKit@1.0.0")
            && line.Contains("requires a StackDescriptor") && line.Contains("Declare StackIdentifier"));
        host.Services.GetServices<IProjectStack>().Should().BeEmpty();
        host.Services.GetRequiredService<IWorkloadProvider>().GetWorkloads().Should().BeEmpty();
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
        provider.GetRuntimeWorkloads().Should().ContainSingle();
        provider.GetContentWorkloads().Count.Should().Be(2);
        provider.GetWorkloads().Count.Should().Be(3);
        provider.GetContentWorkloads().Should().AllSatisfy(w => w.DisplayName.Should().Be(w.PackageId));
        provider.GetContentWorkloads().Should().AllSatisfy(w =>
        {
            string packageRoot = Path.Combine(_home, "workloads", w.PackageId, w.PackageVersion);
            w.InstallDirectory.Should().Be(packageRoot);
            w.ContentRoot.Should().Be(Path.Combine(packageRoot, "tools", "any"));
        });
    }

    [Fact]
    public async Task CreateHostAsync_IncompatibleRidWorkload_IsSkippedWithUpdateGuidance()
    {
        StageFixtureWorkload("withcommand.fixture.incompatible-rid", "1.0.0", CommandWorkloadType);
        WriteRegistryWithRuntimeIdentifier(
            "withcommand.fixture.incompatible-rid",
            "1.0.0",
            CommandWorkloadType,
            "incompatible-rid");

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);
        var rootCommand = Parser.CreateCommand(host.Services);

        rootCommand.Subcommands.Should().NotContain(c => string.Equals(c.Name, "hello-from-workload", StringComparison.Ordinal));
        interaction.Lines.Should().Contain(line => line.StartsWith("WARNING:", StringComparison.Ordinal)
            && line.Contains("func workload update", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("packageId")]
    [InlineData("packageVersion")]
    public async Task CreateHostAsync_IncompatibleRidWorkloadWithNullIdentityField_IsSkippedWithWarning(string nullField)
    {
        string packageId = nullField == "packageId" ? "null" : "\"incompatible.fixture\"";
        string packageVersion = nullField == "packageVersion" ? "null" : "\"1.0.0\"";
        Directory.CreateDirectory(_home);
        File.WriteAllText(
            Path.Combine(_home, WorkloadPathsOptions.WorkloadRegistryFileName),
            $$"""
            {
              "$schema": "{{WorkloadManifestSchema.RegistryV1Schema}}",
              "workloads": [
                {
                  "packageId": {{packageId}},
                  "packageVersion": {{packageVersion}},
                  "runtimeIdentifier": "incompatible-{{Guid.NewGuid():N}}"
                }
              ]
            }
            """);

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);
        var rootCommand = Parser.CreateCommand(host.Services);

        rootCommand.Subcommands.Should().Contain(c => string.Equals(c.Name, "version", StringComparison.OrdinalIgnoreCase));
        interaction.Lines.Should().Contain(line => line.StartsWith("WARNING:", StringComparison.Ordinal)
            && line.Contains("targets runtime identifier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateHostAsync_WhenWorkloadConfigureThrows_WarnsAndContinues()
    {
        StageFixtureWorkload("throwing.fixture", "1.0.0", ThrowingWorkloadType);
        WriteRegistry(("throwing.fixture", "1.0.0", ThrowingWorkloadType));

        var interaction = new TestInteractionService();

        using IHost host = await StartHostAsync(interaction);
        var rootCommand = Parser.CreateCommand(host.Services);

        interaction.Lines.Should().Contain(l => l.StartsWith("WARNING:", StringComparison.Ordinal)
                 && l.Contains("throwing.fixture@1.0.0", StringComparison.Ordinal)
                 && l.Contains("Boom from ThrowingWorkload", StringComparison.Ordinal));

        // Built-ins must still be there: a single misbehaving workload cannot brick the CLI.
        rootCommand.Subcommands.Should().Contain(c => string.Equals(c.Name, "version", StringComparison.OrdinalIgnoreCase));
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
        workloads.Should().BeEmpty();
        rootCommand.Subcommands.Should().NotContain(c => string.Equals(c.Name, "hello-from-workload", StringComparison.Ordinal));
        interaction.Lines.Should().NotContain(l => l.StartsWith("WARNING:", StringComparison.Ordinal));
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

            await builder.RegisterWorkloadsAsync(cancellationToken: TestContext.Current.CancellationToken);
            using IHost host = builder.Build();
            await host.StartAsync(TestContext.Current.CancellationToken);

            var rootCommand = Parser.CreateCommand(host.Services);

            // The registered WorkloadPathsOptions wins: the staged workload's command is present.
            rootCommand.Subcommands.Should().Contain(c => string.Equals(c.Name, "hello-from-workload", StringComparison.Ordinal));

            // And the bound IWorkloadPaths reflects the override, not IConfiguration.
            var paths = host.Services.GetRequiredService<IWorkloadPaths>();
            paths.Home.Should().Be(Path.GetFullPath(_home));
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
        var installDir = Path.Combine(new WorkloadPathsOptions(_home).GetInstallDirectory(packageId, version), "tools", "any");
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

    private async Task StageWorkloadAsync(string id, string type, StackDescriptor? stack, CancellationToken cancellationToken)
    {
        StageFixtureWorkload(id, "1.0.0", type);
        var store = new WorkloadStore(new WorkloadPathsOptions(_home));
        await store.SaveWorkloadAsync(new WorkloadEntry
        {
            PackageId = id,
            PackageVersion = "1.0.0",
            DisplayName = "Package title",
            Description = "Package description",
            Stack = stack,
            EntryPoint = new() { AssemblyPath = FixtureAssembly, Type = type },
        }, cancellationToken);
    }

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

    private void WriteRegistryWithRuntimeIdentifier(string packageId, string version, string typeName, string runtimeIdentifier)
    {
        WorkloadRegistry registry = new()
        {
            Workloads =
            [
                new WorkloadEntry
                {
                    PackageId = packageId,
                    PackageVersion = version,
                    RuntimeIdentifier = runtimeIdentifier,
                    Aliases = [],
                    Kind = WorkloadKind.Workload,
                    EntryPoint = new EntryPointSpec
                    {
                        AssemblyPath = FixtureAssembly,
                        Type = typeName,
                    },
                },
            ],
        };

        Directory.CreateDirectory(_home);
        string json = JsonSerializer.Serialize(registry, WorkloadJsonContext.Default.WorkloadRegistry);
        File.WriteAllText(Path.Combine(_home, WorkloadPathsOptions.WorkloadRegistryFileName), json);
    }
}
