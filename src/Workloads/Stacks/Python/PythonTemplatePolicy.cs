// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Native;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Qualifies the Python stack's bundled native templates and supplies its source-bound append capability.
/// </summary>
internal sealed class PythonTemplatePolicy(
    PythonTemplateSourceReader sources, IPythonTemplateAppendExecutor executor, PythonAppendTargetGuard targetGuard) : NativeTemplatePolicy
{
    private readonly PythonTemplateSourceReader _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    private readonly IPythonTemplateAppendExecutor _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly PythonAppendTargetGuard _targetGuard = targetGuard ?? throw new ArgumentNullException(nameof(targetGuard));

    public override string Stack => "python";

    public override bool Handles(NativeTemplateSource selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        return selected.PackageId == PythonTemplateSourceReader.PackageId && selected.Language == "Python"
            && (!selected.IsProject || selected.Identity == "Azure.Functions.Python.Basic");
    }

    public override async Task<NativeItemPolicyDescription> DescribeItemAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return (await _sources.ReadAsync(selected, cancellationToken)).Description;
        }
        catch (InvalidDataException exception)
        {
            throw Invalid("item", exception, cancellationToken);
        }
        catch (IOException exception)
        {
            throw Invalid("item", exception, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw Invalid("item", exception, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Invalid("item", exception, cancellationToken);
        }
    }

    public override async Task<IReadOnlyList<string>> ValidateProjectAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _sources.ValidateProjectAsync(selected, cancellationToken);
            return [];
        }
        catch (InvalidDataException exception)
        {
            throw Invalid("project", exception, cancellationToken);
        }
        catch (IOException exception)
        {
            throw Invalid("project", exception, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw Invalid("project", exception, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Invalid("project", exception, cancellationToken);
        }
    }

    public override INativeTemplateAppendCapability CreateAppendCapability(NativeTemplateSource selected, WorkingDirectory invocationOutputDirectory)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(invocationOutputDirectory);
        return new PythonAppendCapability(_sources, _executor, selected, invocationOutputDirectory, _targetGuard);
    }

    private static InvalidDataException Invalid(string kind, Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new($"Cannot qualify the selected Python {kind}'s owned sources: {exception.Message}", exception);
    }
}
