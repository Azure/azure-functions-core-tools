// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Binds an eligible selected template to invocation within its owning templater's live session.
/// </summary>
internal sealed class ResolvedTemplate
{
    private readonly ISelectedTemplateInvoker _invoker;

    internal ResolvedTemplate(TemplateCatalogEntry entry, ISelectedTemplateInvoker invoker,
        string? configurationSha256 = null, string? configurationSourceIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        if (entry.ConstraintEvaluation.Status != TemplateConstraintEvaluationStatus.Eligible
            || entry.MetadataDiagnostic is not null || entry.ProjectConfigurationDiagnostic is not null)
        {
            throw new ArgumentException("Only a validated eligible template can be resolved.", nameof(entry));
        }

        Entry = entry;
        ConfigurationSha256 = configurationSha256;
        ConfigurationSourceIdentity = configurationSourceIdentity;
    }

    public TemplateCatalogEntry Entry { get; }

    public IReadOnlyList<TemplateParameterDefinition> Parameters => Entry.Parameters;

    public string? ConfigurationSha256 { get; }

    public string? ConfigurationSourceIdentity { get; }

    internal Task<ReadOnlyMemory<byte>> ReadPackageFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        cancellationToken.ThrowIfCancellationRequested();
        if (Path.IsPathRooted(relativePath) || relativePath.Replace('\\', '/').Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new ArgumentException("Supply an exact package-relative source path without traversal.", nameof(relativePath));
        }

        return _invoker.ReadPackageFileAsync(relativePath, cancellationToken);
    }

    internal Task<TemplateInvocationResult?> ValidateConfigurationAsync(CancellationToken cancellationToken)
        => _invoker.ValidateConfigurationAsync(cancellationToken);

    internal Task<NativeTemplatePreparationResult> CaptureNativePreparationAsync(TemplateInvocationRequest request,
        bool expandFiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Parameters.Keys.Any(key => !Parameters.Any(parameter => parameter.CanonicalName == key)))
        {
            return Task.FromResult(new NativeTemplatePreparationResult(null,
                new(TemplateInvocationStatus.InvalidArguments, [], "Preparation values must use declared canonical parameter names.")));
        }

        return _invoker.CaptureAsync(request with { Parameters = new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal) },
            expandFiles, cancellationToken);
    }

    public Task<TemplateInvocationResult> InvokeAsync(TemplateInvocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OutputDirectory);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Parameters.Keys.Any(key => !Parameters.Any(parameter => string.Equals(parameter.CanonicalName, key, StringComparison.Ordinal))))
        {
            return Task.FromResult(new TemplateInvocationResult(TemplateInvocationStatus.InvalidArguments, [], "Invocation values must use declared canonical parameter names."));
        }

        request = request with { Parameters = new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal) };
        return _invoker.InvokeAsync(request, cancellationToken);
    }
}