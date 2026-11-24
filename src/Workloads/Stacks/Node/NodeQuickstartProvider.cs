// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Quickstart provider for the Node stack (JavaScript and TypeScript).
/// </summary>
internal sealed class NodeQuickstartProvider(NodeProjectStack stack) : IQuickstartProvider
{
    public IProjectStack ProjectStack { get; } = stack ?? throw new ArgumentNullException(nameof(stack));

    public IReadOnlyList<string> GetNextSteps(LanguageDescriptor language) =>
    [
        "Run `npm install` to install dependencies",
        "Run `func run [path]` where path is folder containing host.json to launch the app"
    ];
}
