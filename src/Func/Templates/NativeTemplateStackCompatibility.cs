// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Gates template generation on the captured stack's native-template capability, without acquiring or upgrading workloads.
/// </summary>
internal sealed class NativeTemplateStackCompatibility(NativeTemplateWorkloadCapture capture, InstalledProjectStackCatalog stacks)
{
    private readonly NativeTemplateWorkloadCapture _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    private readonly InstalledProjectStackCatalog _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));

    public TemplateConstraintEvaluationSnapshot ForStack(TemplateConstraintEvaluationSnapshot availability, string stack)
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        IProjectStack selected = _stacks.FindStack(stack)
            ?? throw new InvalidOperationException("Template asset scoping requires the selected installed stack.");
        NativeTemplateWorkloadRegistration origin = _capture.GetStackOrigin(selected)
            ?? throw new InvalidOperationException("Template asset scoping requires the selected stack's captured physical owner.");
        return new(availability.Workloads)
        {
            PackageScope = availability.PackageScope?.ForPhysicalOwner(origin.PackageId, origin.PackageVersion),
        };
    }

    public string? GetDiagnostic(string stack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        IProjectStack? installed = _stacks.FindStack(stack);
        NativeTemplateWorkloadRegistration? origin = installed is null ? null : _capture.GetStackOrigin(installed);
        if (origin is { BundledTemplatesDeclared: true, Failure: null, TemplateDiagnostic: null })
        {
            return null;
        }

        string package = origin?.LogicalPackageId ?? origin?.PackageId ?? stack;
        if (origin?.TemplateDiagnostic is { } diagnostic)
        {
            return diagnostic + " " + RecoveryHint(stack);
        }
        return $"Stack '{stack}' does not supply compatible native templates. Run 'func workload update {package}' "
            + "(add --major to permit a major-version update or --source <v3-feed-url> to select a compatible package source), then retry. "
            + "No project files were changed; no workload was upgraded automatically.";
    }

    public string RecoveryHint(string stack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        IProjectStack? installed = _stacks.FindStack(stack);
        NativeTemplateWorkloadRegistration? origin = installed is null ? null : _capture.GetStackOrigin(installed);
        return $"Run 'func setup --features {stack} --install-policy if-needed' to explicitly repair retained bundled-template registration, "
            + $"or 'func workload update {origin?.LogicalPackageId ?? origin?.PackageId ?? stack}' to install a compatible version. No templates are acquired automatically.";
    }
}
