// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Go;
using Azure.Functions.Cli.Templates.Java;
using Azure.Functions.Cli.Templates.PowerShell;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using HostGoPreparation = Azure.Functions.Cli.Templates.Go.Shared.GoProjectPreparation;
using HostPowerShellPreparation = Azure.Functions.Cli.Templates.PowerShell.Shared.PowerShellProjectPreparation;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData("Go", "go")]
    [InlineData("PowerShell", "powershell")]
    [InlineData("Java", "java")]
    public async Task ExplicitHostFactory_RealInstalledStack_SourceQualifiedProviderIsSelectedWithoutRequestCapability(string stack, string alias)
    {
        using var installed = await InstallAsync(stack);
        TestInteractionService interaction = new();
        using var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(interaction,
            new(Path.Combine(_root, "workloads")), new(Home), services =>
            {
                _contexts.AddRange(services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>()
                    .Select(workload => workload.LoadContext));
                services.AddSingleton<HostGoPreparation>();
                services.AddScoped<GoNativeProjectPreparation>();
                services.AddScoped<INativeProjectWorkflowProvider, GoProjectWorkflowProvider>();
                services.AddSingleton<HostPowerShellPreparation>();
                services.AddScoped<PowerShellNativeProjectPreparation>();
                services.AddScoped<INativeProjectWorkflowProvider, PowerShellProjectWorkflowProvider>();
                services.AddScoped<INativeProjectWorkflowProvider, JavaProjectWorkflowProvider>();
            }, enableBundleBindingConstraints: false, CancellationToken.None);
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "factory-" + alias));
        var request = new InitExecutionRequest(target, $"Azure.Functions.{stack}.Basic", "OwnedProject",
            alias, stack, false, true, false, true,
            stack == "Go" ? ["--no-bundles", "true", "--skip-go-mod-tidy", "true"] : ["--no-bundles", "true"],
            new HashSet<string>());
        request.ProjectPreparation.Should().BeNull();
        using var invocation = host.CreateInvocation();

        var result = await invocation.Composer.ExecuteProjectAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue(string.Join(" ", result.Diagnostics));
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(target.Info.FullName, ".func", "config.json")));
        config.RootElement.GetProperty("stack").GetProperty("runtime").GetString().Should().Be(alias);
        if (stack == "Java")
        {
            result.Preparation.Should().BeNull();
            File.Exists(Path.Combine(target.Info.FullName, "pom.xml")).Should().BeTrue();
        }
        else
        {
            result.Preparation!.Preview.Operations.Should().ContainSingle().Which.Skipped.Should().BeTrue();
            result.Preparation.Completion!.Succeeded.Should().BeTrue();
        }

        File.Exists(Path.Combine(target.Info.FullName, "requirements.psd1")).Should().BeFalse();
    }
}

public sealed class NativeWorkflowDefaultRegistrationTests
{
    [Fact]
    public void DefaultCliHostBuilder_DoesNotRegisterNativeFactoryComposerOrCapture()
    {
        var builder = CliHostFactory.CreateBuilder(new TestInteractionService());

        builder.Services.Should().NotContain(service => service.ServiceType == typeof(NativeTemplateWorkflowComposer));
        builder.Services.Should().NotContain(service => service.ServiceType == typeof(NativeTemplateWorkflowRegistry));
        builder.Services.Should().NotContain(service => service.ServiceType == typeof(INativeTemplateAvailability));
        builder.Services.Should().NotContain(service => service.ServiceType == typeof(NativeTemplateWorkloadCapture));
    }
}
