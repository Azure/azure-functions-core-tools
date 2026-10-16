// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Python;

/// <summary>
/// Applies original Python item defaults and regex validation from the exact selected native package.
/// </summary>
internal sealed class PythonItemCompatibility(IInteractionService interaction, PythonTemplateSourceReader sources) : INativeItemCompatibility
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly PythonTemplateSourceReader _sources = sources ?? throw new ArgumentNullException(nameof(sources));

    public async Task<NativeItemCompatibilityResult> ResolveAsync(ResolvedTemplate selected, TemplateCandidateParseResult arguments,
        string? requestedName, bool canPrompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(arguments);
        if (selected.Entry.Type != TemplateType.Item || selected.Entry.Identity != arguments.TemplateIdentity)
        {
            throw new ArgumentException("Python compatibility requires the selected item and its own parsed arguments.", nameof(arguments));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            PythonTemplateSource source = await _sources.ReadAsync(selected, cancellationToken);
            if (source.Compatibility is not { } contract)
            {
                return new(null, "unresolved", ["Internal Python expansion helpers cannot be selected as ordinary items."]);
            }

            NativeItemCompatibility compatibility = new(_interaction, _ => contract, async (owned, path, token) =>
            {
                await _sources.ReadAsync(owned, token);
                return await owned.ReadPackageFileAsync(path, token);
            });
            return await compatibility.ResolveAsync(selected, arguments, requestedName, canPrompt, cancellationToken);
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

    private static NativeItemCompatibilityResult Failed(Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(null, "unresolved", [$"Cannot qualify the selected Python item's owned source: {exception.Message} Nothing was generated."]);
    }
}
