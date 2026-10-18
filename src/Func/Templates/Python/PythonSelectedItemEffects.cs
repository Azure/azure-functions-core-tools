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

        PythonAppendCapability capability = new(_files, _sources, _executor, selected, request.OutputDirectory);
        NativeSelectedItemAppendSupport support = new(_files, capability, ResolveAsync, confirmed);
        TemplateInvocationResult? result = await support.InvokeAsync(templater, selected, current, request, cancellationToken);
        return result is { Status: TemplateInvocationStatus.Success } && capability.ValidationNotice is { } notice
            ? result with { Diagnostic = result.Diagnostic is null ? notice : result.Diagnostic + " " + notice } : result;
    }

    private static NativeSelectedItemBinding Failed(Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(false, null, [$"Cannot qualify the selected Python item's owned source: {exception.Message} Nothing was generated."]);
    }
}
