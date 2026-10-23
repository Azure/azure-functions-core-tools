// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("list-absent")]
    [InlineData("interactive")]
    [InlineData("noninteractive")]
    public async Task NewOwnerOrder_ActualOwnedNode_PreservesQualifiedListAndSelection(string mode)
    {
        var entry = await InstallAssociationStackAsync();
        var catalog = new NodeFunctionCatalogFixture(entry, FunctionArchive);
        (await catalog.InstallAsync(_templates, _workloads, _paths, _installer, _files,
            AdmissionDeployment(new Azure.Functions.Cli.Workloads.Storage.FileWorkloadOperationCoordinator(_paths)), Home, _root))
            .Status.Should().Be(BasicAcquisitionStatus.Completed);
        string target = Path.Combine(_root, "owned-new");
        var contexts = NewOwnerContexts(target);
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(mode == "interactive");
        interaction.PromptForSelectionAsync(Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IEnumerable<string>>(1).First(value => value.Contains("Http", StringComparison.OrdinalIgnoreCase)));
        using var provider = await CreateServicesAsync(services =>
        {
            services.AddSingleton(contexts);
            services.AddSingleton(interaction);
            services.AddScoped<NativeTemplateWorkflowRegistry>();
        }, nativeCatalog: catalog);
        using var scope = provider.CreateScope();
        var before = NativeState();
        var request = NodeComposedItem(target, mode == "list-absent" ? "no-such-item" : null) with
        {
            List = mode.StartsWith("list", StringComparison.Ordinal), NonInteractive = mode != "interactive",
            Name = mode == "interactive" ? "MenuHttp" : null,
        };

        var result = await scope.ServiceProvider.GetRequiredService<INewExecutionRunner>()
            .ExecuteAsync(request, Availability(entry), CancellationToken.None);

        if (mode == "interactive")
        {
            result.Succeeded.Should().BeTrue(string.Join(" ", result.Diagnostics));
            result.SelectedTemplate!.Identity.Should().Be("Azure.Functions.Node.Http.JavaScript");
            File.ReadAllText(Path.Combine(target, "MenuHttp.js")).Should().Contain("app.http(");
        }
        else
        {
            result.Kind.Should().Be(mode == "noninteractive" ? "templateRequired" : "listed");
            if (mode == "list")
            {
                result.Templates.Should().Contain(value => value.Identity == "Azure.Functions.Node.Http.JavaScript");
                result.Templates.Should().OnlyContain(value => !value.IsHidden && value.DeclaredInternalRole == null
                    && value.Language == "JavaScript");
            }
            if (mode == "list-absent")
            {
                result.Succeeded.Should().BeTrue();
                result.Templates.Should().BeEmpty();
            }
            Directory.Exists(target).Should().BeFalse();
            NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
                .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("absent", "explicit")]
    [InlineData("absent", "list")]
    [InlineData("absent", "interactive")]
    [InlineData("foreign", "explicit")]
    [InlineData("foreign", "list")]
    [InlineData("foreign", "interactive")]
    [InlineData("refused", "explicit")]
    [InlineData("refused", "list")]
    [InlineData("refused", "interactive")]
    [InlineData("refused-with-legacy", "explicit")]
    [InlineData("refused-with-legacy", "list")]
    [InlineData("refused-with-legacy", "interactive")]
    [InlineData("unqualified", "explicit")]
    [InlineData("unqualified", "list")]
    [InlineData("unqualified", "interactive")]
    public async Task NewOwnerOrder_UnusableOwnedPolicy_NeverUsesInjectedFallbackOrWrites(string policy, string mode)
    {
        var entry = await InstallAssociationStackAsync();
        string archive = FunctionArchive;
        if (policy.StartsWith("refused", StringComparison.Ordinal))
        {
            archive = Path.Combine(_root, "refused-functions.nupkg");
            File.Copy(FunctionArchive, archive);
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Update);
            const string configuration = "content/http-js/.template.config/template.json";
            JsonNode changed;
            using (var stream = zip.GetEntry(configuration)!.Open()) changed = JsonNode.Parse(stream)!;
            changed["symbols"]!["InvocationOutputIntent"]!["binding"] = "host:WorkingDirectory";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(changed.ToJsonString());
            zip.GetEntry(configuration)!.Delete();
            using (var stream = zip.CreateEntry(configuration).Open()) stream.Write(bytes);
            const string manifestPath = "provenance/node/compatibility.json";
            JsonNode manifest;
            using (var stream = zip.GetEntry(manifestPath)!.Open()) manifest = JsonNode.Parse(stream)!;
            manifest["templates"]!.AsArray().Single(value => value!["identity"]!.GetValue<string>() == "Azure.Functions.Node.Http.JavaScript")!
                ["configuration"]!["sha256"] = Convert.ToHexString(SHA256.HashData(bytes));
            zip.GetEntry(manifestPath)!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(manifestPath).Open());
            writer.Write(manifest.ToJsonString());
        }
        var catalog = new NodeFunctionCatalogFixture(entry, archive);
        (await catalog.InstallAsync(_templates, _workloads, _paths, _installer, _files,
            AdmissionDeployment(new Azure.Functions.Cli.Workloads.Storage.FileWorkloadOperationCoordinator(_paths)), Home, _root))
            .Status.Should().Be(BasicAcquisitionStatus.Completed);
        string target = Path.Combine(_root, "refused-new");
        var contexts = NewOwnerContexts(target);
        var interaction = Substitute.For<IInteractionService>();
        interaction.IsInteractive.Returns(mode == "interactive");
        var compatibility = Substitute.For<INativeItemCompatibility>();
        var effects = Substitute.For<INativeSelectedItemEffects>();
        using var provider = await CreateServicesAsync(services =>
        {
            services.AddSingleton(contexts);
            services.AddSingleton(interaction);
            services.AddScoped<NativeTemplateWorkflowRegistry>();
            if (policy == "refused-with-legacy")
            {
                var legacy = Substitute.For<INativeItemWorkflowProvider>();
                legacy.Stack.Returns("node");
                legacy.Compatibility.Returns(compatibility);
                legacy.Effects.Returns(effects);
                services.AddSingleton(legacy);
            }
            if (policy is "absent" or "foreign")
            {
                var owner = services.Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<Azure.Functions.Cli.Workloads.RuntimeWorkloadInfo>().Single();
                services.RemoveAll<WorkloadNativeTemplatePolicyRegistration>();
                if (policy == "foreign")
                {
                    var foreign = Substitute.For<NativeTemplatePolicy>();
                    foreign.Stack.Returns("node");
                    services.AddScoped(_ => new WorkloadNativeTemplatePolicyRegistration(owner, foreign));
                }
            }
        }, nativeCatalog: policy == "unqualified" ? null : catalog);
        using var scope = provider.CreateScope();
        var before = NativeState();
        var request = NodeComposedItem(target, mode == "explicit" ? "http" : null) with
        {
            List = mode == "list", NonInteractive = mode != "interactive",
            ItemCompatibility = compatibility, SelectedItemEffects = effects,
        };

        var result = await scope.ServiceProvider.GetRequiredService<INewExecutionRunner>()
            .ExecuteAsync(request, Availability(entry), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().Be(policy.StartsWith("refused", StringComparison.Ordinal) && mode == "explicit"
            ? "internalTemplate" : "workflowUnavailable");
        Directory.Exists(target).Should().BeFalse();
        NativeState().OrderBy(pair => pair.Key, StringComparer.Ordinal).Should()
            .Equal(before.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        compatibility.ReceivedCalls().Should().BeEmpty();
        effects.ReceivedCalls().Should().BeEmpty();
        interaction.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(IInteractionService.PromptForSelectionAsync));
    }

    private static INewExecutionContextResolver NewOwnerContexts(string target)
    {
        var contexts = Substitute.For<INewExecutionContextResolver>();
        var directory = WorkingDirectory.FromExplicit(target);
        contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new NewExecutionContextResult(new(directory, new(directory, "node", "JavaScript")), null));
        return contexts;
    }
}
