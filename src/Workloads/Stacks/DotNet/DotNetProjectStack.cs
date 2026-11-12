// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

internal sealed class DotNetProjectStack(IDotnetCliRunner dotnetCli) : IProjectStack
{
    private const string RestorePostActionId = "210D431B-A78B-4D2F-B762-4ED3E3EA9025";
    private const string AddPackageReferencePostActionId = "B17581D1-C5C9-4489-8F0A-004BE667B814";
    private const string OpenEditorPostActionId = "84C0DA21-51C8-4541-9940-6CA19AF04EE6";

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

    public Task RunTemplatePostActionAsync(string actionId, WorkingDirectory projectDirectory, CancellationToken cancellationToken)
        => RunTemplatePostActionAsync(actionId, new Dictionary<string, string>(), projectDirectory, cancellationToken);

    public async Task RunTemplatePostActionAsync(
        string actionId,
        IReadOnlyDictionary<string, string> arguments,
        WorkingDirectory projectDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(actionId, OpenEditorPostActionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(actionId, AddPackageReferencePostActionId, StringComparison.OrdinalIgnoreCase))
        {
            if (!arguments.TryGetValue("referenceType", out string? referenceType)
                || !string.Equals(referenceType, "package", StringComparison.OrdinalIgnoreCase)
                || !arguments.TryGetValue("reference", out string? package)
                || string.IsNullOrWhiteSpace(package)
                || !arguments.TryGetValue("version", out string? version)
                || string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException("The .NET template package-reference post action is missing package metadata.");
            }

            try
            {
                await _dotnetCli.RunAsync(["add", "package", package, "--version", version], projectDirectory.Info.FullName, cancellationToken);
            }
            catch (DotnetCliException ex)
            {
                throw new InvalidOperationException($"The .NET template package-reference post action failed: {ex.Message}", ex);
            }

            return;
        }

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
