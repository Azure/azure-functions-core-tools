// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Supplies the CLI-owned Node adapters to explicitly composed, invocation-scoped native workflows.
/// </summary>
internal sealed class NodeTemplateWorkflowProvider(IInteractionService interaction, IFileSystem fileSystem, NodeNpmInstaller npmInstaller)
    : INativeItemWorkflowProvider, INativeProjectWorkflowProvider
{
    private readonly NodeItemCompatibility _compatibility = new(interaction ?? throw new ArgumentNullException(nameof(interaction)));
    private readonly NodeProjectPreparation _preparation = new(fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)),
        npmInstaller ?? throw new ArgumentNullException(nameof(npmInstaller)));

    public string Stack => "node";

    public INativeItemCompatibility Compatibility => _compatibility;

    public INativeProjectPreparation Preparation => _preparation;

    public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeItemWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            NodeItemSource source = await NodeItemCompatibility.ReadSourceAsync(context.SelectedTemplate, cancellationToken);
            return NativeTemplateInvocationPreparation.Ready(context.Invocation with
            {
                SourceSnapshot = new(context.SelectedTemplate.Entry.Identity,
                    context.SelectedTemplate.ConfigurationSha256 ?? throw new InvalidDataException("The selected Node configuration has no fingerprint."),
                    source.Sources, [new(Path.GetFileName(source.ContentPath), source.ContentSource)]),
            });
        }
        catch (InvalidDataException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
        catch (System.Text.Json.JsonException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
        catch (IOException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
    }

    public bool Handles(TemplateCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Type == TemplateType.Project
            && entry.Package is { Identifier: NodeItemCompatibility.PackageId } package
            && NuGetVersion.TryParse(package.Version, out _)
            && (entry.Identity, entry.Language) is ("Azure.Functions.Node.Basic.JavaScript", "JavaScript")
                or ("Azure.Functions.Node.Basic.TypeScript", "TypeScript");
    }
}
