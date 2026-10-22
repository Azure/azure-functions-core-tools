// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Native;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Validates one source-bound Python app append without granting permission for other effects or targets.
/// </summary>
internal sealed class PythonAppendCapability(PythonTemplateSourceReader sources, IPythonTemplateAppendExecutor executor,
    NativeTemplateSource selected, WorkingDirectory output, PythonAppendTargetGuard targetGuard) : INativeTemplateAppendCapability
{
    private readonly PythonTemplateSourceReader _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IPythonTemplateAppendExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly NativeTemplateSource _selected = selected ?? throw new ArgumentNullException(nameof(selected));
    private readonly WorkingDirectory _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly PythonAppendTargetGuard _targetGuard = targetGuard ?? throw new ArgumentNullException(nameof(targetGuard));

    public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateAppendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            PythonTemplateSource source = await _sources.ReadAsync(_selected, cancellationToken);
            if (source.Description.InternalExpansionOnly || source.Description.Append is not { } mapping
                || request.Source != mapping.HelperSource || string.IsNullOrWhiteSpace(request.GeneratedSnippet)
                || !SamePath(request.TargetPath, Path.Combine(_output.Info.FullName, mapping.AppendTargetFileName)))
            {
                return ["Only the exact source-qualified Python app append is supported."];
            }

            string relative = Path.GetRelativePath(request.PlanOutputDirectory.Info.FullName, request.TargetPath);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return ["The approved Python append target is missing or outside the selected project boundary."];
            }

            string? diagnostic = await _targetGuard.ValidateAsync(
                _output.Info.FullName, request.TargetPath, request.ExpectedTargetSha256, cancellationToken);
            return diagnostic is null ? [] : [diagnostic];
        }
        catch (InvalidDataException exception)
        {
            return Failure(exception, cancellationToken);
        }
        catch (JsonException exception)
        {
            return Failure(exception, cancellationToken);
        }
        catch (IOException exception)
        {
            return Failure(exception, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(exception, cancellationToken);
        }
    }

    public async Task<NativeTemplateAppendResult> ExecuteAsync(NativeTemplateAppendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> diagnostics = await ValidateAsync(request, cancellationToken);
        if (diagnostics.Count > 0)
        {
            return new(false, diagnostics);
        }

        try
        {
            await _executor.AppendAsync(request.TargetPath, request.GeneratedSnippet, request.ExpectedTargetSha256, cancellationToken);
            return new(true, []);
        }
        catch (InvalidOperationException exception)
        {
            return new(false, [exception.Message]);
        }
        catch (IOException exception)
        {
            return new(false, [exception.Message], TargetMayHaveChanged: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(false, [exception.Message], TargetMayHaveChanged: true);
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static IReadOnlyList<string> Failure(Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return [$"Cannot validate the Python append source or target: {exception.Message} Nothing was appended."];
    }
}
