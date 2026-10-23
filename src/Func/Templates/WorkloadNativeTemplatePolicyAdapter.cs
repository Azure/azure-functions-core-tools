// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;

namespace Azure.Functions.Cli.Templates;

internal sealed class WorkloadNativeTemplatePolicyAdapter(
    NativeTemplatePolicyCandidate candidate, WorkloadNativeTemplatePolicies policies,
    IFileSystem files, IInteractionService interaction, ITemplateArgumentParser arguments)
    : INativeItemWorkflowProvider, INativeProjectWorkflowProvider, INativeItemCompatibility, INativeSelectedItemEffects
{
    private readonly NativeTemplatePolicyCandidate _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
    private readonly WorkloadNativeTemplatePolicies _policies = policies ?? throw new ArgumentNullException(nameof(policies));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly WorkloadNativeProjectPreparation? _preparation = candidate.RequiresPreparation
        ? new(candidate, files) : null;

    public string Stack => _candidate.Admission.Registration.Policy.Stack;
    public INativeItemCompatibility Compatibility => this;
    public INativeSelectedItemEffects Effects => this;
    public INativeProjectPreparation? Preparation => _preparation;

    public bool Handles(TemplateCatalogEntry entry)
        => entry.Identity == _candidate.Entry.Identity && entry.Package == _candidate.Entry.Package;

    public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeItemWorkflowContext context, CancellationToken cancellationToken)
        => await PrepareAsync(context.Invocation, context.Templater.Context.Project?.RootDirectory ?? context.Command.WorkingDirectory, cancellationToken);

    public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeProjectWorkflowContext context, CancellationToken cancellationToken)
        => await PrepareAsync(context.Invocation, context.Command.WorkingDirectory, cancellationToken);

    private async Task<NativeTemplateInvocationPreparation> PrepareAsync(
        TemplateInvocationRequest request, WorkingDirectory project, CancellationToken cancellationToken)
    {
        try
        {
            using NativeTemplatePolicySession session = new(_candidate, project, _arguments);
            TemplateInvocationRequest prepared = await session.PrepareAsync(request, cancellationToken);
            return NativeTemplateInvocationPreparation.Ready(prepared with { PolicyCandidate = _candidate });
        }
        catch (InvalidDataException exception)
        {
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
    }

    public async Task<NativeItemCompatibilityResult> ResolveAsync(ResolvedTemplate selected, TemplateCandidateParseResult arguments,
        string? requestedName, bool canPrompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            NativePolicyItemDescription description = await DescribeAsync(selected, cancellationToken);
            if (description.Compatibility is null)
            {
                return new(null, "unresolved", ["An internal helper cannot be invoked as an ordinary item."]);
            }
            NativeItemCompatibility compatibility = new(_interaction, _ => description.Compatibility, async (source, path, token) =>
            {
                TemplateInvocationResult? changed = await source.ValidateConfigurationAsync(token);
                if (changed is not null)
                {
                    throw new InvalidDataException(changed.Diagnostic ?? "The selected source configuration changed.");
                }
                await _candidate.Admission.RevalidateAsync(token);
                return await source.ReadPackageFileAsync(path, token);
            });
            return await compatibility.ResolveAsync(selected, arguments, requestedName, canPrompt, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            return new(null, "unresolved", [exception.Message]);
        }
        catch (NotSupportedException exception)
        {
            return new(null, "unresolved", [exception.Message]);
        }
    }

    public async Task<NativeSelectedItemBinding> ResolveAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return (await DescribeAsync(selected, cancellationToken)).Binding;
        }
        catch (InvalidDataException exception)
        {
            return new(false, null, [exception.Message]);
        }
        catch (NotSupportedException exception)
        {
            return new(false, null, [exception.Message]);
        }
    }

    public async Task<TemplateInvocationResult?> InvokeAsync(Templater templater, ResolvedTemplate selected,
        NativeSelectedItemBinding binding, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        NativeSelectedItemBinding current = await ResolveAsync(selected, cancellationToken);
        if (current.InternalExpansionOnly || current.Diagnostics.Count > 0 || current.Append != binding.Append
            || binding.InternalExpansionOnly || binding.Diagnostics.Count > 0)
        {
            return new(TemplateInvocationStatus.EffectFailure, [], string.Join(" ", current.Diagnostics.Count > 0
                ? current.Diagnostics : ["The source-qualified item policy changed. Nothing was generated."]));
        }
        if (current.Append is null)
        {
            return null;
        }
        using NativeTemplatePolicyLifetime lifetime = new();
        WorkloadNativeTemplateSource source = _candidate.CreateSource(lifetime);
        await source.RevalidateAsync(cancellationToken);
        INativeTemplateAppendCapability capability = _candidate.Admission.Registration.Policy
            .CreateAppendCapability(source, request.OutputDirectory)
            ?? throw new InvalidDataException("The workload returned no required append capability.");
        WorkloadNativeAppendCapability adapter = new(capability, templater.Context.Project?.RootDirectory ?? request.OutputDirectory, source);
        NativeSelectedItemAppendSupport support = new(_files, adapter, async (helper, token) =>
        {
            WorkloadNativeTemplatePolicyAdapter provider = await _policies.FindAsync(helper, token)
                ?? throw new InvalidDataException("The internal helper has no bound owning workload policy.");
            if (!ReferenceEquals(provider._candidate.Admission.Registration, _candidate.Admission.Registration))
            {
                throw new InvalidDataException("The helper policy belongs to another runtime owner.");
            }
            return await provider.ResolveAsync(helper, token);
        }, confirmed: request.PolicyEffectsConfirmed);
        return await support.InvokeAsync(templater, selected, current, request, cancellationToken);
    }

    private async Task<NativePolicyItemDescription> DescribeAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        if (!Handles(selected.Entry))
        {
            throw new InvalidDataException("The item differs from its selected policy source.");
        }
        using NativeTemplatePolicyLifetime lifetime = new();
        WorkloadNativeTemplateSource source = _candidate.CreateSource(lifetime);
        await source.RevalidateAsync(cancellationToken);
        NativeItemPolicyDescription description = await _candidate.Admission.Registration.Policy.DescribeItemAsync(source, cancellationToken)
            ?? throw new InvalidDataException("The workload returned no item policy.");
        NativeTemplatePolicyCandidate.RequireDiagnostics(description.Diagnostics);
        if (description.ParameterValidation is null)
        {
            throw new InvalidDataException("The workload returned no canonical parameter validation collection.");
        }
        NativeItemAppendMapping? mapping = description.Append is not { } append ? null
            : new(selected.Entry.Package!, selected.ConfigurationSha256!, Stamp(append.MappingSource), Stamp(append.CreateSource),
                append.CreateTargetFileName, append.AppendTargetFileName, append.HelperIdentity, append.HelperConfigurationSha256,
                Stamp(append.HelperSource), append.ExpectedHelperOutput);
        NativeTemplateLegacyContract? compatibility = description.InternalExpansionOnly ? null
            : description.ValidationSource is { } validation
                ? new(Stamp(validation), selected.Entry.Package, description.DefaultName, description.FunctionNamePrompt, null,
                    Array.AsReadOnly<NativeTemplateValidationRule>([.. description.ParameterValidation.Select(prompt =>
                        new NativeTemplateValidationRule(prompt.Id, prompt with
                        {
                            Choices = prompt.Choices is null ? null : Array.AsReadOnly<string>([.. prompt.Choices]),
                        }))]))
                : throw new InvalidDataException("The ordinary item policy has no source-qualified validation contract.");
        await source.RevalidateAsync(cancellationToken);
        return new(new(description.InternalExpansionOnly, description.InternalExpansionOnly ? null : mapping, []), compatibility);
    }

    private static TemplateEffectSource Stamp(NativeTemplateSourceStamp source)
        => new(source.TemplateIdentity, source.RelativePath, source.Sha256);

    private sealed record NativePolicyItemDescription(NativeSelectedItemBinding Binding, NativeTemplateLegacyContract? Compatibility);
}

