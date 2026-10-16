// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Python;

/// <summary>
/// Qualifies the packaged Python basic project without adding dependency installation or legacy initializer effects.
/// </summary>
internal sealed class PythonProjectWorkflowProvider(PythonTemplateSourceReader sources) : INativeProjectWorkflowProvider
{
    private readonly PythonTemplateSourceReader _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    public string Stack => "python";

    public bool Handles(TemplateCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Identity == "Azure.Functions.Python.Basic" && entry.Type == TemplateType.Project
            && entry.Language == "Python" && entry.Package?.Identifier == PythonTemplateSourceReader.PackageId;
    }

    public async Task<DotNetProjectLayoutQualification?> QualifyLayoutAsync(NativeProjectTemplateSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await _sources.ValidateProjectAsync(source.Entry, source.Configuration.Fingerprint, source.ReadPackageFileAsync, cancellationToken);
            return null;
        }
        catch (InvalidDataException exception)
        {
            throw Invalid(exception);
        }
        catch (JsonException exception)
        {
            throw Invalid(exception);
        }
        catch (IOException exception)
        {
            throw Invalid(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Invalid(exception);
        }
    }

    public async Task<NativeTemplateInvocationPreparation> PrepareAsync(NativeProjectWorkflowContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            TemplateInvocationResult? changed = await context.SelectedTemplate.ValidateConfigurationAsync(cancellationToken);
            if (changed is not null)
            {
                return NativeTemplateInvocationPreparation.Reject(changed.Diagnostic ?? "The selected Python project changed.");
            }

            await _sources.ValidateProjectAsync(context.SelectedTemplate.Entry, context.SelectedTemplate.ConfigurationSha256!,
                context.SelectedTemplate.ReadPackageFileAsync, cancellationToken);
            return NativeTemplateInvocationPreparation.Ready(context.Invocation);
        }
        catch (InvalidDataException exception)
        {
            return NativeTemplateInvocationPreparation.Reject(Invalid(exception).Message);
        }
        catch (JsonException exception)
        {
            return NativeTemplateInvocationPreparation.Reject(Invalid(exception).Message);
        }
        catch (IOException exception)
        {
            return NativeTemplateInvocationPreparation.Reject(Invalid(exception).Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return NativeTemplateInvocationPreparation.Reject(Invalid(exception).Message);
        }
    }

    private static InvalidTemplateMetadataException Invalid(Exception exception) =>
        new($"Cannot qualify the selected Python project's owned sources: {exception.Message}", exception);
}
