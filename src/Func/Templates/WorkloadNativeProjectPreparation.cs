// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Invokes only the exact owning workload's typed preparation capability through the protected phases.
/// </summary>
internal sealed class WorkloadNativeProjectPreparation(NativeTemplatePolicyCandidate candidate, IFileSystem files) : INativeProjectPreparation
{
    private readonly NativeTemplatePolicyCandidate _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly Dictionary<NativeProjectPreparationPreview, BoundNativeProjectPreparation> _previews = new(ReferenceEqualityComparer.Instance);

    internal async Task<TemplateInvocationRequest> BindInvocationAsync(TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _candidate.RevalidateDeclarationAsync(cancellationToken);
        using NativeTemplatePolicyLifetime lifetime = new();
        WorkloadNativeTemplateSource source = _candidate.CreateSource(lifetime);
        await source.RevalidateAsync(cancellationToken);
        INativeTemplateProjectPreparation capability = _candidate.Admission.Registration.Policy.CreateProjectPreparation(source)
            ?? throw new InvalidDataException("The owning workload returned no required project preparation capability.");
        await source.RevalidateAsync(cancellationToken);
        return request with { ProjectPreparationBinding = new(_candidate, capability, request) };
    }

    internal bool OwnsPreview(NativeProjectPreparationContext context, NativeProjectPreparationPreview preview)
        => _previews.TryGetValue(preview, out BoundNativeProjectPreparation? binding)
        && ReferenceEquals(context.NativePreview.ProjectPreparationCapture?.Owner, binding)
        && ReferenceEquals(binding.Candidate, _candidate)
        && preview.OwnedPlan is not null && preview.Operations.Count == 0;

    public async Task<NativeProjectPreparationPreview> PreviewAsync(NativeProjectPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            NativeProjectPreparationCapture capture = context.NativePreview.ProjectPreparationCapture
                ?? throw new InvalidDataException("Required preparation has no actual captured invocation receipt.");
            if (!ReferenceEquals(capture.Owner.Candidate, _candidate)
                || context.SelectedTemplate.ConfigurationSha256 != _candidate.ConfigurationSha256
                || context.SelectedTemplate.Entry.Identity != _candidate.Entry.Identity
                || !NativeProjectPreparationCapture.SamePath(context.TargetDirectory.Info.FullName, capture.Project.Root))
            {
                throw new InvalidDataException("Preparation preview belongs to another owner, source or invocation.");
            }
            await _candidate.RevalidateDeclarationAsync(cancellationToken);
            using NativeTemplatePolicyLifetime lifetime = new();
            WorkloadNativeTemplateCapturedOutput view = capture.CreateView(lifetime);
            await view.RevalidateAsync(cancellationToken);
            NativeTemplateProjectPreparationPlan plan = await capture.Owner.Capability.PreviewAsync(view, cancellationToken)
                ?? throw new InvalidDataException("The owner returned no required preparation plan.");
            if (!plan.Succeeded)
            {
                return new(false, [], plan.Diagnostics);
            }
            await view.RevalidateAsync(cancellationToken);
            NativeTemplateProjectPreparationPlan frozen = capture.Owner.Freeze(capture, plan);
            NativeProjectPreparationPreview preview = new(true, [], frozen.Diagnostics) { OwnedPlan = frozen };
            _previews.Add(preview, capture.Owner);
            return preview;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, [], [exception.Message]);
        }
    }

    public async Task<NativeProjectPreparationResult> CompleteAsync(
        NativeProjectPreparationContext context, NativeProjectPreparationPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preview);
        bool completing = false;
        try
        {
            if (!OwnsPreview(context, preview) || !_previews.Remove(preview, out BoundNativeProjectPreparation? binding))
            {
                throw new InvalidDataException("Preparation completion has no matching one-use source-qualified preview.");
            }
            NativeTemplateProjectPreparationPlan plan = binding.BeginCompletion(context, preview.OwnedPlan!);
            cancellationToken.ThrowIfCancellationRequested();
            await _candidate.RevalidateDeclarationAsync(cancellationToken);
            NativeProjectPreparationCapture capture = context.Created!.ProjectPreparationCapture!;
            await capture.ValidateInstalledAsync(_files, cancellationToken);
            using NativeTemplatePolicyLifetime lifetime = new();
            WorkloadNativeCreatedProject created = new(capture, lifetime, _files);
            await created.RevalidateAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            completing = true;
            NativeTemplateProjectPreparationResult returned = await binding.ExecuteAsync(created, plan, cancellationToken)
                ?? throw new InvalidDataException("The owning workload returned no required preparation completion.");
            NativeTemplateProjectPreparationResult copied = new(returned.Succeeded, returned.Diagnostics, returned.TargetMayHaveChanged);
            return new(copied.Succeeded, copied.Diagnostics) { OwnedCompletion = copied };
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, [exception.Message])
            {
                OwnedCompletion = completing ? new(false, [exception.Message], true) : null,
            };
        }
    }
}
