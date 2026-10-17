// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Go;
using Azure.Functions.Cli.Templates.Java;
using Azure.Functions.Cli.Templates.PowerShell;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using NuGet.Packaging;
using HostGoPreparation = Azure.Functions.Cli.Templates.Go.Shared.GoProjectPreparation;
using HostPowerShellPreparation = Azure.Functions.Cli.Templates.PowerShell.Shared.PowerShellProjectPreparation;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Theory]
    [InlineData(true, 0, "")]
    [InlineData(false, 17, "fresh boot tidy failure")]
    public async Task OwnedFactory_Go_ActualBootSelectsProductionPreparationAndPreservesSkipOrFailure(
        bool skip, int exitCode, string stderr)
    {
        using var installed = await InstallAsync("Go");
        HostGoTool helper = new() { Outcome = (exitCode, stderr) };
        using var host = await CreateOwnedFactoryAsync("go", helper, new HostAzLookup("12"));
        using var invocation = host.CreateInvocation();
        var request = OwnedFactoryRequest("Go", "go", "golang",
            skip ? ["--skip-go-mod-tidy", "true"] : []);
        request.ProjectPreparation.Should().BeNull();

        var result = await invocation.Composer.ExecuteProjectAsync(request, CancellationToken.None);

        result.Kind.Should().Be(skip ? "created" : "partialPreparation", string.Join(" ", result.Diagnostics));
        result.Succeeded.Should().Be(skip);
        result.Preparation!.Preview.Operations.Should().ContainSingle().Which.Skipped.Should().Be(skip);
        helper.Calls.Should().Be(skip ? 0 : 1);
        AssertOwnedFactoryConfiguration(request, "go", "Go");
        if (!skip)
        {
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains(stderr, StringComparison.Ordinal));
            result.Preparation.Completion!.Diagnostics.Should().Contain(stderr);
        }
    }

    [Fact]
    public async Task OwnedFactory_Go_CancellationFromOwnedToolPropagatesAfterNativeConfiguration()
    {
        using var installed = await InstallAsync("Go");
        using CancellationTokenSource cancellation = new();
        HostGoTool helper = new() { DuringExecution = cancellation.Cancel };
        using var host = await CreateOwnedFactoryAsync("go", helper, new HostAzLookup("12"));
        using var invocation = host.CreateInvocation();
        var request = OwnedFactoryRequest("Go", "go", "golang", []);
        request.ProjectPreparation.Should().BeNull();

        Func<Task> action = () => invocation.Composer.ExecuteProjectAsync(request, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        helper.Calls.Should().Be(1);
        AssertOwnedFactoryConfiguration(request, "go", "Go");
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "12")]
    [InlineData(true, null)]
    public async Task OwnedFactory_PowerShell_ActualBootAppliesExactOnceResolvedPreviewOrDefaultSkip(bool enabled, string? major)
    {
        using var installed = await InstallAsync("PowerShell");
        HostAzLookup helper = new(major);
        using var host = await CreateOwnedFactoryAsync("powershell", new HostGoTool(), helper);
        using var invocation = host.CreateInvocation();
        var request = OwnedFactoryRequest("PowerShell", "powershell", "pwsh",
            ["--managed-dependencies", enabled.ToString(), "--runtime-version", "7.6"]);
        request.ProjectPreparation.Should().BeNull();

        var result = await invocation.Composer.ExecuteProjectAsync(request, CancellationToken.None);

        result.Kind.Should().Be("created", string.Join(" ", result.Diagnostics));
        result.Succeeded.Should().BeTrue();
        AssertOwnedFactoryConfiguration(request, "powershell", "PowerShell");
        helper.Lookups.Should().Be(enabled ? 1 : 0, "the real completion must not repeat the preview lookup");
        string target = Path.Combine(request.WorkingDirectory.Info.FullName, "requirements.psd1");
        File.Exists(target).Should().Be(enabled);
        if (enabled)
        {
            string original = new HostPowerShellPreparation().ReadRequirementsTemplate();
            var plan = (TemplateCreateFileEffect)result.Preparation!.Preview.FileEffects!.Effects.Single();
            plan.Content.Should().Be(major is null ? original : original.Replace("MAJOR_VERSION", major, StringComparison.Ordinal));
            plan.Source.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original))));
            File.ReadAllText(target).Should().Be(plan.Content);
            result.Preparation.Completion!.Effects!.CompletedEffects.Should().ContainSingle().Which.Should().Be(plan);
            if (major is null)
            {
                result.Preparation.Preview.Diagnostics.Should().ContainSingle().Which.Should().Contain("Could not resolve", "commented", "MAJOR_VERSION");
                result.Preparation.Completion.Diagnostics.Should().Contain(diagnostic => diagnostic.Contains("Could not resolve", StringComparison.Ordinal));
            }
        }
        else
        {
            helper.SourceReads.Should().Be(0);
            result.Preparation!.Preview.Operations.Should().ContainSingle().Which.Skipped.Should().BeTrue();
        }
    }

    [Fact]
    public async Task OwnedFactory_PowerShell_PreviewResolvesExactPlanWithoutLiveFilesOrCompletion()
    {
        using var installed = await InstallAsync("PowerShell");
        HostAzLookup helper = new("12");
        using var host = await CreateOwnedFactoryAsync("powershell", new HostGoTool(), helper);
        using var invocation = host.CreateInvocation();
        var request = OwnedFactoryRequest("PowerShell", "powershell", "pwsh",
            ["--managed-dependencies", "true"]) with { DryRun = true };
        request.ProjectPreparation.Should().BeNull();

        var result = await invocation.Composer.ExecuteProjectAsync(request, CancellationToken.None);

        result.Kind.Should().Be("previewed", string.Join(" ", result.Diagnostics));
        helper.Lookups.Should().Be(1);
        ((TemplateCreateFileEffect)result.Preparation!.Preview.FileEffects!.Effects.Single()).Content.Should().Contain("# 'Az' = '12.*'");
        result.Preparation.Completion.Should().BeNull();
        Directory.Exists(request.WorkingDirectory.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task OwnedFactory_Java_ActualBootUsesQualifiedConfigurationWithoutAnyPreparationOperation()
    {
        using var installed = await InstallAsync("Java");
        HostGoTool go = new();
        HostAzLookup ps = new("12");
        using var host = await CreateOwnedFactoryAsync("java", go, ps);
        using var invocation = host.CreateInvocation();
        var request = OwnedFactoryRequest("Java", "java", "Java", []);
        request.ProjectPreparation.Should().BeNull();

        var result = await invocation.Composer.ExecuteProjectAsync(request, CancellationToken.None);

        result.Kind.Should().Be("created", string.Join(" ", result.Diagnostics));
        result.Preparation.Should().BeNull();
        AssertOwnedFactoryConfiguration(request, "java", "Java");
        File.ReadAllText(Path.Combine(request.WorkingDirectory.Info.FullName, "pom.xml")).Should().Contain("<artifactId>factoryapp</artifactId>");
        Directory.Exists(Path.Combine(request.WorkingDirectory.Info.FullName, "target")).Should().BeFalse();
        go.Calls.Should().Be(0);
        ps.Lookups.Should().Be(0);
    }

    private async Task<NativeTemplateWorkflowHost> CreateOwnedFactoryAsync(
        string stack, HostGoPreparation go, HostPowerShellPreparation powershell, CancellationToken cancellationToken = default)
    {
        return await new NativeTemplateWorkflowHostFactory().CreateAsync(new TestInteractionService(),
            new(Path.Combine(_root, "workloads")), new(Home), services =>
            {
                NativeTemplateWorkloadCapture capture = services.Select(service => service.ImplementationInstance)
                    .OfType<NativeTemplateWorkloadCapture>().Distinct().Single();
                var receipt = capture.GetCompletedRegistrations().Should().ContainSingle().Subject;
                using PackageArchiveReader archive = new(BuiltArchive(stack == "go" ? "Go" : stack == "java" ? "Java" : "PowerShell"));
                receipt.PackageId.Should().BeEquivalentTo(archive.NuspecReader.GetId());
                receipt.PackageVersion.Should().Be(archive.NuspecReader.GetVersion().ToNormalizedString());
                receipt.Aliases.Should().Contain(stack);
                receipt.Workload.Should().NotBeNull();
                receipt.Failure.Should().BeNull();
                foreach (RuntimeWorkloadInfo workload in services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>())
                {
                    _contexts.Add(workload.LoadContext);
                }
                if (stack == "go")
                {
                    services.AddSingleton(go);
                    services.AddScoped<GoNativeProjectPreparation>();
                    services.AddScoped<INativeProjectWorkflowProvider, GoProjectWorkflowProvider>();
                }
                else if (stack == "powershell")
                {
                    services.AddSingleton(powershell);
                    services.AddScoped<PowerShellNativeProjectPreparation>();
                    services.AddScoped<INativeProjectWorkflowProvider, PowerShellProjectWorkflowProvider>();
                }
                else
                {
                    services.AddScoped<INativeProjectWorkflowProvider, JavaProjectWorkflowProvider>();
                }
            }, enableBundleBindingConstraints: false, cancellationToken);
    }

    private InitExecutionRequest OwnedFactoryRequest(string stack, string runtime, string language, IReadOnlyList<string> options)
        => new(WorkingDirectory.FromExplicit(Path.Combine(_root, "factory-project")), $"Azure.Functions.{stack}.Basic",
            "FactoryApp", runtime, language, false, true, false, true, ["--no-bundles", "true", .. options], new HashSet<string>());

    private static void AssertOwnedFactoryConfiguration(InitExecutionRequest request, string runtime, string language)
    {
        string root = request.WorkingDirectory.Info.FullName;
        var configuration = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".func", "config.json")))!;
        configuration["stack"]!["runtime"]!.GetValue<string>().Should().Be(runtime);
        configuration["stack"]!["language"]!.GetValue<string>().Should().Be(language);
        File.Exists(Path.Combine(root, "host.json")).Should().BeTrue();
    }
}
