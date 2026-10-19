// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal sealed record NativeItemAppendMapping(TemplatePackageInfo Package, string ConfigurationSha256,
    TemplateEffectSource MappingSource, TemplateEffectSource CreateSource, string CreateTargetFileName, string AppendTargetFileName,
    string HelperIdentity, string HelperConfigurationSha256, TemplateEffectSource HelperSource, string ExpectedHelperOutput)
{
    public string? CreateFileNameParameter { get; init; }

    public string? SelectedAppendFileNameParameter { get; init; }
}

internal sealed record NativeSelectedItemBinding(
    bool InternalExpansionOnly, NativeItemAppendMapping? Append, IReadOnlyList<string> Diagnostics);

internal interface INativeSelectedItemEffects
{
    public Task<NativeSelectedItemBinding> ResolveAsync(ResolvedTemplate selected, CancellationToken cancellationToken);

    public Task<TemplateInvocationResult?> InvokeAsync(Templater templater, ResolvedTemplate selected,
        NativeSelectedItemBinding binding, TemplateInvocationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Expands an explicitly mapped internal helper through the existing native engine before planning a live append.
/// </summary>
internal sealed class NativeSelectedItemAppendSupport(IFileSystem fileSystem, INativeTemplateEffectCapability capability,
    Func<ResolvedTemplate, CancellationToken, Task<NativeSelectedItemBinding>> bindings, bool confirmed) : INativeSelectedItemEffects
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly INativeTemplateEffectCapability _capability = capability ?? throw new ArgumentNullException(nameof(capability));
    private readonly Func<ResolvedTemplate, CancellationToken, Task<NativeSelectedItemBinding>> _bindings =
        bindings ?? throw new ArgumentNullException(nameof(bindings));

    public Task<NativeSelectedItemBinding> ResolveAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        return _bindings(selected, cancellationToken);
    }

