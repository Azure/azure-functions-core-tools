// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.TemplateEngine.Abstractions;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Projects native-evaluated dependency actions into a bounded, internal plan without approving execution.
/// </summary>
internal sealed class NativeTemplateEffectPlanner
{
    internal static readonly Guid PackageReferenceActionId = new("B17581D1-C5C9-4489-8F0A-004BE667B814");
    internal static readonly Guid RestoreActionId = new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");
    internal static readonly Guid EditorActionId = new("84C0DA21-51C8-4541-9940-6CA19AF04EE6");

    public NativeTemplateEffectPlanningResult PlanDependencies(WorkingDirectory outputDirectory,
        TemplateEffectSource source, ICreationResult evaluated, string exactProjectPath)
        => PlanDependencies(outputDirectory, source, evaluated, exactProjectPath, null);

    public NativeTemplateEffectPlanningResult PlanFSharpItemDependencies(WorkingDirectory projectDirectory,
        TemplateEffectSource source, ICreationEffects nativeEffects, WorkingDirectory itemOutputDirectory,
        string exactProjectPath, TemplateFSharpCompileEffect repair)
    {
        ArgumentNullException.ThrowIfNull(projectDirectory);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(nativeEffects);
        ArgumentNullException.ThrowIfNull(itemOutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(exactProjectPath);
        ArgumentNullException.ThrowIfNull(repair);
        NativeTemplateEffectPlanningResult structural = PlanFilesAndDependencies(projectDirectory, [repair]);
        if (structural.Plan is null)
        {
            return structural;
        }

        if (!SameSource(source, repair.Source) || !SamePath(exactProjectPath, repair.ProjectPath))
        {
            return new(null, ["F# repair must match the current selected source, configuration fingerprint and exact .fsproj target."]);
        }

        string[] generated = [.. repair.GeneratedFiles.Select(file => Path.GetFullPath(file.Path))];
        IFileChange[] nativeFSharp = [.. nativeEffects.FileChanges.Where(change =>
            string.Equals(Path.GetExtension(change.TargetRelativePath), ".fs", StringComparison.Ordinal))];
        string[] primaryOutputs = [.. nativeEffects.CreationResult.PrimaryOutputs.Select(output =>
            Path.GetFullPath(Path.Combine(itemOutputDirectory.Info.FullName, output.Path)))];
        StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (nativeFSharp.Any(change => change.ChangeKind != ChangeKind.Create)
            || !nativeFSharp.Select(change => Path.GetFullPath(Path.Combine(itemOutputDirectory.Info.FullName, change.TargetRelativePath)))
                .Order(comparer).SequenceEqual(generated.Order(comparer), comparer)
            || !primaryOutputs.Order(comparer).SequenceEqual(generated.Order(comparer), comparer))
        {
            return new(null, ["F# repair requires exactly the qualified native-created .fs files and their evaluated primary outputs; overwrites and inferred files are not supported."]);
        }

        return PlanDependencies(projectDirectory, source, nativeEffects.CreationResult, exactProjectPath, repair);
    }

    private NativeTemplateEffectPlanningResult PlanDependencies(WorkingDirectory outputDirectory,
        TemplateEffectSource source, ICreationResult evaluated, string exactProjectPath, TemplateFSharpCompileEffect? repair)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(evaluated);
        ArgumentException.ThrowIfNullOrWhiteSpace(exactProjectPath);
        if (!Path.IsPathFullyQualified(exactProjectPath)
            || Path.GetExtension(exactProjectPath) is not (".csproj" or ".fsproj"))
        {
            return new(null, ["Dependency effects require one exact absolute .csproj or .fsproj target; no project is inferred."]);
        }

        List<TemplateEffect> effects = [];
        if (repair is not null)
        {
            effects.Add(repair);
        }

        List<string> diagnostics = [];
        foreach (IPostAction action in evaluated.PostActions)
        {
            if (action.ActionId == PackageReferenceActionId)
            {
                if (action.Args.Count != 4 || !action.Args.TryGetValue("referenceType", out string? referenceType) || referenceType != "package"
                    || !action.Args.TryGetValue("reference", out string? package) || !NuGet.Packaging.PackageIdValidator.IsValidPackageId(package)
                    || !action.Args.TryGetValue("version", out string? version) || !NuGetVersion.TryParse(version, out _)
                    || !action.Args.TryGetValue("projectFileExtensions", out string? extension)
                    || extension != Path.GetExtension(exactProjectPath))
                {
                    diagnostics.Add($"Package-reference action '{action.ActionId}' has unsupported arguments or does not match the exact project.");
                    continue;
                }

                effects.Add(new TemplatePackageReferenceEffect(source, exactProjectPath, package, version, extension, action.ContinueOnError));
            }
            else if (action.ActionId == RestoreActionId && action.Args.Count == 0)
            {
                effects.Add(new TemplateRestoreEffect(source, exactProjectPath, action.ContinueOnError));
            }
            else if (action.ActionId == EditorActionId && action.ContinueOnError
                && action.Args.Count == 1 && action.Args.TryGetValue("files", out string? files) && files == "0")
            {
                effects.Add(new TemplateEditorNoOpEffect(source));
            }
            else
            {
                diagnostics.Add($"Native action '{action.ActionId}' is not handled by the controlled dependency capability.");
            }
        }

        return diagnostics.Count > 0 ? new(null, diagnostics.AsReadOnly()) : PlanFilesAndDependencies(outputDirectory, effects);
    }

