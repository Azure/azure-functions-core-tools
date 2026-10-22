// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads.Python;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledPythonTemplateTests
{
    private static PythonTemplatePolicy CreatePythonPolicy() => new(new(), new PythonTemplateAppendExecutor(), new());

    private static PythonPolicyTestAdapter CreatePythonProvider(bool confirmed = true, IInteractionService? interaction = null)
        => new(CreatePythonPolicy(), interaction ?? Substitute.For<IInteractionService>(), confirmed);

    private static BoundNativeTemplatePolicy BindPythonTestSource(
        TemplateCatalogEntry entry, string mountPoint, WorkloadNativeTemplatePolicyRegistration owner)
        // Source-level tests use an explicit fixture receipt; boot tests exercise physical workload admission.
        => new(owner, entry, mountPoint, required: true, "fixture", token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult("fixture");
        });

    private static WorkloadNativeTemplateSource CreatePythonPolicySource(ResolvedTemplate selected)
    {
        var owner = new WorkloadNativeTemplatePolicyRegistration(TestWorkloads.CreateInfo("Python.Policy.Fixture"), CreatePythonPolicy());
        return new(selected.Entry, selected.ConfigurationSha256!, new(), selected.ReadPackageFileAsync,
            selected.ValidateConfigurationAsync, BindPythonTestSource(selected.Entry, selected.PackageMountPoint!, owner));
    }

    private static INativeTemplateEffectCapability CreatePythonAppendCapability(
        ResolvedTemplate selected, WorkingDirectory output, IPythonTemplateAppendExecutor? executor = null, PythonAppendTargetGuard? guard = null)
    {
        var source = CreatePythonPolicySource(selected);
        return new WorkloadNativeAppendCapability(new PythonAppendCapability(new(), executor ?? new PythonTemplateAppendExecutor(),
            source, output, guard ?? new()), output, source);
    }

    private static TemplateEffectSource EffectSource(NativeTemplateSourceStamp source)
        => new(source.TemplateIdentity, source.RelativePath, source.Sha256);

    private static NativeItemAppendMapping? ItemMapping(ResolvedTemplate selected, NativeTemplateAppendMapping? mapping)
        => mapping is null ? null : new(selected.Entry.Package!, selected.ConfigurationSha256!,
            EffectSource(mapping.MappingSource), EffectSource(mapping.CreateSource), mapping.CreateTargetFileName, mapping.AppendTargetFileName,
            mapping.HelperIdentity, mapping.HelperConfigurationSha256, EffectSource(mapping.HelperSource), mapping.ExpectedHelperOutput);

    private sealed record PythonPolicyTestSource(
        NativeSelectedItemBinding Binding, NativeTemplateLegacyContract? Compatibility, TemplateEffectSource HelperSource);

    private sealed class PythonPolicyTestReader
    {
        private readonly PythonTemplateSourceReader _sources = new();

        public async Task<PythonPolicyTestSource> ReadAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
        {
            var source = await _sources.ReadAsync(CreatePythonPolicySource(selected), cancellationToken);
            var description = source.Description;
            NativeTemplateLegacyContract? compatibility = description.ValidationSource is not { } validation ? null
                : new(EffectSource(validation), selected.Entry.Package, description.DefaultName, description.FunctionNamePrompt, null,
                    [.. description.ParameterValidation.Select(prompt => new NativeTemplateValidationRule(prompt.Id, prompt))]);
            return new(new(description.InternalExpansionOnly, ItemMapping(selected, description.Append), description.Diagnostics),
                compatibility, EffectSource(source.HelperSource));
        }
    }

    private sealed class PythonPolicyTestAdapter(PythonTemplatePolicy policy, IInteractionService interaction, bool confirmed)
        : INativeItemWorkflowProvider, INativeProjectWorkflowProvider, INativeItemCompatibility, INativeSelectedItemEffects
    {
        private readonly PythonTemplatePolicy _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        private readonly WorkloadNativeTemplatePolicyRegistration _owner = new(TestWorkloads.CreateInfo("Python.Policy.Fixture"), policy);

        public string Stack => _policy.Stack;
        public INativeItemCompatibility Compatibility => this;
        public INativeSelectedItemEffects Effects => this;

        public bool Handles(TemplateCatalogEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            return entry.Package is not null && _policy.Handles(new WorkloadNativeTemplateSource(entry, new('A', 64), new(),
                (_, _) => throw new InvalidOperationException("This metadata-only fixture has no readable source."),
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<TemplateInvocationResult?>(null);
                }, BindPythonTestSource(entry, "fixture", _owner)));
        }

        public async Task<NativeProjectLayoutQualification?> QualifyLayoutAsync(NativeProjectTemplateSource source, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(source);
            var native = new WorkloadNativeTemplateSource(source.Entry, source.Configuration.Fingerprint, new(), source.ReadPackageFileAsync,
                source.RevalidateAsync ?? (token =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<TemplateInvocationResult?>(null);
                }), BindPythonTestSource(source.Entry, source.PackageMountPoint ?? "fixture", _owner));
            try
            {
                await _policy.ValidateProjectAsync(native, cancellationToken);
                return null;
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidTemplateMetadataException(exception.Message, exception);
            }
        }

        public Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeItemWorkflowContext context, CancellationToken cancellationToken)
            => Adapter(context.SelectedTemplate).PrepareAsync(context, cancellationToken);

        public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeProjectWorkflowContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            var candidate = Candidate(context.SelectedTemplate);
            try
            {
                await candidate.QualifySourceAsync(requireProjectLayout: false, cancellationToken);
                return await Adapter(candidate).PrepareAsync(context, cancellationToken);
            }
            catch (InvalidDataException exception)
            {
                return NativeTemplateInvocationPreparation.Reject(exception.Message);
            }
        }

        public Task<NativeItemCompatibilityResult> ResolveAsync(ResolvedTemplate selected, TemplateCandidateParseResult arguments,
            string? requestedName, bool canPrompt, CancellationToken cancellationToken)
            => Adapter(selected).ResolveAsync(selected, arguments, requestedName, canPrompt, cancellationToken);

        public Task<NativeSelectedItemBinding> ResolveAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
            => Adapter(selected).ResolveAsync(selected, cancellationToken);

        public Task<TemplateInvocationResult?> InvokeAsync(Templater templater, ResolvedTemplate selected,
            NativeSelectedItemBinding binding, TemplateInvocationRequest request, CancellationToken cancellationToken)
            => Adapter(selected).InvokeAsync(templater, selected, binding, request with { PolicyEffectsConfirmed = confirmed }, cancellationToken);

        private NativeTemplatePolicyCandidate Candidate(ResolvedTemplate selected)
            => new(BindPythonTestSource(selected.Entry, selected.PackageMountPoint!, _owner), selected.Entry, selected.ConfigurationSha256!,
                selected.ConfigurationSourceIdentity!, selected.ReadPackageFileAsync, selected.ValidateConfigurationAsync,
                selected.Configuration!.PackageConfigurationPath!);

        private WorkloadNativeTemplatePolicyAdapter Adapter(ResolvedTemplate selected) => Adapter(Candidate(selected));

        private WorkloadNativeTemplatePolicyAdapter Adapter(NativeTemplatePolicyCandidate candidate)
            => new(candidate, FindAsync, new PhysicalFileSystem(), _interaction, new TemplateArgumentParser());

        private Task<WorkloadNativeTemplatePolicyAdapter?> FindAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<WorkloadNativeTemplatePolicyAdapter?>(Adapter(selected));
        }
    }
}
