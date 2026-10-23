// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates;

internal sealed class NodeFunctionCatalogFixture(WorkloadEntry stack, string archive) : IBasicCompanionCatalog, IBasicPackagePayloadSource
{
    private readonly WorkloadEntry _stack = stack ?? throw new ArgumentNullException(nameof(stack));
    private readonly string _archive = archive ?? throw new ArgumentNullException(nameof(archive));

    public async Task<BasicCompanionManifest> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using PackageArchiveReader package = new(_archive);
        JsonArray templates = [];
        foreach (string path in package.GetFiles().Where(path => path.EndsWith("/.template.config/template.json", StringComparison.Ordinal)))
        {
            using var configuration = JsonDocument.Parse(package.GetStream(path));
            templates.Add(new JsonObject
            {
                ["path"] = path,
                ["identity"] = configuration.RootElement.GetProperty("identity").GetString(),
                ["type"] = "item",
                ["contentRole"] = "function",
            });
        }
        string version = package.NuspecReader.GetVersion().ToNormalizedString();
        JsonObject document = new()
        {
            ["schemaVersion"] = 2,
            ["revision"] = "explicit-local-node-function-fixture",
            ["stacks"] = new JsonArray(new JsonObject
            {
                ["packageId"] = _stack.LogicalPackage?.PackageId ?? _stack.PackageId,
                ["versions"] = $"[{_stack.LogicalPackage?.PackageVersion ?? _stack.PackageVersion}]",
                ["sources"] = new JsonArray(BasicAcquisitionFixture.Source),
                ["hostIdentifier"] = "func",
                ["engineHostVersions"] = $"[{AssemblyCliVersionProvider.Instance.Version}]",
                ["channels"] = new JsonArray("preview"),
                ["companions"] = new JsonArray(new JsonObject
                {
                    ["packageId"] = package.NuspecReader.GetId(), ["versions"] = $"[{version}]",
                    ["requiredRoles"] = new JsonArray("function"),
                }),
            }),
            ["artifacts"] = new JsonArray(new JsonObject
            {
                ["packageId"] = package.NuspecReader.GetId(), ["version"] = version,
                ["source"] = BasicAcquisitionFixture.Source,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(_archive, cancellationToken))),
                ["contentRevision"] = "real-repo-node-functions-fixture-not-publisher-authority",
                ["basicOnly"] = false, ["templates"] = templates,
            }),
        };
        return new BasicCompanionManifestReader().Read(document.ToJsonString());
    }

    public Task<Stream> OpenAsync(BasicTemplateArtifact artifact, bool offline, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(File.OpenRead(_archive));
    }

    internal async Task<BasicAcquisitionResult> InstallAsync(TemplateStore templates, WorkloadStore store, WorkloadPathsOptions paths,
        Azure.Functions.Cli.Workloads.Install.WorkloadInstaller installer, PhysicalFileSystem files,
        Azure.Functions.Cli.Workloads.Install.IWorkloadDeploymentService deployment, string home, string root)
    {
        INativeTemplateOwnership ownership = new Azure.Functions.Cli.Templates.Acquisition.NativeTemplateOwnership(files, new(home));
        BasicTemplateLifecycle lifecycle = new(templates, this, new BasicArtifactInspector(files), ownership, files);
        FileWorkloadOperationCoordinator coordinator = new(paths);
        PackageSourceProvider sources = new(Options.Create(new WorkloadCatalogOptions { Source = BasicAcquisitionFixture.Source }));
        BasicAcquisitionRunner runner = new(this, new(), sources, store, paths, installer, lifecycle, files, coordinator, deployment);
        BasicAcquisitionRequest request = new(_stack.LogicalPackage?.PackageId ?? _stack.PackageId,
            NuGetVersion.Parse(_stack.LogicalPackage?.PackageVersion ?? _stack.PackageVersion), WorkingDirectory.FromExplicit(root),
            BasicAcquisitionFixture.Source, IncludePrerelease: true)
        {
            NativeContext = new(_stack, "func", NuGetVersion.Parse(AssemblyCliVersionProvider.Instance.Version), BundleChannel.Preview,
                [NativeTemplateContentRole.Function]),
        };
        return await runner.RunAsync(request, CancellationToken.None);
    }
}
