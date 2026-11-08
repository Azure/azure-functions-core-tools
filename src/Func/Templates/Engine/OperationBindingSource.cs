// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class OperationBindingSource(string prefix, IReadOnlyDictionary<string, string> bindings) : IBindSymbolSource
{
    public Guid Id { get; } = new("A5793348-65EB-4484-BE1E-F8B65B784FE4");

    public int Priority => 100;

    public string DisplayName => "Operation bindings";

    public string SourcePrefix { get; } = prefix;

    public bool RequiresPrefixMatch => true;

    public Task<string?> GetBoundValueAsync(IEngineEnvironmentSettings settings, string bindName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(bindings.GetValueOrDefault($"{SourcePrefix}:{bindName}"));
    }
}
