// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.PowerShell;

/// <summary>
/// Scaffolds a PowerShell Functions project. Currently a stub; full scaffolding
/// lands in a follow-up release.
/// </summary>
internal sealed class PowerShellProjectInitializer : IProjectInitializer
{
    public string Stack => "powershell";

    public string DisplayName => "PowerShell";

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
        new Dictionary<string, IReadOnlyList<string>>()
        {
            { "PowerShell", ["pwsh", "ps"] }
        };

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return [];
    }

    public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parseResult);
        cancellationToken.ThrowIfCancellationRequested();

        throw new NotImplementedException("PowerShell project initialization is not implemented yet.");
    }
}
