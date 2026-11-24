// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Quickstart provider for the .NET stack.
/// </summary>
internal sealed class DotNetQuickstartProvider(DotNetProjectStack stack) : IQuickstartProvider
{
    public IProjectStack Stack { get; } = stack ?? throw new ArgumentNullException(nameof(stack));

    public IReadOnlyList<string> GetNextSteps(LanguageDescriptor language) =>
    [
        "Run `func run [path]` where path is folder containing host.json to launch the app"
    ];
}
