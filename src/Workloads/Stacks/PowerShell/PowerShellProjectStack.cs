// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.PowerShell;

internal sealed class PowerShellProjectStack : IProjectStack
{
    public string Stack => "powershell";

    public string DisplayName => "PowerShell";

    public IReadOnlyList<string> WorkerRuntimeAliases => [];

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        { "PowerShell", ["pwsh", "ps"] }
    };
}
