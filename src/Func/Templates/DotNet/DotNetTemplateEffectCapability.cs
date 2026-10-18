// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.DotNet.Shared;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Executes one selected native dependency plan through CLI-owned copies of the shared .NET helpers.
/// </summary>
internal sealed class DotNetTemplateEffectCapability(
    ResolvedTemplate selected, WorkingDirectory root, string projectPath, string expectedProjectSha256, bool createsProject,
    IFileSystem files, FSharpProjectFiles paths, DotNetTemplateProjectPreparation preparation,
    DotNetTemplateDependencyExecutor dependencies, FSharpCompileUpdater updater, bool reinitializesProject = false) : INativeTemplateEffectCapability
{
    private readonly ResolvedTemplate _selected = selected ?? throw new ArgumentNullException(nameof(selected));
    private readonly WorkingDirectory _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly FSharpProjectFiles _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly DotNetTemplateProjectPreparation _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
    private readonly DotNetTemplateDependencyExecutor _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    private readonly FSharpCompileUpdater _updater = updater ?? throw new ArgumentNullException(nameof(updater));
    private readonly string _project = DotNetTemplateDependencyExecutor.ValidateProjectPath(projectPath);
    private string _projectDigest = expectedProjectSha256 ?? throw new ArgumentNullException(nameof(expectedProjectSha256));
    private NativeTemplateEffectPlan? _qualified;
    private FSharpCompileUpdatePlan? _compile;

    public void Bind(NativeTemplateEffectPlan qualified)
    {
        ArgumentNullException.ThrowIfNull(qualified);
        if (_qualified is not null)
        {
            throw new InvalidOperationException("A controlled .NET dependency capability cannot replace its selected native plan.");
        }

        _qualified = qualified;
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (_qualified is null || !SamePath(plan.OutputDirectory.Info.FullName, _root.Info.FullName)
            || !plan.Effects.SequenceEqual(_qualified.Effects)
            || await _selected.ValidateConfigurationAsync(cancellationToken) is not null)
        {
            return ["The controlled .NET dependency plan is unbound or its selected source changed."];
        }

        try
        {
            bool exists = _files.FileExists(_project);
            _paths.ValidatePath(_project, _root.Info.FullName, mustExist: !createsProject || exists);
            if (exists && !(createsProject && reinitializesProject))
            {
                await ValidateProjectAsync(cancellationToken);
            }
            else if (!createsProject || _files.DirectoryExists(_project))
            {
                return ["The selected .NET dependency project is missing or occupied."];
            }

            TemplateFSharpCompileEffect? repair = plan.Effects.OfType<TemplateFSharpCompileEffect>().SingleOrDefault();
            if (repair is not null)
            {
                _compile = await _updater.PlanAsync(_project, repair.ExpectedProjectSha256, repair.EvaluatedCompileOrder,
                    [.. repair.GeneratedFiles.Select(file => new FSharpGeneratedFile(file.Path, file.Sha256,
                        file.ExpectedTargetSha256 is not null))], cancellationToken);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or DotnetCliException)
        {
            return [exception.Message];
        }

        return [];
    }

    public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effect);
        cancellationToken.ThrowIfCancellationRequested();
        if (_qualified is null || !_qualified.Effects.Contains(effect)
            || await _selected.ValidateConfigurationAsync(cancellationToken) is not null)
        {
            return new(false, ["The controlled .NET effect does not belong to the current selected source and plan."]);
        }

        bool attempted = false;
        try
        {
            await ValidateProjectAsync(cancellationToken);
            switch (effect)
            {
                case TemplateFSharpCompileEffect:
                    attempted = true;
                    await _updater.ApplyAsync(_compile ?? throw new InvalidOperationException("F# repair was not preflighted."), cancellationToken);
                    break;
                case TemplatePackageReferenceEffect package:
                    attempted = true;
                    await _dependencies.AddPackageReferenceAsync(_project,
                        new(package.PackageId, package.Version, package.ProjectFileExtension), cancellationToken);
                    break;
                case TemplateRestoreEffect:
                    attempted = true;
                    await _dependencies.RestoreAsync(_project, cancellationToken);
                    break;
                default:
                    return new(false, ["The controlled .NET capability does not execute this effect."]);
            }

            _paths.ValidatePath(_project, _root.Info.FullName, mustExist: true);
            _projectDigest = await _files.ComputeSha256Async(_project, cancellationToken);
            return new(true, []);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or DotnetCliException)
        {
            if (attempted && _files.FileExists(_project))
            {
                _paths.ValidatePath(_project, _root.Info.FullName, mustExist: true);
                _projectDigest = await _files.ComputeSha256Async(_project, cancellationToken);
            }

            return new(false, [exception.Message], TargetMayHaveChanged: attempted);
        }
    }

    private async Task ValidateProjectAsync(CancellationToken cancellationToken)
    {
        _paths.ValidatePath(_project, _root.Info.FullName, mustExist: true);
        string digest = await _files.ComputeSha256Async(_project, cancellationToken);
        if (!string.Equals(digest, _projectDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected .NET project changed outside its qualified dependency sequence.");
        }

        await _preparation.ReadAsync(_project, cancellationToken);
        _paths.ValidatePath(_project, _root.Info.FullName, mustExist: true);
        if (!string.Equals(await _files.ComputeSha256Async(_project, cancellationToken), digest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected .NET project changed during dependency evaluation.");
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
