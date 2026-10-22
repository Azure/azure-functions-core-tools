// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Update;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("missing", false)]
    [InlineData("invalid", false)]
    [InlineData("valid", false)]
    [InlineData("valid", true)]
    public async Task IntegratedHost_ActualNodeBoot_PreservesLazyInstallationReadingAndScopedProviders(string metadata, bool overrideFileSystem)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        string installationRoot = Path.Combine(_root, "application-installation");
        string metadataPath = Path.Combine(installationRoot, InstallationMetadataReader.MetadataFileName);
        if (metadata != "missing")
        {
            await _files.CreateAllTextAsync(metadataPath, metadata == "valid"
                ? """{"schemaVersion":1,"installMethod":"standalone","updatePolicy":"in-place"}"""
                : """{"schemaVersion":1,"installMethod":"standalone","updatePolicy":"in-place","unexpected":true}""",
                CancellationToken.None);
        }

        using var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(_interaction, _paths, new(Home), services =>
        {
            services.Where(service => service.ServiceType == typeof(IFileSystem)).Should().ContainSingle()
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            services.Where(service => service.ServiceType == typeof(IInstallationMetadataReader)).Should().ContainSingle()
                .Which.ImplementationType.Should().Be(typeof(InstallationMetadataReader));
            var capture = services.Select(service => service.ImplementationInstance).OfType<NativeTemplateWorkloadCapture>().Distinct().Single();
            var receipt = capture.GetCompletedRegistrations().Should().ContainSingle().Subject;
            receipt.Workload.Should().NotBeNull();
            receipt.Failure.Should().BeNull();
            receipt.Aliases.Should().Contain("node");
            _loadContexts.Add(receipt.Workload!.LoadContext);
            services.Configure<CliEnvironmentOptions>(options => options.BaseDirectory = installationRoot);
            if (overrideFileSystem)
            {
                services.AddSingleton<IFileSystem>(_files);
            }

            services.AddFirstPartyNativeWorkflows();
        }, enableBundleBindingConstraints: false, CancellationToken.None);
        using var invocation = host.CreateInvocation();
        var services = invocation.Services;
        var reader = services.GetRequiredService<IInstallationMetadataReader>();
        services.GetRequiredService<IOptions<CliEnvironmentOptions>>().Value.BaseDirectory.Should().Be(installationRoot);
        installationRoot.Should().NotBe(Home).And.NotBe(_paths.Home);
        services.GetRequiredService<InstalledProjectStackCatalog>().Stacks.Should().ContainSingle()
            .Which.Stack.Should().Be("node");
        var provider = services.GetRequiredService<NodeTemplateWorkflowProvider>();
        services.GetServices<INativeItemWorkflowProvider>().OfType<NodeTemplateWorkflowProvider>().Should().ContainSingle()
            .Which.Should().BeSameAs(provider);
        services.GetServices<INativeProjectWorkflowProvider>().OfType<NodeTemplateWorkflowProvider>().Should().ContainSingle()
            .Which.Should().BeSameAs(provider);
        if (overrideFileSystem)
        {
            services.GetRequiredService<IFileSystem>().Should().BeSameAs(_files);
        }
        else
        {
            services.GetRequiredService<IFileSystem>().Should().BeOfType<PhysicalFileSystem>();
        }

        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "integrated-project"));
        var result = await invocation.Composer.ExecuteProjectAsync(new(target, "Azure.Functions.Node.Basic.JavaScript",
            "IntegratedApp", "node", "js", false, true, false, true,
            ["--skip-npm-install", "true", "--no-bundles", "true"], new HashSet<string>()), CancellationToken.None);

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Diagnostics));
        File.Exists(Path.Combine(target.Info.FullName, ".func", "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(target.Info.FullName, "package.json")).Should().BeTrue();
        using var next = host.CreateInvocation();
        next.Services.GetRequiredService<NodeTemplateWorkflowProvider>().Should().NotBeSameAs(provider);
        next.Services.GetRequiredService<IInstallationMetadataReader>().Should().BeSameAs(reader);
        next.Services.GetRequiredService<IFileSystem>().Should().BeSameAs(services.GetRequiredService<IFileSystem>());
        Func<Task> read = () => reader.ReadAsync(CancellationToken.None);
        if (metadata == "missing")
        {
            (await read.Should().ThrowAsync<FileNotFoundException>()).Which.FileName.Should().Be(metadataPath);
        }
        else if (metadata == "invalid")
        {
            await read.Should().ThrowAsync<InvalidInstallationMetadataException>();
        }
        else
        {
            (await reader.ReadAsync(CancellationToken.None)).Should()
                .Be(new InstallationMetadata(InstallationUpdatePolicy.InPlace));
        }
    }
}
