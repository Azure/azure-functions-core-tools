// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeTemplateBootCaptureTests : IDisposable
{
    private const string Fixture = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.dll";
    private const string Namespace = "Azure.Functions.Cli.Workloads.Tests.Fixtures.WithCommand.";
    private readonly string _home = Path.Combine(Path.GetTempPath(), "func-native-capture-" + Guid.NewGuid().ToString("N"));
    private readonly List<ServiceProvider> _providers = [];

    [Fact]
    public async Task RegisterWorkloadsAsync_ActualSelectionAndConfiguration_CapturesOnlySelectedRuntimeOutcomes()
    {
        WorkloadEntry[] entries =
        [
            Entry("Physical", "1.0.0", "StubWorkload"),
            Entry("Physical", "2.0.0", "ConfiguredStackWorkload", logical: true),
            Entry("Partial", "1.0.0", "PartiallyConfiguredStackWorkload"),
            Entry("Missing", "1.0.0", "MissingWorkloadType"),
            new() { PackageId = "Content", PackageVersion = "5.0", Kind = WorkloadKind.Content },
            new() { PackageId = "WrongRid", PackageVersion = "1.0", RuntimeIdentifier = "not-this-platform" },
        ];
        Stage(entries);
        NativeTemplateWorkloadCapture capture = new();
        ServiceCollection services = new();
        var interaction = new TestInteractionService();

        await WorkloadRegistration.RegisterWorkloadsAsync(services, new(_home), interaction, capture, CancellationToken.None);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        var runtime = provider.GetRequiredService<IWorkloadProvider>();
        var availability = await new NativeTemplateAvailability(runtime, capture).CaptureAsync(CancellationToken.None);

        runtime.GetRuntimeWorkloads().Select(row => row.PackageId).Should().BeEquivalentTo("Physical", "Partial");
        runtime.GetContentWorkloads().Should().ContainSingle();
        new InstalledProjectStackCatalog(provider.GetServices<IProjectStack>()).FindStack("partial").Should().NotBeNull();
        var configured = new InstalledProjectStackCatalog(capture.SelectConfiguredStacks(provider.GetServices<IProjectStack>().ToArray(), services));
        configured.FindStack("partial").Should().BeNull();
        configured.FindStack("configured").Should().NotBeNull();
        configured.Stacks.Should().ContainSingle();
        availability.Workloads.Select(row => row.PackageId).Should().BeEquivalentTo("Physical", "Published", "Partial", "Missing");
        availability.Workloads.Single(row => row.PackageId == "Physical").Version.ToNormalizedString().Should().Be("2.0.0");
        availability.Workloads.Single(row => row.PackageId == "Published").Version.ToNormalizedString().Should().Be("1.4.0");
        availability.Workloads.Single(row => row.PackageId == "Published").Aliases.Should().Equal("published-alias");
        availability.Workloads.Where(row => row.PackageId is "Partial" or "Missing").Should().OnlyContain(row => !row.IsUsable);
        availability.Workloads.Where(row => row.PackageId is "Physical" or "Published").Should().OnlyContain(row => row.IsUsable);
        interaction.Lines.Should().Contain(line => line.Contains("failed to initialize", StringComparison.Ordinal))
            .And.Contain(line => line.Contains("could not be loaded", StringComparison.Ordinal))
            .And.Contain(line => line.Contains("not-this-platform", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterWorkloadsAsync_DefaultOverload_DoesNotRegisterNativeCaptureOrChangeExistingProviderBehavior()
    {
        Stage([Entry("Partial", "1.0.0", "PartiallyConfiguredStackWorkload")]);
        ServiceCollection services = new();

        await WorkloadRegistration.RegisterWorkloadsAsync(services, new(_home), new TestInteractionService(), CancellationToken.None);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        provider.GetService<NativeTemplateWorkloadCapture>().Should().BeNull();
        provider.GetService<INativeTemplateAvailability>().Should().BeNull();
        provider.GetRequiredService<IWorkloadProvider>().GetRuntimeWorkloads().Should().ContainSingle();
        new InstalledProjectStackCatalog(provider.GetServices<IProjectStack>()).FindStack("partial").Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterWorkloadsAsync_CanceledBoot_DoesNotPublishCompletedCapture()
    {
        NativeTemplateWorkloadCapture capture = new();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> register = () => WorkloadRegistration.RegisterWorkloadsAsync(
            new ServiceCollection(), new(_home), new TestInteractionService(), capture, cancellation.Token);

        await register.Should().ThrowAsync<OperationCanceledException>();
        Action read = () => capture.GetCompletedRegistrations();
        read.Should().Throw<InvalidOperationException>().WithMessage("*explicitly completed*");
    }

    [Fact]
    public async Task CaptureAsync_RegistryRemovedAfterBoot_DoesNotReadItAgain()
    {
        Stage([Entry("Selected", "1.0.0", "StubWorkload")]);
        NativeTemplateWorkloadCapture capture = new();
        ServiceCollection services = new();
        await WorkloadRegistration.RegisterWorkloadsAsync(services, new(_home), new TestInteractionService(), capture, CancellationToken.None);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        File.Delete(Path.Combine(_home, WorkloadPathsOptions.WorkloadRegistryFileName));
        var availability = new NativeTemplateAvailability(provider.GetRequiredService<IWorkloadProvider>(), capture);

        var snapshot = await availability.CaptureAsync(CancellationToken.None);

        snapshot.Workloads.Should().ContainSingle().Which.IsUsable.Should().BeTrue();
    }

    public void Dispose()
    {
        foreach (ServiceProvider provider in _providers)
        {
            foreach (RuntimeWorkloadInfo runtime in provider.GetRequiredService<IWorkloadProvider>().GetRuntimeWorkloads())
            {
                runtime.LoadContext.Unload();
            }

            provider.Dispose();
        }

        if (Directory.Exists(_home))
        {
            try
            {
                Directory.Delete(_home, recursive: true);
            }
            catch (IOException)
            {
                // Collectible workload contexts may retain assembly handles until after test disposal.
            }
            catch (UnauthorizedAccessException)
            {
                // Windows can retain mapped workload assemblies until the test process exits.
            }
        }
    }

    private void Stage(IReadOnlyList<WorkloadEntry> entries)
    {
        Directory.CreateDirectory(_home);
        foreach (WorkloadEntry entry in entries.Where(entry => entry.EntryPoint is not null))
        {
            var content = Path.Combine(_home, "workloads", entry.PackageId, entry.PackageVersion, "tools", "any");
            Directory.CreateDirectory(content);
            foreach (var source in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "tools", "any")))
            {
                File.Copy(source, Path.Combine(content, Path.GetFileName(source)), overwrite: true);
            }
        }

        WorkloadRegistry registry = new() { Workloads = [.. entries] };
        File.WriteAllText(Path.Combine(_home, WorkloadPathsOptions.WorkloadRegistryFileName),
            JsonSerializer.Serialize(registry, WorkloadJsonContext.Default.WorkloadRegistry));
    }

    private static WorkloadEntry Entry(string id, string version, string type, bool logical = false) => new()
    {
        PackageId = id,
        PackageVersion = version,
        Aliases = ["physical-alias"],
        EntryPoint = new() { AssemblyPath = Fixture, Type = Namespace + type },
        LogicalPackage = logical ? new() { PackageId = "Published", PackageVersion = "1.4.0", Aliases = ["published-alias"] } : null,
    };
}
