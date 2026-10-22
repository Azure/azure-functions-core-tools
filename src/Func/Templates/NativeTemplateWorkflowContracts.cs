// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal interface INativeTemplateAvailability
{
    public Task<TemplateConstraintEvaluationSnapshot> CaptureAsync(CancellationToken cancellationToken);
}

internal sealed record NativeTemplateInvocationPreparation(TemplateInvocationRequest? Request, IReadOnlyList<string> Diagnostics)
{
    public static NativeTemplateInvocationPreparation Ready(TemplateInvocationRequest request)
        => new(request ?? throw new ArgumentNullException(nameof(request)), []);

    public static NativeTemplateInvocationPreparation Reject(params string[] diagnostics) => new(null, Array.AsReadOnly(diagnostics));
}

internal sealed record NativeItemWorkflowContext(Templater Templater, ResolvedTemplate SelectedTemplate,
    TemplateCandidateParseResult Arguments, NewExecutionRequest Command, TemplateInvocationRequest Invocation);

internal sealed record NativeProjectWorkflowContext(Templater Templater, ResolvedTemplate SelectedTemplate,
    TemplateCandidateParseResult Arguments, InitExecutionRequest Command, TemplateInvocationRequest Invocation);

internal sealed record NativeItemHelpContext(ResolvedTemplate SelectedTemplate, TemplateEngineContext Context);

internal sealed record NativeItemHelpProjection(
    IReadOnlyList<TemplateParameterDefinition> Parameters, IReadOnlyList<string> Diagnostics);

/// <summary>
/// Identifies a mounted project candidate before layout qualification without creating another template session.
/// </summary>
internal sealed record NativeProjectTemplateSource(TemplateCatalogEntry Entry, TemplateConfigurationSnapshot Configuration,
    string ConfigurationSourceIdentity, Func<string, CancellationToken, Task<ReadOnlyMemory<byte>>> ReadPackageFileAsync)
{
    internal string? PackageMountPoint { get; init; }

    internal Func<CancellationToken, Task<TemplateInvocationResult?>>? RevalidateAsync { get; init; }

    internal object? SessionIdentity { get; init; }
}

/// <summary>
/// Supplies CLI-owned item compatibility and selected-source invocation preparation for one installed stack.
/// </summary>
internal interface INativeItemWorkflowProvider
{
    public string Stack { get; }

    public INativeItemCompatibility? Compatibility => null;

    public INativeSelectedItemEffects? Effects => null;

    public Task<NativeItemHelpProjection> DescribeHelpAsync(NativeItemHelpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new NativeItemHelpProjection(context.SelectedTemplate.Parameters, []));
    }

    public Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeItemWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NativeTemplateInvocationPreparation.Ready(context.Invocation));
    }
}

/// <summary>
/// Supplies source-qualified candidate layout and selected project preparation without invoking a legacy initializer.
/// </summary>
internal interface INativeProjectWorkflowProvider
{
    public string Stack { get; }

    public bool Handles(TemplateCatalogEntry entry);

    public INativeProjectPreparation? Preparation => null;

    public Task<NativeProjectLayoutQualification?> QualifyLayoutAsync(NativeProjectTemplateSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<NativeProjectLayoutQualification?>(null);
    }

    public Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeProjectWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(NativeTemplateInvocationPreparation.Ready(context.Invocation));
    }
}
