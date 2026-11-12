// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

internal sealed class DotNetProjectStack(IDotnetCliRunner dotnetCli) : IProjectStack
{
    private const string RestorePostActionId = "210D431B-A78B-4D2F-B762-4ED3E3EA9025";

    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));

    public string Stack => "dotnet";

    public string DisplayName => ".NET";

    public IReadOnlyList<string> WorkerRuntimeAliases { get; } = ["dotnet-isolated"];

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        { "C#", ["csharp"] },
        { "F#", ["fsharp"] }
    };

    public async Task RunTemplatePostActionAsync(string actionId, WorkingDirectory projectDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(actionId, RestorePostActionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"The .NET Stack does not support template post-action '{actionId}'.");
        }

        try
        {
            await _dotnetCli.RunAsync(["restore"], projectDirectory.Info.FullName, cancellationToken);
        }
        catch (DotnetCliException ex)
        {
            throw new InvalidOperationException($"The .NET template restore post action failed: {ex.Message}", ex);
        }
    }
}
