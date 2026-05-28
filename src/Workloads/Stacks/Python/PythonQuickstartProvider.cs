// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Quickstart provider for the Python stack.
/// </summary>
internal sealed class PythonQuickstartProvider : IQuickstartProvider
{
    public string Stack => "python";

    public string DisplayName => "Python";

    public IReadOnlyList<string> ManifestLanguages { get; } = ["Python"];

    public string GetDisplayLanguage(string manifestLanguage) => manifestLanguage;

    public string? ResolveManifestLanguage(string userInput) => userInput.ToLowerInvariant() switch
    {
        "python" or "py" => "Python",
        _ => null
    };

    public IReadOnlyList<string> GetNextSteps(string manifestLanguage) =>
    [
        "Run `pip install -r requirements.txt` to install dependencies",
        "Verify `local.settings.json` exists and has the required settings values",
        "Run `func run` to launch the app"
    ];
}
