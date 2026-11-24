// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Quickstart provider for the Python stack.
/// </summary>
internal sealed class PythonQuickstartProvider(PythonProjectStack stack) : IQuickstartProvider
{
    public IProjectStack Stack { get; } = stack ?? throw new ArgumentNullException(nameof(stack));

    public IReadOnlyList<string> GetNextSteps(LanguageDescriptor language) =>
    [
        "Run `pip install -r requirements.txt` to install dependencies",
        "Run `func run [path]` where path is folder containing host.json to launch the app"
    ];
}
