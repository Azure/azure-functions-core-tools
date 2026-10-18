// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.DotNet.Shared;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Composes selected native .NET templates with source-qualified CLI-owned preparation and dependency helpers.
/// </summary>
internal sealed class DotNetTemplateWorkflowProvider(DotNetTemplateSources sources, DotNetInvocationProjectResolver projects,
    IFileSystem files, FSharpProjectFiles paths, DotNetTemplateProjectPreparation preparation,
    DotNetTemplateDependencyExecutor dependencies, FSharpCompileUpdater updater) : INativeItemWorkflowProvider, INativeProjectWorkflowProvider
{
    private readonly DotNetTemplateSources _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly DotNetInvocationProjectResolver _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly FSharpProjectFiles _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly DotNetTemplateProjectPreparation _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
    private readonly DotNetTemplateDependencyExecutor _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    private readonly FSharpCompileUpdater _updater = updater ?? throw new ArgumentNullException(nameof(updater));

    public string Stack => "dotnet";

    public async Task<NativeItemHelpProjection> DescribeHelpAsync(NativeItemHelpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ResolvedTemplate selected = context.SelectedTemplate;
        if (!_sources.Matches(selected.Entry, selected.ConfigurationSha256) || selected.Entry.Language != context.Context.Project?.Language
            || await selected.ValidateConfigurationAsync(cancellationToken) is not null)
        {
            return Unevaluated("The selected .NET item source is unqualified or changed; contextual defaults were not evaluated.");
        }

        try
        {
            DotNetInvocationProject project = await _projects.ResolveAsync(context.Context, cancellationToken);
            return new(Array.AsReadOnly<TemplateParameterDefinition>([.. selected.Parameters.Select(parameter => parameter.CanonicalName switch
            {
                "namespace" => parameter with
                {
                    DefaultValue = project.Properties.RootNamespace,
                    Description = "Namespace for generated code. The default is evaluated from the selected .NET project.",
                },
                "Framework" => parameter with { DefaultValue = DotNetTemplateFrameworks.Default },
                _ => parameter,
            })]), []);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or DotnetCliException)
        {
            return Unevaluated($"The selected .NET project defaults could not be evaluated: {exception.Message}");
        }

        NativeItemHelpProjection Unevaluated(string diagnostic) => new(
            Array.AsReadOnly<TemplateParameterDefinition>([.. selected.Parameters.Select(parameter =>
                parameter.CanonicalName is "namespace" or "Framework" ? parameter with
                {
                    DefaultValue = null,
                    Description = "The contextual default is unevaluated. Supply an explicit value or repair the selected .NET project and retry help.",
                } : parameter)]), [diagnostic]);
    }

    public bool Handles(TemplateCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Type == TemplateType.Project && entry.Package?.Identifier.Equals(
            "Azure.Functions.Templates.DotNet", StringComparison.OrdinalIgnoreCase) == true;
    }

    public Task<DotNetProjectLayoutQualification?> QualifyLayoutAsync(NativeProjectTemplateSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        DotNetProjectLayoutQualification? qualification = _sources.Matches(source.Entry, source.Configuration.Fingerprint)
            ? new(source.Entry.Identity, source.Configuration.Fingerprint) : null;
        return Task.FromResult(qualification);
    }

    public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeItemWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ResolvedTemplate selected = context.SelectedTemplate;
        if (!_sources.Matches(selected.Entry, selected.ConfigurationSha256))
        {
            return NativeTemplateInvocationPreparation.Reject("The selected .NET item configuration is not a pinned native source supported by this adapter.");
        }

        try
        {
            DotNetInvocationProject project = await _projects.ResolveAsync(context.Templater.Context, cancellationToken);
            if (selected.Entry.Language != context.Templater.Context.Project?.Language)
            {
                return NativeTemplateInvocationPreparation.Reject("The selected native item language does not match the trusted .NET project.");
            }

            string? name = context.Invocation.Name ?? selected.Entry.DefaultName;
            if (string.IsNullOrWhiteSpace(name))
            {
                return NativeTemplateInvocationPreparation.Reject("Supply --name; the selected .NET item has no usable authored default.");
            }

            Dictionary<string, string?> parameters = new(context.Invocation.Parameters, StringComparer.Ordinal);
            if (selected.Parameters.Any(parameter => parameter.CanonicalName == "namespace")
                && !context.Arguments.ExplicitParameterNames.Contains("namespace"))
            {
                parameters["namespace"] = project.Properties.RootNamespace;
            }
            NativeTemplateSourceSnapshot? auxiliarySources = await _sources.QualifyAuxiliaryAsync(selected, cancellationToken);

            DotNetTemplateEffectCapability capability = Capability(selected, project.Root, project.Properties.ProjectFilePath,
                project.ProjectSha256, createsProject: false);
            TemplateEffectSource source = Source(selected);
            TemplateInvocationRequest request = context.Invocation with
            {
                Name = name,
                Parameters = parameters,
                SourceSnapshot = DotNetTemplateSources.CombineAuxiliarySources(context.Invocation.SourceSnapshot, auxiliarySources),
                EffectSupport = new(project.Root, project.Properties.ProjectFilePath, capability, Confirmed: true),
                MsBuildBindings = new(project.Properties.ProjectFilePath, source.TemplateIdentity, source.Identity,
                    source.Sha256, project.Properties.TargetFramework, project.Properties.RootNamespace),
            };
            NativeTemplatePreparationResult captured = await selected.CaptureNativePreparationAsync(request, _files,
                expandFiles: selected.Entry.Language == "F#" || auxiliarySources is not null, cancellationToken);
            if (captured.Snapshot is not { } snapshot)
            {
                return NativeTemplateInvocationPreparation.Reject(captured.Failure?.Diagnostic ?? "Native .NET item preparation did not produce a selected output plan.");
            }

            if (auxiliarySources is not null && DotNetAuxiliaryNameValidation.Validate(selected.Entry.Identity, snapshot) is { } nameDiagnostic)
            {
                return NativeTemplateInvocationPreparation.Reject(nameDiagnostic);
            }
            NativeTemplateEffectPlanner planner = new();
            NativeTemplateEffectPlanningResult planned;
            if (selected.Entry.Language == "F#")
            {
                TemplateGeneratedFSharpFile[] generated = [.. snapshot.ExpandedFiles.Select(file => new TemplateGeneratedFSharpFile(
                    Path.GetFullPath(Path.Combine(request.OutputDirectory.Info.FullName, file.Key)),
                    Convert.ToHexString(SHA256.HashData(file.Value.Span))))];
                TemplateFSharpCompileEffect repair = new(snapshot.Source, project.Properties.ProjectFilePath,
                    project.ProjectSha256, project.CompileOrder, generated);
                request = request with { EffectSupport = request.EffectSupport! with { FSharpRepair = repair } };
                planned = planner.PlanFSharpItemDependencies(project.Root, snapshot.Source, snapshot.Effects,
                    request.OutputDirectory, project.Properties.ProjectFilePath, repair);
            }
            else
            {
                planned = planner.PlanDependencies(project.Root, snapshot.Source, snapshot.Effects.CreationResult, project.Properties.ProjectFilePath);
            }

            return await BindAsync(request, capability, planned, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or DotnetCliException)
        {
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
    }

    public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeProjectWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        ResolvedTemplate selected = context.SelectedTemplate;
        if (!_sources.Matches(selected.Entry, selected.ConfigurationSha256))
        {
            return NativeTemplateInvocationPreparation.Reject("The selected .NET project configuration is not a pinned native source supported by this adapter.");
        }

        Dictionary<string, string?> parameters = new(context.Invocation.Parameters, StringComparer.Ordinal);
        if (context.Arguments.ExplicitParameterNames.Contains("Framework") && parameters.TryGetValue("Framework", out string? framework)
            && framework is not null && framework != DotNetTemplateFrameworks.Default)
        {
            return NativeTemplateInvocationPreparation.Reject("The controlled .NET project workflow supports only net10.0.");
        }

        parameters["Framework"] = DotNetTemplateFrameworks.Default;
        TemplateInvocationRequest request = context.Invocation with
        {
            Parameters = parameters,
            Name = context.Invocation.Name ?? context.Invocation.OutputDirectory.Info.Name,
        };
        try
        {
            NativeTemplatePreparationResult captured = await selected.CaptureNativePreparationAsync(request, _files, expandFiles: true, cancellationToken);
            if (captured.Snapshot is not { } snapshot)
            {
                return NativeTemplateInvocationPreparation.Reject(captured.Failure?.Diagnostic ?? "Native .NET project preparation did not produce a selected output plan.");
            }

            string[] projects = [.. snapshot.Effects.CreationResult.PrimaryOutputs.Select(output => output.Path)];
            if (projects.Length != 1)
            {
                return NativeTemplateInvocationPreparation.Reject("Native project preparation must produce exactly one source-qualified project primary output.");
            }

            string project = Path.GetFullPath(Path.Combine(request.OutputDirectory.Info.FullName, projects[0]));
            KeyValuePair<string, ReadOnlyMemory<byte>>[] projectContent = [.. snapshot.ExpandedFiles.Where(file =>
                SamePath(Path.GetFullPath(Path.Combine(request.OutputDirectory.Info.FullName, file.Key)), project))];
            if (projectContent.Length != 1)
            {
                return NativeTemplateInvocationPreparation.Reject("The native project primary output has no exact expanded source snapshot.");
            }

            DotNetTemplateEffectCapability capability = Capability(selected, request.OutputDirectory, project,
                Convert.ToHexString(SHA256.HashData(projectContent[0].Value.Span)), createsProject: true,
                reinitializesProject: request.PreviewAfterDirectoryCleanup);
            request = request with { EffectSupport = new(request.OutputDirectory, project, capability, Confirmed: true) };
            NativeTemplateEffectPlanningResult planned = new NativeTemplateEffectPlanner().PlanDependencies(
                request.OutputDirectory, snapshot.Source, snapshot.Effects.CreationResult, project);
            return await BindAsync(request, capability, planned, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or DotnetCliException)
        {
            return NativeTemplateInvocationPreparation.Reject(exception.Message);
        }
    }

    private DotNetTemplateEffectCapability Capability(ResolvedTemplate selected, WorkingDirectory root,
        string project, string digest, bool createsProject, bool reinitializesProject = false)
        => new(selected, root, project, digest, createsProject, _files, _paths, _preparation, _dependencies, _updater, reinitializesProject);

    private static async Task<NativeTemplateInvocationPreparation> BindAsync(TemplateInvocationRequest request,
        DotNetTemplateEffectCapability capability, NativeTemplateEffectPlanningResult planned, CancellationToken cancellationToken)
    {
        if (planned.Plan is null)
        {
            return NativeTemplateInvocationPreparation.Reject([.. planned.Diagnostics]);
        }

        capability.Bind(planned.Plan);
        IReadOnlyList<string> diagnostics = await capability.ValidateAsync(planned.Plan, cancellationToken);
        return diagnostics.Count > 0 ? NativeTemplateInvocationPreparation.Reject([.. diagnostics]) : NativeTemplateInvocationPreparation.Ready(request);
    }

    private static TemplateEffectSource Source(ResolvedTemplate selected) => new(selected.Entry.Identity,
        selected.ConfigurationSourceIdentity ?? throw new InvalidOperationException("The selected native source identity is missing."),
        selected.ConfigurationSha256 ?? throw new InvalidOperationException("The selected native source fingerprint is missing."));

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
