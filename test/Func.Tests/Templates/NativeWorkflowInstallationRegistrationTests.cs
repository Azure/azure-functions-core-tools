// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed class NativeWorkflowInstallationRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-installation-composition-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IntegratedHost_UnrelatedOverrides_DoNotGrantStackAuthorityOrReadInstallationMetadata()
    {
        var files = Substitute.For<IFileSystem>();
        var original = new IOException("installation metadata cannot be read");
        files.OpenReadIfExists(Arg.Any<string>()).Returns<Stream?>(_ => throw original);
        using var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(new TestInteractionService(),
            new(Path.Combine(_root, "workloads")), new(Path.Combine(_root, "cli")), services =>
            {
                services.AddSingleton(files);
                services.AddKeyedSingleton<IProjectStack>("unrelated", Substitute.For<IProjectStack>());
                services.AddSingleton(new object());
                services.AddFirstPartyNativeWorkflows();
            }, enableBundleBindingConstraints: false, CancellationToken.None);
        using var invocation = host.CreateInvocation();
        var services = invocation.Services;
        var reader = services.GetRequiredService<IInstallationMetadataReader>();

        var result = await invocation.Composer.ExecuteProjectAsync(new(
            WorkingDirectory.FromExplicit(Path.Combine(_root, "target")), "Azure.Functions.Node.Basic.JavaScript",
            null, "node", "js", false, true, false, true, [], new HashSet<string>()), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().Be("noInstalledStacks");
        services.GetRequiredService<InstalledProjectStackCatalog>().Stacks.Should().BeEmpty();
        (await services.GetRequiredService<INativeTemplateAvailability>().CaptureAsync(CancellationToken.None)).Workloads.Should().BeEmpty();
        services.GetRequiredService<IFileSystem>().Should().BeSameAs(files);
        services.GetRequiredService<IOptions<CliEnvironmentOptions>>().Value.BaseDirectory.Should().Be(AppContext.BaseDirectory);
        Directory.Exists(Path.Combine(_root, "target")).Should().BeFalse();
        files.DidNotReceiveWithAnyArgs().OpenReadIfExists(default!);
        Func<Task> read = () => reader.ReadAsync(CancellationToken.None);
        (await read.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(original);
        files.Received(1).OpenReadIfExists(Path.Combine(AppContext.BaseDirectory, InstallationMetadataReader.MetadataFileName));
    }

    [Fact]
    public async Task IntegratedHost_UnattributedStackAfterCapture_IsRejectedDespiteMetadataRegistrations()
    {
        using var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(new TestInteractionService(),
            new(Path.Combine(_root, "workloads")), new(Path.Combine(_root, "cli")), services =>
            {
                services.AddSingleton(Substitute.For<IProjectStack>());
                services.AddFirstPartyNativeWorkflows();
            }, enableBundleBindingConstraints: false, CancellationToken.None);

        Action invoke = () => host.CreateInvocation();

        invoke.Should().Throw<InvalidOperationException>().WithMessage("*changed after workload boot*");
        Directory.Exists(Path.Combine(_root, "target")).Should().BeFalse();
    }

    [Fact]
    public void CreateBuilder_ProfileDefaultsAndExplicitOverrides_PreserveSingletonOrdering()
    {
        var builder = CliHostFactory.CreateBuilder(new TestInteractionService());
        builder.Services.Where(service => service.ServiceType == typeof(IFileSystem)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(PhysicalFileSystem));
        builder.Services.Where(service => service.ServiceType == typeof(IInstallationMetadataReader)).Should().ContainSingle()
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        var files = Substitute.For<IFileSystem>();
        var reader = Substitute.For<IInstallationMetadataReader>();
        builder.Services.AddSingleton(files);
        builder.Services.AddSingleton(reader);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IFileSystem>().Should().BeSameAs(files);
        provider.GetRequiredService<IInstallationMetadataReader>().Should().BeSameAs(reader);
        provider.GetServices<IFileSystem>().Should().HaveCount(2);
        provider.GetService<NativeTemplateWorkloadCapture>().Should().BeNull();
        provider.GetService<NativeTemplateWorkflowComposer>().Should().BeNull();
        files.ReceivedCalls().Should().BeEmpty();
        reader.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void AddProfiles_PreRegisteredFileSystem_DoesNotReplaceOrDuplicateOverride()
    {
        ServiceCollection services = new();
        var files = Substitute.For<IFileSystem>();
        services.AddSingleton(files);

        services.AddProfiles();

        services.Where(service => service.ServiceType == typeof(IFileSystem)).Should().ContainSingle()
            .Which.ImplementationInstance.Should().BeSameAs(files);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