internal sealed class WorkloadNativeAppendCapability(
    INativeTemplateAppendCapability capability, WorkingDirectory output, WorkloadNativeTemplateSource source) : INativeTemplateEffectCapability
{
    private readonly INativeTemplateAppendCapability _capability = capability ?? throw new ArgumentNullException(nameof(capability));
    private readonly WorkingDirectory _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly WorkloadNativeTemplateSource _source = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
    {
        await _source.RevalidateAsync(cancellationToken);
        if (plan.Effects.Count != 1 || plan.Effects[0] is not TemplateAppendFileEffect append)
        {
            return ["The bound append capability supports exactly one typed append."];
        }
        return await _capability.ValidateAsync(Request(plan.OutputDirectory, append), cancellationToken);
    }

    public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
    {
        await _source.RevalidateAsync(cancellationToken);
        if (effect is not TemplateAppendFileEffect append)
        {
            return new(false, ["The bound append capability does not support this effect."]);
        }
        NativeTemplateAppendResult result = await _capability.ExecuteAsync(Request(_output, append), cancellationToken);
        return new(result.Succeeded, Array.AsReadOnly<string>([.. result.Diagnostics]), result.TargetMayHaveChanged);
    }

    private static NativeTemplateAppendRequest Request(WorkingDirectory output, TemplateAppendFileEffect effect)
        => new(output, new(effect.Source.TemplateIdentity, effect.Source.Identity, effect.Source.Sha256),
            effect.TargetPath, effect.Content, effect.ExpectedTargetSha256);
}
