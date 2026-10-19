// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python.Shared;

namespace Azure.Functions.Cli.Templates.Python;

/// <summary>
/// Binds native Python append expansion to selected-package metadata and an explicitly approved owned executor.
/// </summary>
internal sealed class PythonSelectedItemEffects(
    IFileSystem files, PythonTemplateSourceReader sources, IPythonTemplateAppendExecutor executor, bool confirmed) : INativeSelectedItemEffects
{
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly PythonTemplateSourceReader _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IPythonTemplateAppendExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));

    public async Task<NativeSelectedItemBinding> ResolveAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return (await _sources.ReadAsync(selected, cancellationToken)).Binding;
        }
        catch (InvalidDataException exception)
        {
            return Failed(exception, cancellationToken);
        }
        catch (JsonException exception)
        {
            return Failed(exception, cancellationToken);
        }
        catch (IOException exception)
        {
            return Failed(exception, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failed(exception, cancellationToken);
        }
    }

    public async Task<TemplateInvocationResult?> InvokeAsync(Templater templater, ResolvedTemplate selected,
        NativeSelectedItemBinding binding, TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templater);
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        NativeSelectedItemBinding current = await ResolveAsync(selected, cancellationToken);
        if (binding.InternalExpansionOnly || binding.Diagnostics.Count > 0 || current.Diagnostics.Count > 0
            || current.InternalExpansionOnly || current.Append is null || current.Append != binding.Append)
        {
            return new(TemplateInvocationStatus.EffectFailure, [], current.Diagnostics.Count > 0
                ? string.Join(" ", current.Diagnostics) : "The Python source-qualified mapping changed or is not an ordinary item. Nothing was generated.");
        }

        NativeItemAppendMapping mapping = current.Append;
        if (mapping.CreateFileNameParameter is { } parameter)
        {
            string? filename = request.Parameters.TryGetValue(parameter, out string? supplied) ? supplied : mapping.CreateTargetFileName;
            string defaultTarget = Path.Combine(request.OutputDirectory.Info.FullName, mapping.CreateTargetFileName);
            bool defaultTargetMissing = !_files.FileExists(defaultTarget) && !_files.DirectoryExists(defaultTarget);
            if (PythonAppFileName.GetDiagnostic(filename, allowExtensionless: defaultTargetMissing) is { } filenameDiagnostic)
            {
                return new(TemplateInvocationStatus.InvalidArguments, [], filenameDiagnostic);
            }

            if (filename != mapping.CreateTargetFileName && defaultTargetMissing)
            {
                return await CreateNamedAppAsync(selected, mapping, request, filename!, cancellationToken);
            }
        }

        PythonAppendCapability capability = new(_files, _sources, _executor, selected, request.OutputDirectory);
        NativeSelectedItemAppendSupport support = new(_files, capability, ResolveAsync, confirmed);
        TemplateInvocationResult? result = await support.InvokeAsync(templater, selected, current, request, cancellationToken);
        return result is { Status: TemplateInvocationStatus.Success } && capability.ValidationNotice is { } notice
            ? result with { Diagnostic = result.Diagnostic is null ? notice : result.Diagnostic + " " + notice } : result;
    }

    private async Task<TemplateInvocationResult> CreateNamedAppAsync(ResolvedTemplate selected, NativeItemAppendMapping mapping,
        TemplateInvocationRequest request, string filename, CancellationToken cancellationToken)
    {
        string target = Path.Combine(request.OutputDirectory.Info.FullName, filename);
        if (request.Force || _files.FileExists(target) || _files.DirectoryExists(target))
        {
            return new(TemplateInvocationStatus.FileConflict, [],
                "Custom Python app filenames support create-only generation. Omit --force and choose a new supported filename; existing files were retained.");
        }
        if (request.SourceSnapshot is not null)
        {
            return new(TemplateInvocationStatus.InvalidArguments, [], "Custom Python app generation cannot replace a supplied source plan.");
        }

        TemplateInvocationRequest planned = request with
        {
            SourceSnapshot = new(selected.Entry.Identity, mapping.ConfigurationSha256,
                [mapping.MappingSource, mapping.CreateSource], [new(mapping.CreateTargetFileName, mapping.CreateSource)]),
        };
        NativeTemplatePreparationResult captured = await selected.CaptureNativePreparationAsync(planned, _files, expandFiles: false, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (captured.Snapshot is not { } snapshot)
        {
            return captured.Failure ?? new(TemplateInvocationStatus.InvalidTemplate, [], "Python filename preview did not produce an owned native output.");
        }
        if (snapshot.Effects.FileChanges.Count != 1 || snapshot.Effects.FileChanges[0].ChangeKind != Microsoft.TemplateEngine.Abstractions.ChangeKind.Create
            || Path.GetFullPath(Path.Combine(request.OutputDirectory.Info.FullName, snapshot.Effects.FileChanges[0].TargetRelativePath))
                != Path.GetFullPath(target))
        {
            return new(TemplateInvocationStatus.InvalidTemplate, [],
                "Custom Python filename generation requires exactly its declared, native-previewed create output.");
        }
        string defaultTarget = Path.Combine(request.OutputDirectory.Info.FullName, mapping.CreateTargetFileName);
        if (_files.FileExists(defaultTarget) || _files.DirectoryExists(defaultTarget))
        {
            return new(TemplateInvocationStatus.FileConflict, [],
                "The canonical Python app target appeared after filename preview. Nothing was generated; retry against the current project.");
        }

        return await selected.InvokeAsync(planned, cancellationToken);
    }

    private static NativeSelectedItemBinding Failed(Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(false, null, [$"Cannot qualify the selected Python item's owned source: {exception.Message} Nothing was generated."]);
    }
}