    public NativeTemplateEffectPlanningResult PlanFilesAndDependencies(WorkingDirectory outputDirectory, IReadOnlyList<TemplateEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(effects);
        List<string> diagnostics = [];
        foreach (TemplateEffect effect in effects)
        {
            ArgumentNullException.ThrowIfNull(effect);
            if (effect is not (TemplateAppendFileEffect or TemplateCreateFileEffect or TemplatePackageReferenceEffect
                or TemplateRestoreEffect or TemplateEditorNoOpEffect or TemplateFSharpCompileEffect))
            {
                diagnostics.Add($"Effect '{effect.GetType().Name}' is unsupported.");
                continue;
            }

            if (effect.Source is null || string.IsNullOrWhiteSpace(effect.Source.TemplateIdentity)
                || string.IsNullOrWhiteSpace(effect.Source.Identity) || !IsSha256(effect.Source.Sha256))
            {
                diagnostics.Add("Every effect requires an exact selected template identity, owned source identity and SHA-256.");
            }

            if (effect is TemplateEditorNoOpEffect)
            {
                continue;
            }

            if (!Path.IsPathFullyQualified(effect.TargetPath))
            {
                diagnostics.Add("Effect targets must be absolute paths.");
                continue;
            }

            string relative = Path.GetRelativePath(outputDirectory.Info.FullName, effect.TargetPath);
            if (Path.IsPathRooted(relative) || relative == ".."
                || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                diagnostics.Add("Effect targets must remain inside the explicitly supplied project boundary.");
            }

            if (effect is TemplateAppendFileEffect append && (!IsSha256(append.ExpectedTargetSha256) || append.Content is null))
            {
                diagnostics.Add("Append effects require expanded content and an expected target SHA-256.");
            }

            if (effect is TemplatePackageReferenceEffect package
                && (!NuGet.Packaging.PackageIdValidator.IsValidPackageId(package.PackageId) || !NuGetVersion.TryParse(package.Version, out _)
                    || package.ProjectFileExtension is not (".csproj" or ".fsproj")
                    || package.ProjectFileExtension != Path.GetExtension(package.ProjectPath)))
            {
                diagnostics.Add("Package effects require an exact package ID, version and matching .csproj or .fsproj target.");
            }

            if (effect is TemplateRestoreEffect restore && Path.GetExtension(restore.ProjectPath) is not (".csproj" or ".fsproj"))
            {
                diagnostics.Add("Restore effects require an exact .csproj or .fsproj target.");
            }

            if (effect is TemplateFSharpCompileEffect compile)
            {
                string projectRoot = Path.GetDirectoryName(Path.GetFullPath(compile.ProjectPath))!;
                StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                if (Path.GetExtension(compile.ProjectPath) != ".fsproj" || !IsSha256(compile.ExpectedProjectSha256)
                    || compile.EvaluatedCompileOrder.Count == 0 || compile.GeneratedFiles.Count == 0
                    || compile.EvaluatedCompileOrder.Any(path => !IsRootFSharpFile(path, projectRoot))
                    || compile.EvaluatedCompileOrder.Select(Path.GetFullPath).Distinct(comparer).Count() != compile.EvaluatedCompileOrder.Count
                    || compile.GeneratedFiles.Any(file => file is null || !IsRootFSharpFile(file.Path, projectRoot)
                        || !IsSha256(file.Sha256) || SamePath(file.Path, compile.EvaluatedCompileOrder[^1]))
                    || compile.GeneratedFiles.Select(file => Path.GetFullPath(file.Path)).Distinct(comparer).Count() != compile.GeneratedFiles.Count)
                {
                    diagnostics.Add("Compile repair requires an exact project digest, a unique root-file sequence and hashed generated files excluding its terminal include.");
                }
            }

            if (effect is TemplateCreateFileEffect create && create.Content is null)
            {
                diagnostics.Add("Create-only effects require source-qualified generated content.");
            }
        }

        return diagnostics.Count > 0 ? new(null, diagnostics.AsReadOnly()) : new(new(outputDirectory, effects), []);
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool SameSource(TemplateEffectSource left, TemplateEffectSource right) =>
        left.TemplateIdentity == right.TemplateIdentity && left.Identity == right.Identity
        && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsRootFSharpFile(string? path, string root) => !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path) && Path.GetExtension(path) == ".fs" && SamePath(Path.GetDirectoryName(path)!, root);
}
