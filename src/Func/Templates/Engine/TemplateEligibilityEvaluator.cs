// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Abstractions.Mount;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Utils;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Evaluates candidate metadata and source-backed constraints for one command-scoped engine session.
/// </summary>
internal sealed class TemplateEligibilityEvaluator(
    TemplateEngineSession session,
    FuncTemplateConstraintEvaluator funcConstraintEvaluator)
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly FuncTemplateConstraintEvaluator _funcConstraintEvaluator =
        funcConstraintEvaluator ?? throw new ArgumentNullException(nameof(funcConstraintEvaluator));

    public async Task<TemplateEligibilityResult> EvaluateAsync(
        ITemplateInfo candidate,
        TemplateCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(entry);
        if (!string.Equals(candidate.Identity, entry.Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The catalog entry must describe the supplied engine candidate.", nameof(entry));
        }

        _session.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (entry.MetadataDiagnostic is { } metadataDiagnostic)
        {
            return Rejected(entry, "invalid-metadata", metadataDiagnostic, entry.MetadataDiagnosticCause);
        }

        if (entry.IsHidden)
        {
            return Rejected(entry, "hidden", $"Template '{entry.Identity}' is hidden and cannot be selected.");
        }

        return await EvaluateConstraintsAsync(candidate, entry, cancellationToken);
    }

    private async Task<TemplateEligibilityResult> EvaluateConstraintsAsync(
        ITemplateInfo candidate,
        TemplateCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        if (!_session.Settings.TryGetMountPoint(candidate.MountPointUri, out IMountPoint? mountPoint) || mountPoint is null)
        {
            return Rejected(entry, "constraint-source-unavailable", $"Template '{entry.Identity}' source is unavailable for constraint validation.");
        }

        using (mountPoint)
        {
            IFile? configFile = mountPoint.FileInfo(candidate.ConfigPlace);
            if (configFile is null || !configFile.Exists)
            {
                return Rejected(entry, "constraint-source-unavailable", $"Template '{entry.Identity}' configuration is missing.");
            }

            string configuration;
            try
            {
                using Stream stream = configFile.OpenRead();
                using StreamReader reader = new(stream);
                configuration = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return Rejected(
                    entry,
                    "constraint-source-unavailable",
                    $"Template '{entry.Identity}' configuration could not be read: {exception.Message}",
                    exception);
            }

            IReadOnlyList<TemplateConstraintDeclaration> declarations;
            bool hasAdditionalConfiguration;
            try
            {
                declarations = TemplateConstraintDeclarationReader.Read(configuration, out hasAdditionalConfiguration);
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return Rejected(entry, "invalid-constraints", exception.Message, exception);
            }

            if (hasAdditionalConfiguration)
            {
                return Rejected(
                    entry,
                    "additional-configuration-not-evaluated",
                    $"Template '{entry.Identity}' uses additional configuration files whose merged constraints cannot be verified.");
            }

            if (declarations.Count != candidate.Constraints.Count
                || declarations.Where((declaration, index) =>
                    !string.Equals(declaration.Type, candidate.Constraints[index].Type, StringComparison.Ordinal)).Any())
            {
                return Rejected(
                    entry,
                    "constraint-metadata-mismatch",
                    $"Template '{entry.Identity}' has constraint metadata the engine did not preserve.");
            }

            if (declarations.Count == 0)
            {
                return TemplateEligibilityResult.Eligible;
            }

            for (int index = 0; index < declarations.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TemplateConstraintDeclaration declaration = declarations[index];
                if (declaration.Type == "func-workload")
                {
                    return Rejected(
                        entry,
                        "constraint-not-evaluated",
                        $"Template '{entry.Identity}' requires workload constraint '{declaration.Label}', but this context has no workload inventory.");
                }

                if (declaration.Type == "func-bundle")
                {
                    NuGetVersion? bundleVersion = NuGetVersion.TryParse(_session.Context.Bundle?.Version, out NuGetVersion? parsedBundleVersion)
                        ? parsedBundleVersion
                        : null;
                    FuncTemplateConstraintOutcome outcome = _funcConstraintEvaluator.Evaluate(
                        declaration,
                        [],
                        _session.Context.Bundle?.Id,
                        bundleVersion);
                    if (outcome != FuncTemplateConstraintOutcome.Eligible)
                    {
                        return Rejected(
                            entry,
                            outcome == FuncTemplateConstraintOutcome.Restricted ? "constraint-restricted" : "constraint-not-evaluated",
                            $"Template '{entry.Identity}' does not satisfy bundle constraint '{declaration.Label}'.");
                    }

                    continue;
                }

                TemplateConstraintInfo constraintInfo = candidate.Constraints[index];
                ITemplateConstraintFactory[] factories = [.. _session.Settings.Components
                    .OfType<ITemplateConstraintFactory>()
                    .Where(factory => string.Equals(factory.Type, constraintInfo.Type, StringComparison.Ordinal))];
                if (factories.Length != 1)
                {
                    return Rejected(
                        entry,
                        "constraint-not-evaluated",
                        $"Template '{entry.Identity}' uses unsupported constraint '{constraintInfo.Type}'.");
                }

                TemplateConstraintManager constraintManager = _session.ConstraintManager;
                TemplateConstraintResult result;
                try
                {
                    result = await constraintManager.EvaluateConstraintAsync(
                        constraintInfo.Type,
                        declaration.Arguments?.GetRawText(),
                        cancellationToken);
                }
                catch (InvalidOperationException exception) when (exception is not ObjectDisposedException)
                {
                    return Rejected(
                        entry,
                        "constraint-not-evaluated",
                        $"Template '{entry.Identity}' constraint '{constraintInfo.Type}' could not be evaluated: {exception.Message}",
                        exception);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (result.EvaluationStatus != TemplateConstraintResult.Status.Allowed)
                {
                    return Rejected(
                        entry,
                        result.EvaluationStatus == TemplateConstraintResult.Status.Restricted ? "constraint-restricted" : "constraint-not-evaluated",
                        result.LocalizedErrorMessage ?? $"Template '{entry.Identity}' constraint '{constraintInfo.Type}' could not be evaluated.");
                }
            }
        }

        return TemplateEligibilityResult.Eligible;
    }

    private static TemplateEligibilityResult Rejected(
        TemplateCatalogEntry entry,
        string code,
        string message,
        Exception? cause = null)
        => TemplateEligibilityResult.Rejected(
            entry,
            new(
                code,
                message,
                entry.Identity,
                entry.Package?.Identifier,
                cause,
                entry.Name,
                entry.Type));
}

/// <summary>
/// Candidate-scoped eligibility outcome; rejection always carries a diagnostic for that candidate.
/// </summary>
internal sealed class TemplateEligibilityResult
{
    private TemplateEligibilityResult(bool isEligible, IReadOnlyList<TemplateResolutionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if ((isEligible && diagnostics.Count != 0) || (!isEligible && diagnostics.Count != 1))
        {
            throw new ArgumentException("Eligibility requires no diagnostics and rejection requires exactly one.", nameof(diagnostics));
        }

        IsEligible = isEligible;
        Diagnostics = diagnostics;
    }

    public static TemplateEligibilityResult Eligible { get; } = new(true, []);

    public bool IsEligible { get; }

    public IReadOnlyList<TemplateResolutionDiagnostic> Diagnostics { get; }

    public static TemplateEligibilityResult Rejected(TemplateCatalogEntry candidate, TemplateResolutionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (!string.Equals(diagnostic.TemplateIdentity, candidate.Identity, StringComparison.Ordinal)
            || !string.Equals(diagnostic.PackageIdentity, candidate.Package?.Identifier, StringComparison.Ordinal))
        {
            throw new ArgumentException("The rejection diagnostic must identify its candidate.", nameof(diagnostic));
        }

        return new(false, [diagnostic]);
    }
}
