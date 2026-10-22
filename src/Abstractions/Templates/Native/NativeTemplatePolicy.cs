// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Native;

/// <summary>
/// Qualifies native template language policy contributed by its owning runtime workload.
/// </summary>
public abstract class NativeTemplatePolicy
{
    public abstract string Stack { get; }

    public abstract bool Handles(NativeTemplateSource selected);

    public abstract Task<NativeItemPolicyDescription> DescribeItemAsync(NativeTemplateSource selected, CancellationToken cancellationToken);

    public abstract Task<IReadOnlyList<string>> ValidateProjectAsync(NativeTemplateSource selected, CancellationToken cancellationToken);

    public abstract INativeTemplateAppendCapability CreateAppendCapability(
        NativeTemplateSource selected, WorkingDirectory invocationOutputDirectory);

    public virtual NativeTemplatePolicyStages GetRequestedStages(NativeTemplateSource selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return NativeTemplatePolicyStages.None;
    }

    public virtual Task<NativeTemplateProjectAnchor> DescribeProjectLayoutAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        throw Unsupported("source-qualified project layout declarations");
    }

    public virtual Task<NativeTemplateInvocationQualification> QualifyInvocationAsync(
        NativeTemplateInvocationContext invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        throw Unsupported("native invocation qualification");
    }

    public virtual Task<NativeTemplateCaptureQualification> QualifyCapturedOutputAsync(
        NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);
        cancellationToken.ThrowIfCancellationRequested();
        throw Unsupported("captured native layout qualification");
    }

    public virtual Task<IReadOnlyList<string>> QualifyPackageReferenceAsync(
        NativeTemplateCapturedOutput captured, NativeTemplateSourceStamp source, string projectPath,
        string packageId, string version, string projectFileExtension, bool continueOnError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        throw Unsupported("package-reference qualification");
    }

    public virtual Task<IReadOnlyList<string>> QualifyRestoreAsync(
        NativeTemplateCapturedOutput captured, NativeTemplateSourceStamp source, string projectPath,
        bool continueOnError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        throw Unsupported("restore qualification");
    }

    public virtual Task<IReadOnlyList<string>> QualifyCompileRepairAsync(
        NativeTemplateCapturedOutput captured, NativeTemplateSourceStamp source,
        NativeTemplateCompileRepair repair, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(repair);
        cancellationToken.ThrowIfCancellationRequested();
        throw Unsupported("compile-order qualification");
    }

    public virtual Task<IReadOnlyList<string>> QualifyCreateFileAsync(
        NativeTemplateCapturedOutput captured, NativeTemplateSourceStamp source, string targetPath,
        string generatedContent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        throw Unsupported("create-only content qualification");
    }

    private static NotSupportedException Unsupported(string role)
        => new($"This workload policy does not support {role}. Update the owning workload.");
}
