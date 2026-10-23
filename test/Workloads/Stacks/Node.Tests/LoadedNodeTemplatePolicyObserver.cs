// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Native;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

internal sealed class LoadedNodeTemplatePolicyObserver(
    NativeTemplatePolicy inner, Action preview, Action complete, Action validateProject) : NativeTemplatePolicy
{
    private readonly NativeTemplatePolicy _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Action _preview = preview ?? throw new ArgumentNullException(nameof(preview));
    private readonly Action _complete = complete ?? throw new ArgumentNullException(nameof(complete));
    private readonly Action _validateProject = validateProject ?? throw new ArgumentNullException(nameof(validateProject));

    public override string Stack => _inner.Stack;

    public override bool Handles(NativeTemplateSource selected) => _inner.Handles(selected);

    public override NativeTemplatePolicyStages GetRequestedStages(NativeTemplateSource selected) => _inner.GetRequestedStages(selected);

    public override bool RequiresProjectPreparation(NativeTemplateSource selected) => _inner.RequiresProjectPreparation(selected);

    public override Task<NativeItemPolicyDescription> DescribeItemAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
        => _inner.DescribeItemAsync(selected, cancellationToken);

    public override Task<IReadOnlyList<string>> ValidateProjectAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        _validateProject();
        return _inner.ValidateProjectAsync(selected, cancellationToken);
    }

    public override Task<NativeTemplateProjectAnchor> DescribeProjectLayoutAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
        => _inner.DescribeProjectLayoutAsync(selected, cancellationToken);

    public override Task<NativeTemplateInvocationQualification> QualifyInvocationAsync(
        NativeTemplateInvocationContext invocation, CancellationToken cancellationToken)
        => _inner.QualifyInvocationAsync(invocation, cancellationToken);

    public override Task<NativeTemplateCaptureQualification> QualifyCapturedOutputAsync(
        NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
        => _inner.QualifyCapturedOutputAsync(captured, cancellationToken);

    public override INativeTemplateAppendCapability CreateAppendCapability(
        NativeTemplateSource selected, WorkingDirectory invocationOutputDirectory)
        => _inner.CreateAppendCapability(selected, invocationOutputDirectory);

    public override INativeTemplateProjectPreparation CreateProjectPreparation(NativeTemplateSource selected)
        => new ObservedPreparation(_inner.CreateProjectPreparation(selected), _preview, _complete);

    private sealed class ObservedPreparation(INativeTemplateProjectPreparation inner, Action preview, Action complete)
        : INativeTemplateProjectPreparation
    {
        private readonly INativeTemplateProjectPreparation _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        private readonly Action _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        private readonly Action _complete = complete ?? throw new ArgumentNullException(nameof(complete));

        public Task<NativeTemplateProjectPreparationPlan> PreviewAsync(NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
        {
            _preview();
            return _inner.PreviewAsync(captured, cancellationToken);
        }

        public Task<NativeTemplateProjectPreparationResult> CompleteAsync(
            NativeTemplateCreatedProject created, NativeTemplateProjectPreparationPlan preview, CancellationToken cancellationToken)
        {
            _complete();
            return _inner.CompleteAsync(created, preview, cancellationToken);
        }
    }
}
