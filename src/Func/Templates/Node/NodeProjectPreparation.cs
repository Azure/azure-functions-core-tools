// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using NuGet.Versioning;

using static Azure.Functions.Cli.Templates.Node.NodeItemCompatibility;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Composes the existing fixed Node npm helper after source-qualified native project creation.
/// </summary>
internal sealed class NodeProjectPreparation(IFileSystem fileSystem, NodeNpmInstaller npmInstaller) : INativeProjectPreparation
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly NodeNpmInstaller _npmInstaller = npmInstaller ?? throw new ArgumentNullException(nameof(npmInstaller));

    public async Task<NativeProjectPreparationPreview> PreviewAsync(NativeProjectPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ResolvedTemplate selected = context.SelectedTemplate;
            TemplateCatalogEntry entry = selected.Entry;
            if (entry.Type != TemplateType.Project || entry.Package is not { Identifier: PackageId, Version: not null } package
                || context.NativePreview.Status != TemplateInvocationStatus.Success || context.NativePreview.ProjectConfigurations.Count != 1)
            {
                return Invalid("Node npm preparation requires one selected native Node project and its validated preview.");
            }
            TemplateProjectConfigurationPlan plan = context.NativePreview.ProjectConfigurations[0];
            if (plan.Stack != "node" || plan.Language != entry.Language
                || Path.GetFullPath(plan.Root) != context.TargetDirectory.Info.FullName
                || !context.Parameters.TryGetValue("SkipNpmInstall", out string? value) || !bool.TryParse(value, out bool skip))
            {
                return Invalid("Node npm preparation requires matching target/language and the declared canonical SkipNpmInstall value.");
            }
            TemplateInvocationResult? changed = await selected.ValidateConfigurationAsync(cancellationToken);
            if (changed is not null)
            {
                return Invalid(changed.Diagnostic ?? "The selected native Node project changed.");
            }

            using var manifest = JsonDocument.Parse(await selected.ReadPackageFileAsync(ManifestPath, cancellationToken));
            JsonElement root = manifest.RootElement;
            if (Text(root, "packageId") != package.Identifier
                || !NuGetVersion.TryParse(Text(root, "packageVersion"), out NuGetVersion? version)
                || !NuGetVersion.TryParse(package.Version, out NuGetVersion? installed) || installed != version)
            {
                return Invalid("Node project preparation source does not match the selected native package ID/version.");
            }
            JsonElement[] mappings = [.. Array(root, "projects").Where(mapping => Text(mapping, "identity") == entry.Identity)];
            if (mappings.Length != 1
                || !string.Equals(Text(Property(mappings[0], "configuration"), "sha256"), selected.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Invalid("Node project preparation source does not match the selected native configuration hash.");
            }
            JsonElement mapping = mappings[0];
            ReadOnlyMemory<byte> configuration = await ReadVerifiedAsync(selected, Property(mapping, "configuration"), cancellationToken);
            await ReadVerifiedAsync(selected, Property(mapping, "host"), cancellationToken);
            await ReadVerifiedAsync(selected, Property(mapping, "package"), cancellationToken);
            using var config = JsonDocument.Parse(configuration);
            if (Text(config.RootElement, "identity") != entry.Identity)
            {
                return Invalid("The mapped Node project source names a different native identity.");
            }
            return new(true, [new(NativeProjectPreparationOperation.NodeNpmInstall, skip)], []);
        }
        catch (InvalidDataException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Invalid(exception.Message);
        }
        catch (JsonException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Invalid(exception.Message);
        }
        catch (IOException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Invalid(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Invalid(exception.Message);
        }
    }

    public async Task<NativeProjectPreparationResult> CompleteAsync(NativeProjectPreparationContext context,
        NativeProjectPreparationPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preview);
        NativeProjectPreparationPreview current = await PreviewAsync(context, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!current.Succeeded || !preview.Succeeded || !current.Operations.SequenceEqual(preview.Operations))
        {
            return new(false, ["Node preparation changed after preview; the generated project was retained.", .. current.Diagnostics]);
        }
        if (current.Operations.Single().Skipped)
        {
            return new(true, ["Skipped npm installation."]);
        }

        string root = context.TargetDirectory.Info.FullName;
        string configurationPath = context.NativePreview.ProjectConfigurations[0].ConfigurationPath;
        if (!_fileSystem.FileExists(Path.Combine(root, "package.json")) || !_fileSystem.FileExists(Path.Combine(root, "host.json"))
            || !_fileSystem.FileExists(configurationPath))
        {
            return new(false, ["Native project files and mandatory CLI configuration must exist before npm installation."]);
        }
        string? json = await _fileSystem.ReadAllTextIfExistsAsync(configurationPath, cancellationToken);
        if (json is null)
        {
            return new(false, ["Mandatory CLI configuration disappeared before npm installation."]);
        }
        using var config = JsonDocument.Parse(json);
        if (!config.RootElement.TryGetProperty("stack", out JsonElement stack) || Text(stack, "runtime") != "node"
            || Text(stack, "language") != context.SelectedTemplate.Entry.Language)
        {
            return new(false, ["Mandatory CLI configuration does not match the selected Node project."]);
        }
        (int exitCode, string stderr) = await _npmInstaller.InstallAsync(root, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return exitCode == 0 ? new(true, string.IsNullOrWhiteSpace(stderr) ? [] : [stderr])
            : new(false, [$"'npm install' failed (exit {exitCode}). The generated project and CLI configuration were retained.", stderr]);
    }

    private static NativeProjectPreparationPreview Invalid(string diagnostic) => new(false, [], [diagnostic]);
}
