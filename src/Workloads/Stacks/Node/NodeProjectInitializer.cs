// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Stub <see cref="IProjectInitializer"/> for Node. Scaffolding logic
/// lands in a follow-up PR; this only claims the stack so the host can
/// route <c>func init --stack node</c> here.
/// </summary>
internal sealed class NodeProjectInitializer : IProjectInitializer
{
    public string Stack => "node";

    public IReadOnlyList<string> SupportedLanguages { get; } = ["JavaScript", "TypeScript"];

    public IReadOnlyList<Option> GetInitOptions() => [];

    public Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "Node project initialization is not implemented yet.");
    }
}
