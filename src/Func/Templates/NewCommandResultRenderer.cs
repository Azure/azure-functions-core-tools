// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandResultRenderer
{
    public void RenderResolutionFailure(NewCommandResolutionFailure failure);

    public int RenderApplyResult(FunctionTemplateInfo template, TemplateApplicationResult result);
}

internal sealed class NewCommandResultRenderer(
    IInteractionService interaction,
    NewCommandRenderer renderer) : INewCommandResultRenderer
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly NewCommandRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    public void RenderResolutionFailure(NewCommandResolutionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        switch (failure.Kind)
        {
            case NewCommandResolutionFailureKind.ProjectRequired:
                _renderer.RenderProjectRequired();
                break;

            case NewCommandResolutionFailureKind.HostJsonBundleMissing:
                _interaction.WriteError("Cannot resolve templates: host.json declares no extension bundle.");
                _interaction.WriteLine(line => line
                    .Muted("Configure ")
                    .Code("extensionBundle.id")
                    .Muted(" in host.json or run ")
                    .Code("func init")
                    .Muted(" with a stack that declares one."));
                break;

            case NewCommandResolutionFailureKind.UnrecognisedBundleId:
                _interaction.WriteError($"Unrecognized extension bundle id '{failure.BundleId}'.");
                _interaction.WriteLine(line => line
                    .Muted("Use one of: ")
                    .Code(BundleHelpers.StableBundleId)
                    .Muted(", ")
                    .Code(BundleHelpers.PreviewBundleId)
                    .Muted(", or ")
                    .Code(BundleHelpers.ExperimentalBundleId)
                    .Muted("."));
                break;

            case NewCommandResolutionFailureKind.NoTemplatesWorkloadInstalled:
                _renderer.RenderNoTemplatesWorkloadInstalled(failure.Stack!);
                break;

            case NewCommandResolutionFailureKind.NoTemplatesWorkloadForChannel:
                RenderMissingTemplatesWorkloadChannel(failure);
                break;

            case NewCommandResolutionFailureKind.MissingLanguage:
                _renderer.RenderMissingLanguage(failure.Stack!, failure.ProjectPath!);
                break;

            default:
                throw new UnreachableException(
                    $"Unhandled {nameof(NewCommandResolutionFailureKind)}: {failure.Kind}.");
        }
    }

    public int RenderApplyResult(FunctionTemplateInfo template, TemplateApplicationResult result)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(result);

        switch (result)
        {
            case TemplateApplicationResult.Created created:
                _interaction.WriteSuccess($"Created function '{template.Id}'.");
                foreach (string file in created.Files)
                {
                    _interaction.WriteLine(line => line.Muted("  ").Code(file));
                }

                return 0;

            case TemplateApplicationResult.AlreadyExists existing:
                _interaction.WriteError("Some files already exist:");
                foreach (string file in existing.ExistingFiles)
                {
                    _interaction.WriteLine(line => line.Muted("  ").Code(file));
                }

                _interaction.WriteLine(line => line
                    .Muted("Re-run with ")
                    .Code("--force")
                    .Muted(" to overwrite."));
                return 1;

            case TemplateApplicationResult.Failed failed:
                RenderApplicationFailure(failed.Failure);
                return 1;

            default:
                _interaction.WriteError("Unknown apply result.");
                return 1;
        }
    }

    private void RenderMissingTemplatesWorkloadChannel(NewCommandResolutionFailure failure)
    {
        string channelName = failure.Channel.ToDisplayString();
        string suggestedPackage = TemplatesWorkloadConstants.GetPackageId(failure.Stack!);
        string suggestedVersion = failure.Channel == Projects.BundleChannel.Stable
            ? "<version>"
            : $"<version>-{channelName}.1";
        _interaction.WriteError(
            $"No installed templates workload matches this project's bundle channel " +
            $"({failure.BundleId} -> channel '{channelName}').");
        _interaction.WriteLine(line => line
            .Muted("Install one with: ")
            .Code($"func workload install {suggestedPackage} --version {suggestedVersion}")
            .Muted("."));
    }

    private void RenderApplicationFailure(TemplateApplicationFailure failure)
    {
        switch (failure)
        {
            case TemplateApplicationFailure.WriteFailed write:
                _interaction.WriteError($"Failed to write '{write.Path}': {write.Message}");
                break;
            case TemplateApplicationFailure.InvalidPrompt prompt:
                _interaction.WriteError($"Invalid prompt '{prompt.PromptId}': {prompt.Reason}");
                break;
            case TemplateApplicationFailure.ProviderError provider:
                _interaction.WriteError(provider.Message);
                break;
            case TemplateApplicationFailure.MissingExtensionBundle bundle:
                _interaction.WriteError($"Stack '{bundle.Stack}' requires extension bundle '{bundle.SuggestedBundleId}', which is not installed.");
                break;
            case TemplateApplicationFailure.MinBundleVersionTooOld min:
                _interaction.WriteError(
                    $"Installed bundle '{min.InstalledBundleVersion}' is outside required range '{min.RequiredRange}' for templates workload '{min.TemplatesWorkloadVersion}'.");
                break;
            case TemplateApplicationFailure.NoTemplatesWorkloadForChannel workload:
                _interaction.WriteError(
                    $"No templates workload installed for channel '{workload.Channel}' on stack '{workload.Stack}'.");
                _interaction.WriteLine(line => line
                    .Muted("Install one with: ")
                    .Code($"func workload install {workload.SuggestedPackageId} --version {workload.SuggestedVersion}")
                    .Muted("."));
                break;
            default:
                _interaction.WriteError("Template application failed.");
                break;
        }
    }
}