// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Quickstart;

internal interface IQuickstartProviderResolver
{
    public Task<IQuickstartProvider?> SelectProviderAsync(string? requestedStack, string actionDescription, CancellationToken cancellationToken);

    public Task<LanguageDescriptor?> ResolveOrPromptLanguageAsync(string? requestedLanguage, IQuickstartProvider provider, QuickstartManifest manifest, CancellationToken cancellationToken);

    public (IQuickstartProvider Provider, LanguageDescriptor Language)? FindUniqueProviderForLanguage(string manifestLanguage);
}
