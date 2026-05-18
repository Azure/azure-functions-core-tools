// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workload.Go;

/// <summary>
/// Stub <see cref="IProjectInitializer"/> for Go. Scaffolding logic
/// lands in a follow-up PR; this only claims the stack so the host can
/// route <c>func init --stack go</c> here.
/// </summary>
internal sealed class GoProjectInitializer : IProjectInitializer
{
    public string Stack => "go";

    public IReadOnlyList<string> SupportedLanguages { get; } = ["Go"];

    public IReadOnlyList<Option> GetInitOptions() => [];

    public Task InitializeAsync(
        InitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "Go project initialization is not implemented yet.");
    }
}