    public async Task<TemplateInvocationResult?> InvokeAsync(Templater templater, ResolvedTemplate selected,
        NativeSelectedItemBinding binding, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templater);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        cancellationToken.ThrowIfCancellationRequested();
        request = request with { Parameters = new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal) };
        if (binding.InternalExpansionOnly || binding.Diagnostics.Count > 0)
        {
            return Failure("An internal expansion role or invalid source mapping cannot be invoked as an ordinary item.");
        }

        if (binding.Append is not { } mapping)
        {
            return null;
        }

        TemplateInvocationResult? configurationFailure = await selected.ValidateConfigurationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        if (selected.Entry.Type != TemplateType.Item || mapping.Package != selected.Entry.Package
            || !string.Equals(mapping.ConfigurationSha256, selected.ConfigurationSha256, StringComparison.OrdinalIgnoreCase)
            || mapping.MappingSource.TemplateIdentity != selected.Entry.Identity || mapping.CreateSource.TemplateIdentity != selected.Entry.Identity
            || mapping.HelperSource.TemplateIdentity != mapping.HelperIdentity
            || !IsFileName(mapping.CreateTargetFileName) || !IsFileName(mapping.AppendTargetFileName) || !IsFileName(mapping.ExpectedHelperOutput))
        {
            return Failure("The selected item, package/version, source configuration or exact output mapping changed.");
        }

        if (!await SourceMatchesAsync(selected, mapping.MappingSource, cancellationToken)
            || !await SourceMatchesAsync(selected, mapping.CreateSource, cancellationToken))
        {
            return Failure("The selected item's owned mapping or create source is missing or changed.");
        }

        string createTarget = Path.Combine(request.OutputDirectory.Info.FullName, mapping.CreateTargetFileName);
        if (_fileSystem.DirectoryExists(createTarget))
        {
            return Failure("The mapped create target is an existing directory; nothing was generated.");
        }

        if (!_fileSystem.FileExists(createTarget))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Failure("Resolve and validate the original item's effective name before internal expansion; nothing was generated.");
        }

        string target = Path.Combine(request.OutputDirectory.Info.FullName, mapping.AppendTargetFileName);
        if (!_fileSystem.FileExists(target))
        {
            return Failure("The existing create target selects append, but the original append target is absent; nothing was generated.");
        }

        string expectedTargetSha256;
        try
        {
            expectedTargetSha256 = await _fileSystem.ComputeSha256Async(target, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure($"Cannot read the existing append target: {exception.Message} Verify file access, then retry. Nothing was appended.");
        }
        TemplateResolutionResult resolution = await templater.ResolveGroupAsync(mapping.HelperIdentity, TemplateType.Item, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolution.Status != TemplateResolutionStatus.Success || resolution.Group is not { Count: 1 } group)
        {
            return Failure($"The exact internal helper is not uniquely available: {resolution.Status}.");
        }

        ResolvedTemplate helper = group[0];
        NativeSelectedItemBinding role = await _bindings(helper, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (helper.Entry.Identity != mapping.HelperIdentity || helper.Entry.Package != mapping.Package
            || !string.Equals(helper.ConfigurationSha256, mapping.HelperConfigurationSha256, StringComparison.OrdinalIgnoreCase) || !helper.Entry.IsHidden
            || helper.Entry.Language != selected.Entry.Language || !role.InternalExpansionOnly || role.Diagnostics.Count > 0
            || !await SourceMatchesAsync(helper, mapping.HelperSource, cancellationToken))
        {
            return Failure("The helper role, identity, package/version, language, configuration or owned source changed.");
        }

        using TempDirectory temporary = _fileSystem.CreateTempDirectory();
        var output = WorkingDirectory.FromExplicit(temporary.Path);
        TemplateInvocationResult expansion = await helper.InvokeAsync(new(output, request.Parameters, request.Name), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string expectedOutput = Path.Combine(output.Info.FullName, mapping.ExpectedHelperOutput);
        if (expansion.Status != TemplateInvocationStatus.Success || expansion.Changes.Count != 1
            || Path.GetFullPath(Path.Combine(output.Info.FullName, expansion.Changes[0].Path)) != expectedOutput
            || !_fileSystem.FileExists(expectedOutput) || _fileSystem.DirectoryExists(expectedOutput)
            || !_fileSystem.GetFileSystemEntries(output.Info.FullName).SequenceEqual([expectedOutput], StringComparer.Ordinal))
        {
            return Failure("Internal native expansion did not produce exactly the expected regular file; no live append was attempted.");
        }

        string? content = await _fileSystem.ReadAllTextIfExistsAsync(expectedOutput, cancellationToken);
        if (content is null || !await SourceMatchesAsync(helper, mapping.HelperSource, cancellationToken)
            || !await SourceMatchesAsync(selected, mapping.MappingSource, cancellationToken)
            || !await SourceMatchesAsync(selected, mapping.CreateSource, cancellationToken))
        {
            return Failure("An owned expansion source or the expected output changed; no live append was attempted.");
        }

        configurationFailure = await selected.ValidateConfigurationAsync(cancellationToken)
            ?? await helper.ValidateConfigurationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        TemplateAppendFileEffect append = new(mapping.HelperSource, target, content, expectedTargetSha256);
        NativeTemplateEffectPlanningResult planned = new NativeTemplateEffectPlanner().PlanFilesAndDependencies(
            templater.Context.Project?.RootDirectory ?? request.OutputDirectory, [append]);
        if (planned.Plan is null)
        {
            return Failure(string.Join(" ", planned.Diagnostics));
        }

        NativeTemplateEffectResult applied = await new NativeTemplateEffectWorkflow().ExecuteAsync(
            planned.Plan, _capability, request.DryRun, confirmed, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(applied.Succeeded ? TemplateInvocationStatus.Success : TemplateInvocationStatus.EffectFailure,
            request.DryRun || applied.CompletedEffects.Count > 0 ? [new(mapping.AppendTargetFileName, TemplateFileChangeKind.Append)] : [],
            applied.Diagnostics.Count == 0 ? null : string.Join(" ", applied.Diagnostics))
        {
            Effects = applied,
        };
    }

    private static async Task<bool> SourceMatchesAsync(ResolvedTemplate selected, TemplateEffectSource source, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = await selected.ReadPackageFileAsync(source.Identity, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return string.Equals(Convert.ToHexString(SHA256.HashData(bytes.Span)), source.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileName(string value) => !string.IsNullOrWhiteSpace(value)
        && !Path.IsPathRooted(value) && !value.Contains('/') && !value.Contains('\\') && value is not ("." or "..");

    private static TemplateInvocationResult Failure(string diagnostic) => new(TemplateInvocationStatus.EffectFailure, [], diagnostic);
}
