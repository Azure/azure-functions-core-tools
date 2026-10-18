// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads.Node;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Supplies the CLI-owned Node adapters to explicitly composed, invocation-scoped native workflows.
/// </summary>
internal sealed class NodeTemplateWorkflowProvider(IInteractionService interaction, IFileSystem fileSystem, NodeNpmInstaller npmInstaller,
    NativeTemplateWorkloadCapture? capture = null, InstalledProjectStackCatalog? stacks = null)
    : INativeItemWorkflowProvider, INativeProjectWorkflowProvider
{
    private readonly NodeItemCompatibility _compatibility = new(interaction ?? throw new ArgumentNullException(nameof(interaction)), () =>
    {
        if (capture is null && stacks is null)
        {
            return null;
        }
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(stacks);
        IProjectStack? selected = stacks.FindStack("node");
        NativeTemplateWorkloadRegistration? owner = selected is null ? null : capture.GetStackOrigin(selected);
        if (owner is null)
        {
            throw new InvalidOperationException("Node item update guidance requires its selected stack's captured physical owner.");
        }
        return $"The selected template archive is owned by physical workload '{owner.PackageId}@{owner.PackageVersion}'. "
            + $"Run 'func workload update {owner.LogicalPackageId ?? owner.PackageId} "
            + $"--version {owner.LogicalPackageVersion ?? owner.PackageVersion} --exact' to explicitly install a compatible version for this selected owner, then retry.";
    });
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
            NodeItemSourceQualifier.ValidateArguments(source, context.Invocation.Parameters);
            if (NodeItemSourceQualifier.ValidateDaprInvocation(source, context.Invocation.Name) is { } helperDiagnostic)
            {
                return NativeTemplateInvocationPreparation.Reject(helperDiagnostic);
            }
            return NativeTemplateInvocationPreparation.Ready(context.Invocation with
            {
                SourceSnapshot = new(context.SelectedTemplate.Entry.Identity,
                    context.SelectedTemplate.ConfigurationSha256 ?? throw new InvalidDataException("The selected Node configuration has no fingerprint."),
                    source.Sources, [new(Path.GetFileName(source.ContentPath), source.ContentSource)]),
            });
        }
        catch (NodeItemHelperCompatibilityException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeTemplateInvocationPreparation.Reject(_compatibility.DescribeUnsupportedHelpers(exception));
        }
        catch (NodeItemParameterCompatibilityException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return NativeTemplateInvocationPreparation.Reject(_compatibility.DescribeUnsupportedParameters(exception));
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
