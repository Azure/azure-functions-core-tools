// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Python.Shared;

namespace Azure.Functions.Cli.Templates.Python;

/// <summary>
/// Validates one source-bound Python app append without granting permission for other effects or targets.
/// </summary>
internal sealed class PythonAppendCapability(IFileSystem files, PythonTemplateSourceReader sources,
    IPythonTemplateAppendExecutor executor, ResolvedTemplate selected, WorkingDirectory output,
    PythonAppendTargetGuard? targetGuard = null) : INativeTemplateEffectCapability
{
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly PythonTemplateSourceReader _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IPythonTemplateAppendExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly ResolvedTemplate _selected = selected ?? throw new ArgumentNullException(nameof(selected));
    private readonly WorkingDirectory _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly PythonAppendTargetGuard _targetGuard = targetGuard ?? new();

    public string? ValidationNotice { get; private set; }

    public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        ValidationNotice = null;
        try
        {
            PythonTemplateSource source = await _sources.ReadAsync(_selected, cancellationToken);
            if (source.Binding.InternalExpansionOnly || source.Binding.Append is not { } mapping || plan.Effects.Count != 1
                || plan.Effects[0] is not TemplateAppendFileEffect append || append.Source != mapping.HelperSource
                || append.Content is null || string.IsNullOrWhiteSpace(append.Content)
                || !SamePath(append.TargetPath, Path.Combine(_output.Info.FullName, mapping.AppendTargetFileName)))
            {
                return ["Only the exact source-qualified Python app append is supported."];
            }

            string relative = Path.GetRelativePath(plan.OutputDirectory.Info.FullName, append.TargetPath);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || !_files.FileExists(append.TargetPath) || _files.DirectoryExists(append.TargetPath))
            {
                return ["The approved Python append target is missing or outside the selected project boundary."];
            }

            string? pathDiagnostic = _targetGuard.GetDiagnostic(_output.Info.FullName, append.TargetPath);
            if (pathDiagnostic is not null)
            {
                return [pathDiagnostic];
            }

            string actual = await _files.ComputeSha256Async(append.TargetPath, cancellationToken);
            pathDiagnostic = _targetGuard.GetDiagnostic(_output.Info.FullName, append.TargetPath);
            if (pathDiagnostic is not null)
            {
                return [pathDiagnostic];
            }

            if (!string.Equals(actual, append.ExpectedTargetSha256, StringComparison.OrdinalIgnoreCase))
            {
                return ["The Python app changed after preview. Nothing was appended."];
            }

            string? original = await _files.ReadAllTextIfExistsAsync(append.TargetPath, cancellationToken);
            if (original is null)
            {
                return ["The approved Python app is missing. Nothing was appended."];
            }

            PythonFunctionNameValidation names = new PythonFunctionNameGuard().Analyze(original, append.Content, cancellationToken);
            ValidationNotice = names.Complete ? null : PythonFunctionNameGuard.ValidationLimitNotice;
            return names.Diagnostic is null ? [] : [names.Diagnostic];
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

    public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(effect);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> diagnostics = await ValidateAsync(new(_output, [effect]), cancellationToken);
        if (diagnostics.Count > 0)
        {
            return new(false, diagnostics);
        }

        var append = (TemplateAppendFileEffect)effect;
        try
        {
            await _executor.AppendAsync(append.TargetPath, append.Content, append.ExpectedTargetSha256, cancellationToken);
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
