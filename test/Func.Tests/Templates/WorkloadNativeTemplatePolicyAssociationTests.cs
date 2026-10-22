// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("wrong-role", false)]
    [InlineData("wrong-host", false)]
    [InlineData("wrong-channel", false)]
    [InlineData("receipt-digest", false)]
    public async Task LoadedPolicy_ActualSchema2Association_BindsSelectedRuntimeAndIndependentExternalContent(string changed, bool success)
    {
        WorkloadEntry runtime = await InstallPolicyFixtureAsync(fromCatalog: true);
        BasicTemplateArtifact artifact = WritePolicyExternalContent();
        BasicCompanionManifest manifest = PolicyAssociation(runtime, artifact);
        var authority = Substitute.For<IBasicCompanionCatalog>();
        authority.LoadAsync(Arg.Any<CancellationToken>()).Returns(_ => manifest);
        var coordinator = new FileWorkloadOperationCoordinator(_paths);
        NativeTemplateOwnership ownership = new(_files, new(Home));
        BasicTemplateLifecycle lifecycle = new(_templates, new PolicyArchivePayload(Path.Combine(_root, "policy-external.nupkg")),
            new BasicArtifactInspector(_files), ownership, _files);
        BasicAcquisitionRunner acquisition = new(authority, new(), new PackageSourceProvider(
            Options.Create(new WorkloadCatalogOptions { Source = artifact.Source })), _workloads, _paths, _installer,
            lifecycle, _files, coordinator, AdmissionDeployment(coordinator));
        var request = new BasicAcquisitionRequest(runtime.PackageId, NuGetVersion.Parse(runtime.PackageVersion),
            WorkingDirectory.FromExplicit(_root), artifact.Source)
        {
            NativeContext = new(runtime, "func", NuGetVersion.Parse(AssemblyCliVersionProvider.Instance.Version),
                BundleChannel.Stable, [NativeTemplateContentRole.AdditionalProject]),
        };

        BasicAcquisitionResult acquired = await acquisition.RunAsync(request, CancellationToken.None);
        acquired.Status.Should().Be(BasicAcquisitionStatus.Completed, acquired.Message);
        acquired.Completed.Should().Contain(step => step.PackageId == artifact.PackageId && step.Version == "2.0.0");
        if (changed == "wrong-role")
        {
            manifest = manifest with
            {
                Stacks = [manifest.Stacks[0] with
                {
                    Companions = [manifest.Stacks[0].Companions[0] with { RequiredRoles = [NativeTemplateContentRole.Function] }],
                }],
            };
        }
        if (changed == "wrong-host" || changed == "wrong-channel")
        {
            NativeTemplateAssociationPolicy policy = manifest.Stacks[0].NativePolicy!;
            manifest = manifest with
            {
                Stacks = [manifest.Stacks[0] with
                {
                    NativePolicy = changed == "wrong-host" ? policy with { HostIdentifier = "not-func" }
                        : policy with { Channels = [BundleChannel.Preview] },
                }],
            };
        }
        if (changed == "receipt-digest")
        {
            string receipt = Path.Combine(Home, "templates", ".native-template-owners", artifact.PackageId.ToLowerInvariant() + ".json");
            var data = JsonNode.Parse(File.ReadAllText(receipt))!;
            data["sha256"] = new string('A', 64);
            File.WriteAllText(receipt, data.ToJsonString());
        }
        using ServiceProvider provider = await CreatePolicyServicesAsync(authority);
        using IServiceScope scope = provider.CreateScope();
        string target = Path.Combine(_root, "external-owned-project");
        InitExecutionCommand command = new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            provider.GetRequiredService<InitExecutionRenderer>(), new([new(runtime.PackageId,
                NuGetVersion.Parse(runtime.PackageVersion), runtime.Aliases, true)]));

        int exit = await command.ParseInvocation([target, "--template", "Fixture.External.Project", "--name", "External",
            "--non-interactive", "--output", "json"]).InvokeAsync();

        exit.Should().Be(success ? 0 : 1, _interaction.AllOutput);
        Directory.Exists(target).Should().Be(success);
        if (success)
        {
            File.ReadAllText(Path.Combine(target, "External.proj")).Should().Be("External|net10.0");
            runtime.PackageVersion.Should().Be("1.0.0");
            artifact.Version.ToNormalizedString().Should().Be("2.0.0");
        }
    }

    private BasicTemplateArtifact WritePolicyExternalContent()
    {
        string input = Directory.CreateDirectory(Path.Combine(_root, "external-policy-content")).FullName;
        string project = Path.Combine(_root, "policy-content", "content", "project");
        foreach (string file in Directory.GetFiles(project, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(input, "content", "project", Path.GetRelativePath(project, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        string configuration = Path.Combine(input, "content", "project", ".template.config", "template.json");
        var data = JsonNode.Parse(File.ReadAllText(configuration))!;
        data["identity"] = "Fixture.External.Project";
        data["shortName"] = "fixture-external-project";
        File.WriteAllText(configuration, data.ToJsonString());
        string archive = Path.Combine(_root, "policy-external.nupkg");
        WritePolicyPackage(input, archive, "Fixture.Policy.External", "2.0.0", "FuncTemplate");
        return new("Fixture.Policy.External", NuGetVersion.Parse("2.0.0"), "https://fixtures.invalid/v3/index.json",
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))), "policy-external-fixture",
            [new("content/project/.template.config/template.json", "Fixture.External.Project", "project")])
        {
            ContentRoles = [new("Fixture.External.Project", NativeTemplateContentRole.AdditionalProject)],
        };
    }

    private static BasicCompanionManifest PolicyAssociation(WorkloadEntry runtime, BasicTemplateArtifact artifact)
        => new("policy-qualified-association",
            [new(runtime.PackageId, VersionRange.Parse("[1.0.0]"), [artifact.Source],
                [new(artifact.PackageId, VersionRange.Parse("[2.0.0]")) { RequiredRoles = [NativeTemplateContentRole.AdditionalProject] }])
            {
                NativePolicy = new("func", VersionRange.Parse($"[{AssemblyCliVersionProvider.Instance.Version}]"), [BundleChannel.Stable]),
            }], [artifact]) { SchemaVersion = 2 };

    private sealed class PolicyArchivePayload(string path) : IBasicPackagePayloadSource
    {
        public Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(File.OpenRead(path));
        }
    }
}
